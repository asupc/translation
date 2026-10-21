param(
    # 部署目标目录；默认为仓库下 publish\deploy
    [string]$Destination,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Translation.App\Translation.App.csproj'
$staging = Join-Path $root 'publish\win-x64'
if (-not $Destination) {
    $destination = Join-Path $root 'publish\deploy'
}
else {
    $destination = $Destination
}
$processName = 'Translation.App'

if (Test-Path $staging) {
    Remove-Item $staging -Recurse -Force
}

& dotnet publish $project -c Release -r win-x64 --self-contained false -o $staging
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

New-Item -ItemType Directory -Force $destination | Out-Null

$running = Get-Process -Name $processName -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

& robocopy $staging $destination /E /PURGE /XD models /R:3 /W:1 /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -ge 8) {
    exit $LASTEXITCODE
}

$models = Join-Path $root 'models'
if ((Test-Path (Join-Path $models 'translation')) -and (Test-Path (Join-Path $models 'ocr\v5'))) {
    & robocopy $models (Join-Path $destination 'models') /MIR /R:3 /W:1 /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) {
        exit $LASTEXITCODE
    }
}
else {
    Write-Warning 'models\translation or models\ocr\v5 not found; models were not synced.'
}

if ($Run) {
    Start-Process -FilePath (Join-Path $destination 'Translation.App.exe') -WorkingDirectory $destination
}

exit 0
