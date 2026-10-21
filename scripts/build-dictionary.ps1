# 构建离线词典数据（ECDICT CSV -> SQLite）。
# 用法:
#   .\scripts\build-dictionary.ps1              # 下载基础版 CSV（约 66MB）并转换
#   .\scripts\build-dictionary.ps1 -CsvPath x   # 使用已下载的 CSV
#   .\scripts\build-dictionary.ps1 -Force       # 重新下载
param(
    [string]$CsvPath,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dictionaryDir = Join-Path $root 'models\dictionary'
$databasePath = Join-Path $dictionaryDir 'ecdict.sqlite'
$defaultCsvPath = Join-Path $dictionaryDir 'ecdict.csv'
$csvUrl = 'https://raw.githubusercontent.com/skywind3000/ecdict/master/ecdict.csv'

New-Item -ItemType Directory -Force $dictionaryDir | Out-Null

if (-not $CsvPath) {
    $CsvPath = $defaultCsvPath
    if ((Test-Path $CsvPath) -and -not $Force) {
        Write-Host "已存在 $CsvPath，跳过下载（用 -Force 重新下载）"
    }
    else {
        Write-Host "下载 $csvUrl （约 66MB）..."
        $temporaryPath = "$CsvPath.download"
        try {
            # CI 上 Invoke-WebRequest 下载大文件易中断，curl 自带重试更可靠
            & curl.exe -L --fail --silent --show-error --retry 3 --retry-delay 5 -o $temporaryPath $csvUrl
            if ($LASTEXITCODE -ne 0) {
                throw "下载失败 (curl exit $LASTEXITCODE)"
            }
            Move-Item -Force $temporaryPath $CsvPath
        }
        finally {
            if (Test-Path $temporaryPath) {
                Remove-Item -Force $temporaryPath
            }
        }
    }
}

if (-not (Test-Path $CsvPath)) {
    throw "CSV 不存在: $CsvPath"
}

if (-not (Get-Command python -ErrorAction SilentlyContinue)) {
    throw '未找到 python，请先安装 Python 3.10+ 并加入 PATH'
}

& python (Join-Path $PSScriptRoot 'build-dictionary.py') --csv $CsvPath --out $databasePath
exit $LASTEXITCODE
