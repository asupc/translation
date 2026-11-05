#include "translation_core.h"

#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <exception>
#include <filesystem>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <vector>

#include <ctranslate2/translator.h>
#include <sentencepiece_processor.h>

struct TranslationEngine {
  std::unique_ptr<sentencepiece::SentencePieceProcessor> source;
  std::unique_ptr<sentencepiece::SentencePieceProcessor> target;
  std::unique_ptr<ctranslate2::Translator> translator;
  std::mutex mutex;
};

namespace {

void set_error(char* buffer, int size, const std::string& message) {
  if (buffer == nullptr || size <= 0) {
    return;
  }
  std::snprintf(buffer, static_cast<size_t>(size), "%s", message.c_str());
}

std::string join_path(const std::string& directory, const char* file) {
  return (std::filesystem::path(directory) / file).string();
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

TranslationEngine* translation_engine_create(
    const char* model_dir,
    int num_threads,
    char* error_message,
    int error_message_size) {
  if (model_dir == nullptr) {
    set_error(error_message, error_message_size, "model_dir is null");
    return nullptr;
  }

  const int threads = num_threads > 0 ? num_threads : 8;
#ifdef _WIN32
  _putenv_s("OMP_NUM_THREADS", std::to_string(threads).c_str());
#endif

  try {
    auto engine = std::make_unique<TranslationEngine>();

    engine->source = std::make_unique<sentencepiece::SentencePieceProcessor>();
    if (!engine->source->Load(join_path(model_dir, "source.spm")).ok()) {
      throw std::runtime_error("failed to load source.spm");
    }

    engine->target = std::make_unique<sentencepiece::SentencePieceProcessor>();
    if (!engine->target->Load(join_path(model_dir, "target.spm")).ok()) {
      throw std::runtime_error("failed to load target.spm");
    }

    ctranslate2::ReplicaPoolConfig config;
    config.num_threads_per_replica = static_cast<size_t>(threads);
    engine->translator = std::make_unique<ctranslate2::Translator>(
        model_dir,
        ctranslate2::Device::CPU,
        ctranslate2::ComputeType::INT8,
        std::vector<int>{0},
        false,
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

char* translation_engine_translate(
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

    std::vector<std::vector<std::string>> batch;
    std::vector<size_t> batch_line;  // batch 中各项对应 lines 的下标
    for (size_t i = 0; i < lines.size(); ++i) {
      if (is_blank(lines[i])) {
        continue;
      }

      std::vector<std::string> source_tokens;
      if (!engine->source->Encode(lines[i], &source_tokens).ok()) {
        throw std::runtime_error("source tokenization failed");
      }
      source_tokens.push_back("</s>");
      batch.push_back(std::move(source_tokens));
      batch_line.push_back(i);
    }

    std::vector<std::string> translated(lines.size());
    if (!batch.empty()) {
      ctranslate2::TranslationOptions options;
      options.beam_size = static_cast<size_t>(beam_size > 0 ? beam_size : 2);
      options.max_decoding_length =
          static_cast<size_t>(max_decoding_length > 0 ? max_decoding_length : 256);

      const auto results = engine->translator->translate_batch(batch, options);
      if (results.size() != batch.size()) {
        throw std::runtime_error("translation returned unexpected result count");
      }

      for (size_t i = 0; i < results.size(); ++i) {
        std::vector<std::string> target_tokens;
        for (const auto& token : results[i].output()) {
          if (token != "</s>" && token != "<pad>") {
            target_tokens.push_back(token);
          }
        }

        if (!engine->target->Decode(target_tokens, &translated[batch_line[i]]).ok()) {
          throw std::runtime_error("target detokenization failed");
        }
      }
    }

    std::string output;
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

void translation_engine_free_string(char* text) {
  std::free(text);
}

void translation_engine_destroy(TranslationEngine* engine) {
  delete engine;
}

const char* translation_engine_version(void) {
  return "0.1.0";
}

}  // extern "C"
