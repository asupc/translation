# 构建随包分发的离线英文发音资源（sherpa-onnx CLI + KittenTTS 模型 + 英语词典）。
# 用法:
#   .\scripts\build-tts.ps1          # 缺失或版本变化时下载并解压
#   .\scripts\build-tts.ps1 -Force   # 强制重新下载
param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$ttsDir = Join-Path $root 'models\tts'
$binDir = Join-Path $ttsDir 'bin'
$modelDir = Join-Path $ttsDir 'model'

# 版本固定；修改后 version.txt 与 CI 缓存键都会失效。
$sherpaVersion = 'v2.0.1'
$runtimeName = "sherpa-onnx-$sherpaVersion-win-x64-shared-MD-Release"
$modelName = 'kitten-micro-en-v0_8'
$lexiconName = 'lexicon-en-us.txt'
$runtimeUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/$sherpaVersion/$runtimeName.tar.bz2"
$modelUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/$modelName.tar.bz2"
$lexiconUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/$lexiconName"
$stamp = "$sherpaVersion;$modelName;$lexiconName;layout=1"

$requiredFiles = @(
    (Join-Path $binDir 'sherpa-onnx-offline-tts.exe'),
    (Join-Path $binDir 'sherpa-onnx-c-api.dll'),
    (Join-Path $binDir 'sherpa-onnx-cxx-api.dll'),
    (Join-Path $binDir 'onnxruntime.dll'),
    (Join-Path $modelDir 'model.onnx'),
    (Join-Path $modelDir 'voices.bin'),
    (Join-Path $modelDir 'tokens.txt'),
    (Join-Path $ttsDir $lexiconName)
)

$stampPath = Join-Path $ttsDir 'version.txt'
$existingStamp = if (Test-Path $stampPath) { Get-Content $stampPath -Raw } else { '' }
$missing = @($requiredFiles | Where-Object { -not (Test-Path $_) })
$upToDate = $existingStamp -and ($existingStamp.Trim() -eq $stamp) -and $missing.Count -eq 0

if ($upToDate -and -not $Force) {
    Write-Host '发音资源已是最新，跳过下载（用 -Force 重新构建）'
    exit 0
}

# 必须用 System32 下的 bsdtar/curl：PATH 中 Git 自带的 GNU tar 会把 "C:\..." 当成远程主机。
$system32 = Join-Path $env:SystemRoot 'System32'
$curl = Join-Path $system32 'curl.exe'
$tar = Join-Path $system32 'tar.exe'
foreach ($tool in @($curl, $tar)) {
    if (-not (Test-Path $tool)) {
        throw "未找到 $tool；需要 Windows 10/11 自带的 curl 与 tar"
    }
}

$temp = Join-Path $env:TEMP ("translation-tts-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $temp | Out-Null

function Invoke-Download {
    param([string]$Url, [string]$Target)

    Write-Host "下载 $Url"
    $temporary = "$Target.download"
    try {
        & $curl -L --fail --silent --show-error --retry 3 --retry-delay 5 --connect-timeout 20 --max-time 900 -o $temporary $Url
        if ($LASTEXITCODE -ne 0) {
            throw "下载失败 (curl exit $LASTEXITCODE): $Url"
        }

        Move-Item -Force $temporary $Target
    }
    finally {
        if (Test-Path $temporary) {
            Remove-Item -Force $temporary
        }
    }
}

try {
    $runtimeTar = Join-Path $temp 'runtime.tar.bz2'
    $modelTar = Join-Path $temp 'model.tar.bz2'
    $lexiconPath = Join-Path $temp $lexiconName
    Invoke-Download $runtimeUrl $runtimeTar
    Invoke-Download $modelUrl $modelTar
    Invoke-Download $lexiconUrl $lexiconPath

    $runtimeExtract = Join-Path $temp 'runtime'
    $modelExtract = Join-Path $temp 'model'
    New-Item -ItemType Directory -Force $runtimeExtract, $modelExtract | Out-Null
    & $tar -xjf $runtimeTar -C $runtimeExtract
    if ($LASTEXITCODE -ne 0) {
        throw "解压运行时失败 (tar exit $LASTEXITCODE)"
    }

    & $tar -xjf $modelTar -C $modelExtract
    if ($LASTEXITCODE -ne 0) {
        throw "解压模型失败 (tar exit $LASTEXITCODE)"
    }

    $runtimeRoot = Join-Path $runtimeExtract $runtimeName
    $modelRoot = Join-Path $modelExtract $modelName

    # 先组装到临时目录，成功后再整体替换，避免半成品覆盖可用资源。
    $staging = Join-Path $temp 'staging'
    $stagingBin = Join-Path $staging 'bin'
    $stagingModel = Join-Path $staging 'model'
    New-Item -ItemType Directory -Force $stagingBin, $stagingModel | Out-Null

    Copy-Item (Join-Path $runtimeRoot 'bin\sherpa-onnx-offline-tts.exe') $stagingBin
    Copy-Item (Join-Path $runtimeRoot 'bin\*.dll') $stagingBin
    Copy-Item (Join-Path $runtimeRoot 'lib\*.dll') $stagingBin
    Copy-Item (Join-Path $modelRoot 'model.onnx') (Join-Path $stagingModel 'model.onnx')
    Copy-Item (Join-Path $modelRoot 'voices.bin') $stagingModel
    Copy-Item (Join-Path $modelRoot 'tokens.txt') $stagingModel
    if (Test-Path (Join-Path $modelRoot 'LICENSE')) {
        Copy-Item (Join-Path $modelRoot 'LICENSE') $stagingModel
    }
    if (Test-Path (Join-Path $modelRoot 'README.md')) {
        Copy-Item (Join-Path $modelRoot 'README.md') $stagingModel
    }
    Copy-Item $lexiconPath $staging

    if (Test-Path $ttsDir) {
        Remove-Item $ttsDir -Recurse -Force
    }
    Move-Item $staging $ttsDir
    Set-Content -Path (Join-Path $ttsDir 'version.txt') -Value $stamp -Encoding ascii

    $missing = @($requiredFiles | Where-Object { -not (Test-Path $_) })
    if ($missing.Count -gt 0) {
        throw "发音资源不完整: $($missing -join ', ')"
    }

    Get-ChildItem $ttsDir -Recurse -File | Measure-Object Length -Sum |
        ForEach-Object { Write-Host ("发音资源就绪: {0} 个文件，约 {1:N1} MB" -f $_.Count, ($_.Sum / 1MB)) }
}
finally {
    if (Test-Path $temp) {
        Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}
