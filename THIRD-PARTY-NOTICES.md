# 第三方组件与模型声明

本程序包含以下第三方组件与数据。分发本程序时请保留本文件。

## 运行时与库

| 组件 | 许可证 | 来源 |
|---|---|---|
| CTranslate2 | MIT | https://github.com/OpenNMT/CTranslate2 |
| oneDNN | Apache-2.0 | https://github.com/oneapi-src/oneDNN |
| SentencePiece | Apache-2.0 | https://github.com/google/sentencepiece |
| RapidOcrNet / RapidOCR | Apache-2.0 | https://github.com/RapidAI/RapidOcrNet |
| SkiaSharp | MIT | https://github.com/mono/SkiaSharp |
| .NET / WPF | MIT | https://github.com/dotnet/runtime |

## 翻译模型

| 模型 | 许可证 | 来源 |
|---|---|---|
| Helsinki-NLP/opus-mt-zh-en | CC-BY-4.0 | https://huggingface.co/Helsinki-NLP/opus-mt-zh-en |
| Helsinki-NLP/opus-mt-en-zh | Apache-2.0 | https://huggingface.co/Helsinki-NLP/opus-mt-en-zh |

`opus-mt-zh-en` 以 CC-BY-4.0 许可分发，再分发时需保留署名。模型经 CTranslate2 int8 量化后随程序分发。

## OCR 模型

| 文件 | 许可证 | 来源 |
|---|---|---|
| PP-OCRv5 中文 mobile（det / cls / rec / dict） | Apache-2.0 | https://www.modelscope.cn/models/RapidAI/RapidOCR |

## 词典数据

| 数据 | 许可证 | 来源 |
|---|---|---|
| ECDICT 基础版 | MIT | https://github.com/skywind3000/ecdict |

ECDICT CSV 由 `scripts/build-dictionary.ps1` 转换为 SQLite 后随程序分发。
