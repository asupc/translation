#include "translation_core.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <exception>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>

#include <ctranslate2/translator.h>
#include <sentencepiece_processor.h>

#ifndef TRANSLATION_CORE_VERSION
#define TRANSLATION_CORE_VERSION "0.1.0"
#endif

namespace {

// 每个 batch 最多容纳的行数：限制大段粘贴文本的峰值内存。
constexpr size_t kMaxBatchLines = 64;

}  // namespace

struct TranslationEngine {
  std::unique_ptr<sentencepiece::SentencePieceProcessor> source;
  std::unique_ptr<sentencepiece::SentencePieceProcessor> target;
  std::unique_ptr<ctranslate2::Translator> translator;
  // CTranslate2 的 Translator 可并发调用，但 SentencePieceProcessor 的
  // Encode/Decode 并发安全未在本项目固定版本中验证；串行化单引擎调用。
  std::mutex mutex;
};

namespace {

void set_error(char* buffer, int size, const std::string& message) {
  if (buffer == nullptr || size <= 0) {
    return;
  }
  std::snprintf(buffer, static_cast<size_t>(size), "%s", message.c_str());
}

// 模型目录来自 C# 的 UTF-8 字符串；只做字符串拼接，避免 Windows 上
// std::filesystem 按 ANSI 代码页转换而损坏中文路径。
std::string join_path(const std::string& directory, const char* file) {
  if (directory.empty()) {
    return file;
  }
  const char last = directory.back();
  if (last == '/' || last == '\\') {
    return directory + file;
  }
  return directory + "/" + file;
}

std::string take_error_message(const char* prefix, const std::exception& error) {
  return std::string(prefix) + ": " + error.what();
}

// 按换行符（\r\n 或 \n）拆分为多行，保留空行占位；newline 返回重组译文
// 应使用的换行符，原文含 \r\n 则沿用 \r\n，否则用 \n。
std::vector<std::string> split_lines(const std::string& text, std::string& newline) {
  newline = text.find("\r\n") != std::string::npos ? "\r\n" : "\n";

  std::vector<std::string> lines;
  std::string line;
  for (size_t i = 0; i < text.size(); ++i) {
    const char c = text[i];
    if (c == '\n' || c == '\r') {
      lines.push_back(line);
      line.clear();
      if (c == '\r' && i + 1 < text.size() && text[i + 1] == '\n') {
        ++i;
      }
    } else {
      line += c;
    }
  }
  lines.push_back(line);
  return lines;
}

bool is_blank(const std::string& line) {
  return line.find_first_not_of(" \t") == std::string::npos;
}

}  // namespace

extern "C" {

TranslationEngine* TRANSLATION_CORE_CALL translation_engine_create(
    const char* model_dir,
    int num_threads,
    char* error_message,
    int error_message_size) {
  if (model_dir == nullptr) {
    set_error(error_message, error_message_size, "model_dir is null");
    return nullptr;
  }

  const int threads = num_threads > 0 ? num_threads : 8;

  try {
    auto engine = std::make_unique<TranslationEngine>();

    const std::string source_path = join_path(model_dir, "source.spm");
    engine->source = std::make_unique<sentencepiece::SentencePieceProcessor>();
    if (const auto status = engine->source->Load(source_path); !status.ok()) {
      throw std::runtime_error(
          "failed to load " + source_path + ": " + std::string(status.message()));
    }

    const std::string target_path = join_path(model_dir, "target.spm");
    engine->target = std::make_unique<sentencepiece::SentencePieceProcessor>();
    if (const auto status = engine->target->Load(target_path); !status.ok()) {
      throw std::runtime_error(
          "failed to load " + target_path + ": " + std::string(status.message()));
    }

    ctranslate2::ReplicaPoolConfig config;
    config.num_threads_per_replica = static_cast<size_t>(threads);

    // 参数顺序（CTranslate2 v4.8.3 ReplicaPool 构造函数）：
    // model_path, device, compute_type, device_indices, tensor_parallel, config。
    // DEFAULT 会按模型权重自动选择计算类型：int8 量化模型使用 INT8，
    // 非量化模型使用 float32，避免硬编码 INT8 导致加载失败。
    engine->translator = std::make_unique<ctranslate2::Translator>(
        model_dir,
        ctranslate2::Device::CPU,
        ctranslate2::ComputeType::DEFAULT,
        std::vector<int>{0},
        /*tensor_parallel=*/false,
        config);

    return engine.release();
  } catch (const std::exception& error) {
    set_error(error_message, error_message_size,
              take_error_message("create failed", error));
    return nullptr;
  } catch (...) {
    set_error(error_message, error_message_size, "create failed: unknown error");
    return nullptr;
  }
}

char* TRANSLATION_CORE_CALL translation_engine_translate(
    TranslationEngine* engine,
    const char* text,
    int beam_size,
    int max_decoding_length,
    char* error_message,
    int error_message_size) {
  if (engine == nullptr || text == nullptr) {
    set_error(error_message, error_message_size, "engine or text is null");
    return nullptr;
  }

  try {
    std::lock_guard<std::mutex> lock(engine->mutex);

    // 句子级模型不保留换行（SentencePiece 会把换行当空白吃掉）：
    // 按行拆分批量翻译，再按原换行结构拼回；max_decoding_length 也随之
    // 作用于单行而非整段，避免多行文本被截断。
    std::string newline;
    const std::vector<std::string> lines = split_lines(text, newline);

    std::vector<std::string> translated(lines.size());

    ctranslate2::TranslationOptions options;
    options.beam_size = static_cast<size_t>(beam_size > 0 ? beam_size : 2);
    options.max_decoding_length =
        static_cast<size_t>(max_decoding_length > 0 ? max_decoding_length : 256);

    std::vector<std::vector<std::string>> pending;
    std::vector<size_t> pending_lines;
    pending.reserve(kMaxBatchLines);
    pending_lines.reserve(kMaxBatchLines);

    // 翻译并写回当前批次的结果；批次上限避免整篇文本同时展开成 token 矩阵。
    const auto flush = [&]() {
      if (pending.empty()) {
        return;
      }

      const auto results = engine->translator->translate_batch(pending, options);
      if (results.size() != pending.size()) {
        throw std::runtime_error("translation returned unexpected result count");
      }

      for (size_t i = 0; i < results.size(); ++i) {
        std::vector<std::string> target_tokens;
        for (const auto& token : results[i].output()) {
          if (token != "</s>" && token != "<pad>") {
            target_tokens.push_back(token);
          }
        }

        if (!engine->target->Decode(target_tokens, &translated[pending_lines[i]]).ok()) {
          throw std::runtime_error("target detokenization failed");
        }
      }

      pending.clear();
      pending_lines.clear();
    };

    for (size_t i = 0; i < lines.size(); ++i) {
      if (is_blank(lines[i])) {
        continue;
      }

      std::vector<std::string> source_tokens;
      if (!engine->source->Encode(lines[i], &source_tokens).ok()) {
        throw std::runtime_error("source tokenization failed");
      }
      source_tokens.push_back("</s>");
      pending.push_back(std::move(source_tokens));
      pending_lines.push_back(i);

      if (pending.size() >= kMaxBatchLines) {
        flush();
      }
    }
    flush();

    std::string output;
    output.reserve(std::strlen(text));
    for (size_t i = 0; i < lines.size(); ++i) {
      if (i > 0) {
        output += newline;
      }
      output += translated[i];
    }

    char* result = static_cast<char*>(std::malloc(output.size() + 1));
    if (result == nullptr) {
      throw std::bad_alloc();
    }
    std::memcpy(result, output.c_str(), output.size() + 1);
    return result;
  } catch (const std::exception& error) {
    set_error(error_message, error_message_size,
              take_error_message("translate failed", error));
    return nullptr;
  } catch (...) {
    set_error(error_message, error_message_size, "translate failed: unknown error");
    return nullptr;
  }
}

void TRANSLATION_CORE_CALL translation_engine_free_string(char* text) {
  std::free(text);
}

void TRANSLATION_CORE_CALL translation_engine_destroy(TranslationEngine* engine) {
  try {
    delete engine;
  } catch (...) {
    // 析构不应抛异常；即便底层库违规，也不能让异常穿过 C ABI。
  }
}

const char* TRANSLATION_CORE_CALL translation_engine_version(void) {
  return TRANSLATION_CORE_VERSION;
}

}  // extern "C"
