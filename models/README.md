# 模型说明

模型目录较大，默认不纳入 git；发布时随安装包分发或首次运行时下载。

## 翻译模型

| 模型 | 方向 | 许可证 | 来源 |
|---|---|---|---|
| opus-mt-zh-en-int8 | 中 → 英 | CC-BY-4.0 | https://huggingface.co/Helsinki-NLP/opus-mt-zh-en |
| opus-mt-en-zh-int8 | 英 → 中 | Apache-2.0 | https://huggingface.co/Helsinki-NLP/opus-mt-en-zh |

- CTranslate2 int8 量化，每个方向约 75.9MB。
- 目录必须包含：`model.bin`、`config.json`、`shared_vocabulary.json`、`source.spm`、`target.spm`、`vocab.json`。
- 转换命令见 `docs/BUILD.md`。
- CC-BY-4.0 模型需要保留署名；发布时在“关于”或第三方声明中加入模型来源。

## OCR 模型

PP-OCRv5 中文 mobile（RapidOcrNet 使用）：

| 文件 | 用途 | 大小 |
|---|---|---|
| ch_PP-OCRv5_det_mobile.onnx | 文本检测 | 4.6MB |
| ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx | 方向分类 | 1.0MB |
| ch_PP-OCRv5_rec_mobile.onnx | 中英识别 | 15.9MB |
| ppocrv5_dict.txt | 识别字典 | 70KB |

- 许可：Apache-2.0（RapidOCR / PaddleOCR 模型）。
- 下载地址见 `docs/BUILD.md`。
- 字典必须与识别模型匹配，否则输出会乱码。

## 词典数据

| 文件 | 用途 | 来源 |
|---|---|---|
| dictionary/ecdict.sqlite | 英文单词查词典（多义项 / 词性 / 词形变化） | ECDICT（MIT），由 `scripts/build-dictionary.ps1` 从基础版 CSV 转换 |

- 基础版 CSV 约 76 万词条，入库时只保留有中文释义的行，并生成变形词 → 原词索引。
- 不入 git；库文件缺失时查词自动回退句子翻译。

## 第三方运行时

| 组件 | 许可证 |
|---|---|
| CTranslate2 | MIT |
| oneDNN | Apache-2.0 |
| SentencePiece | Apache-2.0 |
| RapidOcrNet / RapidOCR | Apache-2.0 |

不要使用 NLLB-200（CC-BY-NC-4.0）做商业分发；当前项目默认不包含该模型。
