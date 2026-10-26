# 使用教程

## 系统要求

- Windows 10/11 x64
- 无需联网：翻译、OCR、查词全部本地运行

## 安装

**方式一：发布包（推荐）**

从 [Releases](https://github.com/asupc/translation/releases) 下载 `Translation-win-x64-*.zip`，解压到任意目录。模型已内置。首次运行前需安装：

- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- [VC++ Redistributable 2015-2022 x64](https://aka.ms/vs/17/release/vc_redist.x64.exe)（提供 OpenMP 运行库 `vcomp140.dll`）

**方式二：从源码构建**

按 [BUILD.md](BUILD.md) 构建应用并准备模型。构建完成后得到可执行文件
`src\Translation.App\bin\Release\net10.0-windows\Translation.App.exe`（或 `build-deploy.ps1`
部署目录中的 `Translation.App.exe`），模型位于其旁的 `models\` 目录。

双击运行即可，无需安装。

## 首次启动

- 应用启动后常驻系统托盘，不显示窗口，不占用任务栏。
- 翻译模型在后台加载（每方向约 150–200ms）；OCR 模型按需加载（首次图片翻译时初始化，约多等 0.5 秒）。

## 基本用法

### 呼出翻译窗口

按全局热键（默认 `Ctrl+Alt+F9`）呼出窗口：自动清空上次内容，等待粘贴或输入。

- 剪切板里是**文本**：`Ctrl+V` 粘贴到原文框，按 `Enter` 翻译。
- 剪切板里是**图片**：`Ctrl+V` 粘贴后自动 OCR 识别，识别文本填入原文框并翻译。

典型流程：选中文字 → `Ctrl+C` → `Ctrl+Alt+F9` → `Ctrl+V` → `Enter`。

### 输入与翻译

- `Ctrl+V`：粘贴；剪切板为图片时自动 OCR 识别并翻译
- `Enter`：翻译
- `Shift+Enter`：换行
- `Esc`：隐藏窗口（应用仍在托盘运行）
- 窗口内按钮：`翻译` 重新翻译原文框内容；`复制译文` 把译文复制到剪切板
- 标题栏右侧显示识别方向（自动 / 中→英 / 英→中）
- 窗口边缘可拖拽调整大小；位置与尺寸会记忆，下次启动恢复

### 单词查词典

输入为**单个英文单词**时自动切换词典模式（ECDICT）：

- 显示多个义项、词性、领域标签
- 显示词形变化（复数、时态、比较级等七类）
- 变形词自动还原：查 `classes` 会显示 `class` 的词条，并提示「classes 是 class 的名词复数」
- 区分大小写：`China`（中国）与 `china`（瓷器）是两个词条；全大写输入按大小写不敏感匹配并取词频更高的候选
- 词典未命中或词典数据缺失时，自动回退为句子翻译

词典数据需先运行 `scripts\build-dictionary.ps1` 构建（见 [BUILD.md](BUILD.md)）；未构建不影响其他功能。

### 图片 OCR 翻译

截图或复制的图片放入剪切板后，呼出窗口按 `Ctrl+V` 粘贴即可：

1. 原文框清空，状态显示「正在识别图片…」
2. PP-OCRv5 中英识别（保留换行）
3. 识别文本填入原文框并自动翻译

单张图 OCR 约 0.3 秒（900×320 实测）；首次图片翻译需先加载 OCR 模型，约 0.8 秒。

## 托盘与设置

右键托盘图标：

- **显示**：显示翻译窗口
- **设置**：打开设置窗口
- **退出**：完全退出应用

设置项：

| 设置 | 说明 |
|---|---|
| 全局热键 | 点击热键框后直接按下组合键（需含 Ctrl/Alt/Shift/Win）；保存后立即生效。注册失败说明被其他程序占用，换一个组合 |
| 开机自动启动 | 写入当前用户的 Run 注册表项，启动后最小化到托盘 |

再次启动应用时不会出现第二个实例，而是唤醒已运行实例的窗口。

## 数据与隐私

- **不联网**：所有功能本地完成，无任何遥测。
- **不记录内容**：日志只记录时机/类型/长度，不记录剪切板正文与 OCR 文本。
- 数据位置：
  - 设置：`%APPDATA%\TranslationApp\settings.json`
  - 日志：`%LOCALAPPDATA%\TranslationApp\logs\app.log`

卸载：退出托盘应用后删除程序目录，再按需删除上述两个数据目录与开机启动项（设置中先关闭）。

## 常见问题

- **按热键没反应**：检查托盘图标是否在运行；热键可能被其他程序占用，到设置中更换。
- **提示模型加载失败**：`models\translation\`、`models\ocr\v5\` 缺失或文件不全，按 [BUILD.md](BUILD.md) 的「模型」一节补齐。
- **翻译很慢**：确认任务管理器中 CPU 占用正常；可选设置环境变量 `OMP_NUM_THREADS=8`（按 CPU 核数调整，逻辑线程全开反而变慢）。
- **查单词走的是句子翻译**：词典数据未构建，运行 `scripts\build-dictionary.ps1`。
- **图片识别乱码**：`models\ocr\v5\` 中的 `ppocrv5_dict.txt` 必须与识别模型配套，重新按文档下载全部四个文件。
