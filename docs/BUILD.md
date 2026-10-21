# 构建说明

## 前置条件

- Windows 10/11 x64
- .NET 10 SDK
- Visual Studio 2022 Build Tools：MSVC x64、CMake（安装器自带 CMake 3.31+）
- Git（首次拉取第三方源码）
- Python 3.10+（仅模型转换/基准需要）

已验证环境：.NET 10.0.301、VS 2022 Build Tools 14.44、CMake 3.31.6。

## 第三方版本

| 组件 | 版本 | 来源 |
|---|---|---|
| CTranslate2 | v4.8.3 | https://github.com/OpenNMT/CTranslate2 |
| oneDNN | v3.13.4 | https://github.com/oneapi-src/oneDNN |
| SentencePiece | v0.2.2 | https://github.com/google/sentencepiece |

`native/third_party/` 不入 git；`scripts/build-native.ps1` 会在缺失时按上述版本克隆。

## 一键构建原生部分

```powershell
.\scripts\build-native.ps1 -Jobs 16
```

脚本会：

1. 检查/克隆第三方源码。
2. 用 OMP 运行时构建 oneDNN，生成 CT2 需要的目录布局。
3. 构建 CTranslate2（DNNL、无 MKL、`OPENMP_RUNTIME=COMP`）。
4. 构建 `translation_core.dll`，并把运行时 DLL 放到 `native/build/`。

## 手工构建步骤

以下命令假设从仓库根目录执行，`cmake` 使用 VS Build Tools 自带的版本。

### 1. oneDNN

```powershell
cmake -S native/third_party/oneDNN -B native/build/oneDNN `
  -G "Visual Studio 17 2022" -A x64 -DCMAKE_BUILD_TYPE=Release `
  -DDNNL_BUILD_TESTS=OFF -DDNNL_BUILD_EXAMPLES=OFF `
  -DDNNL_LIBRARY_TYPE=SHARED -DDNNL_CPU_RUNTIME=OMP -DDNNL_GPU_RUNTIME=NONE `
  -DCMAKE_INSTALL_PREFIX=native/build/dnnl-install-omp

cmake --build native/build/oneDNN --config Release --parallel 16
cmake --install native/build/oneDNN --config Release
```

CT2 的 CMake 期望如下布局（脚本会自动创建）：

```
native/build/dnnl-root/oneapi/dnnl/latest/cpu_gomp/
  include/dnnl.h, dnnl.hpp, oneapi/dnnl/...
  lib/dnnl.lib
```

### 2. CTranslate2

```powershell
cmake -S native/third_party/CTranslate2 -B native/build/ct2 `
  -G "Visual Studio 17 2022" -A x64 -DCMAKE_BUILD_TYPE=Release `
  -DWITH_MKL=OFF -DWITH_DNNL=ON -DWITH_RUY=OFF -DWITH_OPENBLAS=OFF `
  -DOPENMP_RUNTIME=COMP `
  -DINTEL_ROOT=<repo>/native/build/dnnl-root `
  -DBUILD_CLI=OFF -DBUILD_TESTS=OFF -DBUILD_SHARED_LIBS=ON

cmake --build native/build/ct2 --config Release --parallel 16
```

把 `ctranslate2.dll`、`ctranslate2.lib` 复制到 `native/build/`。
把 oneDNN 的 `dnnl.dll`、`dnnl.lib` 也复制到 `native/build/`。

### 3. 原生核心

```powershell
cmake -S native/translation_core -B native/build/core `
  -G "Visual Studio 17 2022" -A x64 -DCMAKE_BUILD_TYPE=Release

cmake --build native/build/core --config Release --parallel 16
```

构建后会把 `translation_core.dll` 复制到 `native/build/`。

### 4. WPF 应用

```powershell
dotnet build src/Translation.App/Translation.App.csproj -c Release
```

应用的构建目标会把 `native/build/` 下的三个 DLL 复制到输出目录。

### 5. 一键构建并部署

```powershell
.\scripts\build-deploy.ps1        # 构建、结束运行中的应用、同步到 publish\deploy
.\scripts\build-deploy.ps1 -Destination 'D:\apps\Translation'  # 部署到指定目录
.\scripts\build-deploy.ps1 -Run   # 同步完成后启动
```

脚本使用 `dotnet publish -r win-x64` 生成发布目录，再同步程序文件和 `models/`。`models/` 按仓库内容镜像同步（部署目录中多余的文件会被删除）；模型较大，首次部署会较慢，之后只复制有变化的文件。仓库缺少 `models/translation` 或 `models/ocr/v5` 时跳过模型同步。

## 模型

模型目录：

```
models/translation/opus-mt-zh-en-int8/
models/translation/opus-mt-en-zh-int8/
models/ocr/v5/
```

### 翻译模型转换

```powershell
python -m pip install ctranslate2 transformers sentencepiece torch
ct2-transformers-converter --model Helsinki-NLP/opus-mt-zh-en `
  --output_dir models/translation/opus-mt-zh-en-int8 `
  --quantization int8 --copy_files source.spm target.spm vocab.json --force

ct2-transformers-converter --model Helsinki-NLP/opus-mt-en-zh `
  --output_dir models/translation/opus-mt-en-zh-int8 `
  --quantization int8 --copy_files source.spm target.spm vocab.json --force
```

### OCR 模型下载（PP-OCRv5 中文 mobile）

```text
https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/onnx/PP-OCRv5/det/ch_PP-OCRv5_det_mobile.onnx
https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/onnx/PP-OCRv5/cls/ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx
https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/onnx/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile.onnx
https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.10.0/paddle/PP-OCRv5/rec/ch_PP-OCRv5_rec_mobile/ppocrv5_dict.txt
```

放入 `models/ocr/v5/`，文件名保持上述名称。

## 词典数据

```powershell
.\scripts\build-dictionary.ps1
```

下载 ECDICT 基础版 CSV（约 66MB，https://github.com/skywind3000/ecdict ，MIT 许可）并转换为 `models/dictionary/ecdict.sqlite`。`-CsvPath` 可指定已手动下载的 CSV，`-Force` 重新下载。需要 Python 3.10+。不构建词典数据时，查单个单词自动回退句子翻译。

## 运行

```powershell
.\src\Translation.App\bin\Release\net10.0-windows\Translation.App.exe
```

开发时也可以设置（指向仓库中的 `models` 目录的绝对路径）：

```powershell
$env:TRANSLATION_MODELS_DIR = "<仓库根目录>\models"
```

## 常见问题

- **热键注册失败**：被其他程序占用；在托盘“设置”中更换，默认 `Ctrl+Alt+F9`。
- **模型加载失败**：检查 `models/translation/.../model.bin`、`source.spm`、`target.spm` 是否存在。
- **OCR 失败**：检查 `models/ocr/v5/` 四个文件是否齐全，字典与识别模型必须匹配。
- **DLL 找不到**：确认 `translation_core.dll`、`ctranslate2.dll`、`dnnl.dll` 都在应用输出目录；`vcomp140.dll` 由 VC++ 运行库提供。
- **性能明显偏慢**：确认 CT2 使用 DNNL 且 `OPENMP_RUNTIME=COMP`；运行时 `OMP_NUM_THREADS` 建议 8（按 CPU 调整）。
