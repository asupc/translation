param(
    [int]$Jobs = 16,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$native = Join-Path $root 'native'
$thirdParty = Join-Path $native 'third_party'
$build = Join-Path $native 'build'

New-Item -ItemType Directory -Force $build | Out-Null

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw 'vswhere.exe not found. Install Visual Studio 2022 Build Tools with C++ tools.'
}

$vsPath = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1)
if (-not $vsPath) {
    throw 'Visual Studio with C++ tools not found.'
}

$cmake = Join-Path $vsPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if (-not (Test-Path $cmake)) {
    $command = Get-Command cmake.exe -ErrorAction SilentlyContinue
    if (-not $command) {
        throw 'cmake.exe not found.'
    }
    $cmake = $command.Source
}

function Invoke-Checked {
    param(
        [string]$FilePath,
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed ($LASTEXITCODE): $FilePath $($Arguments -join ' ')"
    }
}

function Ensure-Repo {
    param(
        [string]$Path,
        [string]$Url,
        [string]$Branch,
        [switch]$Recurse
    )

    if (Test-Path (Join-Path $Path 'CMakeLists.txt')) {
        return
    }

    $arguments = @('clone', '--depth', '1', '--branch', $Branch)
    if ($Recurse) {
        $arguments += @('--recurse-submodules', '--shallow-submodules')
    }
    $arguments += @($Url, $Path)
    Invoke-Checked 'git' $arguments
}

Ensure-Repo -Path (Join-Path $thirdParty 'CTranslate2') `
    -Url 'https://github.com/OpenNMT/CTranslate2.git' `
    -Branch 'v4.8.3' -Recurse

Ensure-Repo -Path (Join-Path $thirdParty 'sentencepiece') `
    -Url 'https://github.com/google/sentencepiece.git' `
    -Branch 'v0.2.2'

Ensure-Repo -Path (Join-Path $thirdParty 'oneDNN') `
    -Url 'https://github.com/oneapi-src/oneDNN.git' `
    -Branch 'v3.13.4' -Recurse

$dnnlInstall = Join-Path $build 'dnnl-install-omp'
$dnnlLibrary = Join-Path $dnnlInstall 'lib\dnnl.lib'
if ($Force -or -not (Test-Path $dnnlLibrary)) {
    Write-Host 'Building oneDNN (OMP)...'
    $dnnlBuild = Join-Path $build 'oneDNN'
    Invoke-Checked $cmake @(
        '-S', (Join-Path $thirdParty 'oneDNN'),
        '-B', $dnnlBuild,
        '-G', 'Visual Studio 17 2022',
        '-A', 'x64',
        '-DCMAKE_BUILD_TYPE=Release',
        # CI 上较新的 MSVC 对 <optional> 等头文件严格检查语言标准，显式固定 C++17
        '-DCMAKE_CXX_STANDARD=17',
        '-DDNNL_BUILD_TESTS=OFF',
        '-DDNNL_BUILD_EXAMPLES=OFF',
        '-DDNNL_LIBRARY_TYPE=SHARED',
        '-DDNNL_CPU_RUNTIME=OMP',
        '-DDNNL_GPU_RUNTIME=NONE',
        "-DCMAKE_INSTALL_PREFIX=$dnnlInstall"
    )
    Invoke-Checked $cmake @('--build', $dnnlBuild, '--config', 'Release', '--parallel', "$Jobs")
    Invoke-Checked $cmake @('--install', $dnnlBuild, '--config', 'Release')
}

$dnnlLayout = Join-Path $build 'dnnl-root\oneapi\dnnl\latest\cpu_gomp'
New-Item -ItemType Directory -Force (Join-Path $dnnlLayout 'include'), (Join-Path $dnnlLayout 'lib') | Out-Null
Copy-Item (Join-Path $dnnlInstall 'include\*') (Join-Path $dnnlLayout 'include') -Recurse -Force
Copy-Item (Join-Path $dnnlInstall 'include\oneapi\dnnl\*') (Join-Path $dnnlLayout 'include') -Force
Copy-Item (Join-Path $dnnlInstall 'lib\dnnl.lib') (Join-Path $dnnlLayout 'lib') -Force

$ct2Build = Join-Path $build 'ct2'
$ct2Library = Join-Path $build 'ctranslate2.lib'
if ($Force -or -not (Test-Path $ct2Library)) {
    Write-Host 'Building CTranslate2 (DNNL, OpenMP)...'
    Invoke-Checked $cmake @(
        '-S', (Join-Path $thirdParty 'CTranslate2'),
        '-B', $ct2Build,
        '-G', 'Visual Studio 17 2022',
        '-A', 'x64',
        '-DCMAKE_BUILD_TYPE=Release',
        '-DCMAKE_CXX_STANDARD=17',
        '-DWITH_MKL=OFF',
        '-DWITH_DNNL=ON',
        '-DWITH_RUY=OFF',
        '-DWITH_OPENBLAS=OFF',
        '-DOPENMP_RUNTIME=COMP',
        "-DINTEL_ROOT=$(Join-Path $build 'dnnl-root')",
        '-DBUILD_CLI=OFF',
        '-DBUILD_TESTS=OFF',
        '-DBUILD_SHARED_LIBS=ON'
    )
    Invoke-Checked $cmake @('--build', $ct2Build, '--config', 'Release', '--parallel', "$Jobs")

    Copy-Item (Join-Path $ct2Build 'Release\ctranslate2.dll') $build -Force
    Copy-Item (Join-Path $ct2Build 'Release\ctranslate2.lib') $build -Force
    Copy-Item (Join-Path $dnnlInstall 'bin\dnnl.dll') $build -Force
    Copy-Item (Join-Path $dnnlInstall 'lib\dnnl.lib') $build -Force
}

Write-Host 'Building translation_core...'
$coreBuild = Join-Path $build 'core'
Invoke-Checked $cmake @(
    '-S', (Join-Path $native 'translation_core'),
    '-B', $coreBuild,
    '-G', 'Visual Studio 17 2022',
    '-A', 'x64',
    '-DCMAKE_BUILD_TYPE=Release'
)
Invoke-Checked $cmake @('--build', $coreBuild, '--config', 'Release', '--parallel', "$Jobs")

Write-Host ''
Write-Host "Native build complete: $build"
Get-ChildItem $build -File | Select-Object Name, Length
