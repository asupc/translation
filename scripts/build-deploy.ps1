param(
    # 部署目标目录；默认为仓库下 publish\deploy
    [string]$Destination,
    [switch]$SkipModels,
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Translation.App\Translation.App.csproj'
$staging = Join-Path $root 'publish\win-x64'
if (-not $Destination) {
    $Destination = Join-Path $root 'publish\deploy'
}
$processName = 'Translation.App'

function Invoke-Robocopy {
    param(
        [string]$Source,
        [string]$Target,
        [string[]]$Options
    )

    & robocopy $Source $Target @Options /R:3 /W:1 /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy 失败 ($LASTEXITCODE): $Source -> $Target"
    }
}

if (Test-Path $staging) {
    Remove-Item $staging -Recurse -Force
}

& dotnet publish $project -c Release -r win-x64 --self-contained false -o $staging
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

New-Item -ItemType Directory -Force $Destination | Out-Null

$running = Get-Process -Name $processName -ErrorAction SilentlyContinue
if ($running) {
    $running | Stop-Process -Force
    # 进程被终止后可能短暂仍出现在 Get-Process 中；轮询等待其真正消失，
    # 否则会在文件尚未释放时误报“无法结束”。
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Process -Name $processName -ErrorAction SilentlyContinue) -and
           (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
    $stillRunning = Get-Process -Name $processName -ErrorAction SilentlyContinue
    if ($stillRunning) {
        throw "无法结束正在运行的 $processName，文件可能仍被占用。"
    }
    Start-Sleep -Milliseconds 300
}

Invoke-Robocopy -Source $staging -Target $Destination -Options @('/E', '/PURGE', '/XD', 'models')

$models = Join-Path $root 'models'
if (-not $SkipModels) {
    $hasTranslation = Test-Path (Join-Path $models 'translation')
    $hasOcr = Test-Path (Join-Path $models 'ocr\v5')
    if (-not $hasTranslation -and -not $hasOcr) {
        throw 'models\translation 与 models\ocr\v5 都不存在；如只需部署程序，请加 -SkipModels。'
    }

    $destinationModels = Join-Path $Destination 'models'
    New-Item -ItemType Directory -Force $destinationModels | Out-Null
    foreach ($name in @('translation', 'ocr', 'dictionary', 'tts')) {
        $source = Join-Path $models $name
        if (-not (Test-Path $source)) {
            Write-Warning "models\$name 不存在，跳过该目录（保留目标目录中的现有内容）。"
            continue
        }

        Invoke-Robocopy -Source $source -Target (Join-Path $destinationModels $name) -Options @('/MIR')
    }
}

if ($Run) {
    Start-Process -FilePath (Join-Path $Destination 'Translation.App.exe') -WorkingDirectory $Destination
}

exit 0
