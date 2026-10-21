param(
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Translation.App\Translation.App.csproj'

& dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

if ($Run) {
    $output = Join-Path $root 'src\Translation.App\bin\Release\net10.0-windows'
    Start-Process -FilePath (Join-Path $output 'Translation.App.exe') -WorkingDirectory $output
}
