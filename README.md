# Translation — Windows 离线翻译工具

常驻托盘的 Windows 翻译工具：全局热键呼出窗口，粘贴文字直接翻译，粘贴图片自动 OCR 后翻译，单个英文单词走离线词典。**默认全程离线**——不联网、无遥测、不记录剪切板内容。

## 功能特性

- **全局热键呼出**：默认 `Ctrl+Alt+F9`（可配置），一键呼出翻译窗口
- **中英自动识别**：自动判断方向，中↔英双向翻译
- **图片 OCR 翻译**：粘贴图片自动 PP-OCRv5 中英识别后翻译，保留换行
- **离线词典**：单个英文单词走 ECDICT 词典——多义项、词性、词形变化；变形词自动还原（`classes` → `class`），未命中自动回退句子翻译
- **常驻托盘**：单实例、可选开机启动、窗口位置与尺寸记忆
- **完全离线**：翻译核心为本地 C++（CTranslate2 + oneDNN + SentencePiece），无任何网络请求

## 实测性能

Ryzen 9 7945HX（16C/32T）实测：

| 场景 | 结果 |
|---|---|
| 原生翻译 zh→en | P50 38.6ms，P95 48ms |
| 原生翻译 en→zh | P50 43.8ms，P95 51ms |
| 热键 → 窗口可见 | 43–52ms |
| 图片 OCR（900×320，预热后） | 260ms |
| 翻译模型体积 | 75.9MB/方向 |
| OCR 模型体积 | 约 21.5MB |

## 快速上手

**方式一：下载发布包（推荐）**

从 [Releases](https://github.com/asupc/translation/releases) 下载 `Translation-win-x64-*.zip`，解压后运行 `Translation.App.exe`。翻译 / OCR / 词典模型已内置，无需另外下载；需安装 .NET 10 Desktop Runtime 与 VC++ Redistributable（详见 Release 说明）。

**方式二：从源码构建**

按 [BUILD.md](docs/BUILD.md) 构建应用并准备模型（翻译模型需转换，OCR 模型需下载，词典数据可选）。

**使用**

1. 运行 `Translation.App.exe`，应用常驻托盘。
2. `Ctrl+Alt+F9` 呼出窗口，`Ctrl+V` 粘贴：文本按 `Enter` 翻译，图片自动 OCR 后翻译。

完整功能说明与使用教程见 **[docs/USAGE.md](docs/USAGE.md)**。

## 项目结构

```
src/Translation.App/        WPF 应用
native/translation_core/    C ABI 原生翻译核心（C++）
models/                     模型存放说明（模型文件不入 git）
scripts/                    构建/部署/词典数据脚本
docs/                       架构、构建、使用文档
tests/                      单元测试（dotnet test）
```

## 文档

- [使用教程](docs/USAGE.md) — 安装、热键、OCR、词典、设置、FAQ
- [构建说明](docs/BUILD.md) — 环境要求、一键构建、模型转换与下载
- [架构说明](docs/ARCHITECTURE.md) — 模块划分、热键/剪切板流程、线程模型、性能关键点

## 已知限制

- 在线翻译引擎尚未接入，当前仅离线引擎。
- 首次运行窗口居中主屏；跟随鼠标所在显示器定位在后续版本。
- 以 zip 包分发，无安装器与自动更新。
- 热键与 OCR 使用固定默认模型；模型管理/多语言在后续版本。

## 许可证

代码以 [MIT](LICENSE) 许可发布。依赖与模型的许可证：

| 组件 | 许可证 |
|---|---|
| CTranslate2 | MIT |
| oneDNN | Apache-2.0 |
| SentencePiece | Apache-2.0 |
| RapidOcrNet / RapidOCR / PP-OCRv5 模型 | Apache-2.0 |
| OPUS-MT zh-en 模型 | CC-BY-4.0 |
| OPUS-MT en-zh 模型 | Apache-2.0 |
| ECDICT 词典数据 | MIT |

其中 CC-BY-4.0 模型要求保留署名；再分发时请在附带材料中注明模型来源（详见 [models/README.md](models/README.md)）。
