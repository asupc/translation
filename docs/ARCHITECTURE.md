# 架构说明

## 总览

```
┌──────────────────── Translation.App (WPF, C#) ────────────────────┐
│ Tray / GlobalHotkey / Clipboard / Settings / SingleInstance       │
│ MainWindow / SettingsWindow                                       │
│ OcrService (RapidOcrNet + PP-OCRv5)                               │
│ DictionaryService (ECDICT SQLite)                                 │
│ TranslationCore (P/Invoke)                                        │
└───────────────────────────────┬───────────────────────────────────┘
                                │ C ABI
┌───────────────────────────────▼───────────────────────────────────┐
│ translation_core.dll (C++)                                        │
│ SentencePiece tokenize/detokenize                                 │
│ CTranslate2 Translator + OPUS-MT int8 + oneDNN                    │
└───────────────────────────────────────────────────────────────────┘
```

## 热键与剪切板流程

1. `RegisterHotKey` 注册单一全局热键（默认 `Ctrl+Alt+F9`）。
2. 收到 `WM_HOTKEY` 后显示窗口并清空上次内容，等待粘贴或输入；**不读取剪切板**。
3. 用户在原文框按 `Ctrl+V` 时在 UI 线程预检剪切板：
   - 有文本：放行按键，走 TextBox 默认粘贴，`Enter` 触发翻译。
   - 无文本而有图片：拦截按键，取 `BitmapSource`（`Freeze` 后可用于后台线程），后台 OCR，识别文本填入原文后再走翻译。

没有独立的截图/划词功能，OCR 只针对粘贴的剪切板图片。

## 原生核心

`native/translation_core/translation_core.h` 暴露 C ABI：

```c
TranslationEngine* translation_engine_create(
    const char* model_dir, int num_threads,
    char* error_message, int error_message_size);

char* translation_engine_translate(
    TranslationEngine* engine, const char* text,
    int beam_size, int max_decoding_length,
    char* error_message, int error_message_size);

void translation_engine_free_string(char* text);
void translation_engine_destroy(TranslationEngine* engine);
```

- 每个方向一个 `TranslationEngine`（zh→en、en→zh），启动时加载。
- 源文本用 `source.spm` 编码，**必须追加 `</s>`**。
- `ctranslate2::Translator` 的 `int8` 计算；`beam_size=2`，`max_decoding_length=256`。
- 目标 token 用 `target.spm` 解码，过滤 `</s>`/`<pad>`。
- 引擎内部用 `std::mutex` 串行化请求；WPF 层用 `Task.Run` 避免阻塞 UI。
- 返回字符串用 `malloc` 分配，调用方必须用 `translation_engine_free_string` 释放。

## OCR

- RapidOcrNet 4.2.0 + PP-OCRv5 中文 mobile 模型（det/cls/rec + dict，约 21.5MB）。
- 懒加载：首次图片翻译才初始化（`EnsureOcrAsync`），空闲期不占 OCR 内存（约 150–200MB：session + 推理 arena）。
- `BitmapSource` → PNG → `SKBitmap` → `RapidOcr.Detect`。
- 默认关闭角度分类（`DoAngle=false`），识别并行度 1。
- 中英混排模型同时支持英文；识别文本保留换行，交给翻译层。

## 词典

- 触发：输入为单个英文单词（无空格、纯字母，可含内部 `'` `.` `-`；首尾引号与尾部句读会被剥掉）时优先走离线词典，其余输入走句子翻译；词典未命中或库文件缺失时静默回退句子翻译。
- 数据：ECDICT（MIT）基础版 CSV 由 `scripts/build-dictionary.ps1` 转成 `models/dictionary/ecdict.sqlite`，只保留有中文释义的词条；两张表：`entries`（词条）与 `inflections`（变形词 → 原词索引）。
- `WordPattern` 判定输入，`EntryParser` 解析多行释义（词性前缀 / 领域标签）与词形变化字段，均为纯函数，有单元测试。
- `DictionaryService` 只读、懒打开：首次查询才建立 SQLite 连接，锁串行化；查词不依赖 NMT 模型加载。
- 变形词（classes / running / went）经 `inflections` 还原到原词条目展示，并附「classes 是 class 的名词复数」提示。
- 词条区分大小写（China=中国 / china=瓷器）；全大写输入按大小写不敏感匹配并取词频更高的候选。

## 线程与生命周期

- UI 线程：窗口、托盘、热键消息、剪切板。
- 后台线程：原生翻译（P/Invoke 阻塞）、OCR 推理、模型加载。
- 翻译/OCR 请求在同一时间通常只有一个；核心内部串行化，避免 CPU 争抢。
- 启动顺序：
  1. 单实例 Mutex / 唤醒事件
  2. 设置加载
  3. 创建窗口（`--tray` 时不显示）
  4. 注册热键、托盘
  5. 后台加载翻译模型（OCR 懒加载，首次图片翻译才初始化）
- 单实例：命名 Mutex + 命名 EventWaitHandle；第二个实例设置事件后退出，第一个实例显示窗口。
- 空闲释放（`IdleReleaseMinutes`，默认 3 分钟，0 关闭）：
  - 窗口隐藏（Esc/关闭）后开始计时，呼出即取消并预热翻译模型。
  - 到点后销毁翻译模型、OCR 模型与词典连接（native 卸载放后台线程，不卡 UI）；下次翻译/OCR/查词自动重新加载。
  - 竞态防护：检查点全在 UI 线程；翻译/OCR 进行中用计数（`BeginOperation`/`EndOperation`）推迟释放，销毁前先等未完成的模型初始化任务收尾。

## 性能关键点

- CTranslate2 用 oneDNN 后端，`DNNL_CPU_RUNTIME=OMP`；CT2 自身用 `OPENMP_RUNTIME=COMP`。
- 运行时设置 `OMP_NUM_THREADS`（默认 8）；16 逻辑线程全开会明显变慢。
- `compute_type=int8`；模型 int8 量化后约 75.9MB/方向。
- 翻译模型启动时加载；OCR 模型懒加载（首次图片翻译时初始化）。
- 窗口常驻托盘，热键路径不承担进程冷启动；窗口隐藏超过 `IdleReleaseMinutes` 后释放模型与词典（实测约降 180MB），呼出即预热重载。
- 翻译窗口使用不透明窗口 + `WindowChrome`，支持边缘缩放；Windows 11 用 DWM 圆角。

## 路径与配置

- 模型根目录解析顺序：
  1. 环境变量 `TRANSLATION_MODELS_DIR`
  2. 应用目录下的 `models/`
  3. 从应用目录向上查找包含 `models/translation` 的目录（开发时用）
- 词典：`models/dictionary/ecdict.sqlite`（缺失时查词自动回退句子翻译）
- 设置：`%APPDATA%\TranslationApp\settings.json`
  - `Hotkey`、`AutoStart`、`WindowWidth`、`WindowHeight`、`WindowLeft`、`WindowTop`、`IdleReleaseMinutes`
- 日志：`%LOCALAPPDATA%\TranslationApp\logs\app.log`；只记录时机/类型/长度，不记录剪切板正文。
- 开机启动：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`，值为 `"<exe>" --tray`。

## 隐私

- 默认不联网；离线翻译。
- 不记录剪切板内容和 OCR 文本。
- 后续百度在线引擎需用户显式开启，密钥用 DPAPI/凭据管理器保存。
