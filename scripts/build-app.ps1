param(
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Translation.App\Translation.App.csproj'

# 原生 DLL 缺失时 dotnet build 仍会成功，应用却会在启动/首次翻译时崩溃；这里提前失败。
$nativeDlls = @('translation_core.dll', 'ctranslate2.dll', 'dnnl.dll')
$missing = @($nativeDlls | Where-Object {
        -not (Test-Path (Join-Path $root "native\build\$_"))
    })
if ($missing.Count -gt 0) {
    throw "缺少原生 DLL: $($missing -join ', ')。先运行 .\scripts\build-native.ps1。"
}

& dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($Run) {
    # 从 MSBuild 取实际可执行文件路径，避免升级 TargetFramework 后硬编码路径失效。
    # TargetPath 是程序集 dll（SDK 项目的 apphost 是单独的 exe），启动它不会成功。
    $target = (& dotnet msbuild $project -nologo -getProperty:RunCommand -p:Configuration=Release | Select-Object -Last 1)
    if ($LASTEXITCODE -ne 0 -or -not $target -or -not (Test-Path $target)) {
        throw "构建成功但找不到输出文件: $target"
    }

    Start-Process -FilePath $target -WorkingDirectory (Split-Path -Parent $target)
}
