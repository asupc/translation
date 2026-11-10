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

# 按安装的 VS 主版本选择 CMake 生成器：BuildTools 18 自带 v180 工具集，
# 用 "Visual Studio 17 2022" 生成器会因缺少 v143 工具集而报 MSB8020。
[int]$vsMajor = ((& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationVersion | Select-Object -First 1) -split '\.')[0]
$vsGenerator = if ($vsMajor -ge 18) { 'Visual Studio 18 2026' } else { 'Visual Studio 17 2022' }
Write-Host "Using CMake generator: $vsGenerator"

# 版本或生成器变化时，缓存里的构建产物必须失效重编；否则会出现新头文件
# 搭配旧库的不一致（CI 的 restore-keys 也会恢复上一版产物）。
$ct2Tag = 'v4.8.3'
$spmTag = 'v0.2.2'
$dnnlTag = 'v3.13.4'
$stampPath = Join-Path $build 'native-inputs.txt'
$stamp = "CTranslate2=$ct2Tag; sentencepiece=$spmTag; oneDNN=$dnnlTag; generator=$vsGenerator"
$stampMatches = $false
if (Test-Path $stampPath) {
    $existingStamp = Get-Content $stampPath -Raw
    $stampMatches = $existingStamp -and ($existingStamp.Trim() -eq $stamp)
}

function Initialize-BuildDir {
    param(
        [string]$Dir,
        [string]$Generator
    )

    # 生成器切换（17 <-> 18）后旧 CMakeCache 会直接报错，提前按生成器隔离。
    $cache = Join-Path $Dir 'CMakeCache.txt'
    if (Test-Path $cache) {
        $match = Select-String -Path $cache -Pattern '^CMAKE_GENERATOR:INTERNAL=(.*)$' | Select-Object -First 1
        if ($match -and $match.Matches[0].Groups[1].Value -ne $Generator) {
            Write-Host "Generator changed, clearing $Dir"
            Remove-Item $Dir -Recurse -Force
        }
    }
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
        # 缓存/旧目录可能停留在错误版本：校验 tag，不匹配则重新克隆。
        $current = & git -C $Path describe --tags --exact-match 2>$null
        if ($LASTEXITCODE -eq 0 -and $current -eq $Branch) {
            return
        }

        Write-Host "Replacing stale $Path (expected $Branch, found '$current')"
    }
    elseif (Test-Path $Path) {
        # 上次克隆中断的半成品目录：git clone 会因目录非空直接失败。
        Write-Host "Removing incomplete $Path"
    }

    # 先克隆到临时目录，成功后再替换，避免网络失败把仍可用的源码删掉。
    $temp = "$Path.tmp-$([Guid]::NewGuid().ToString('N'))"
    $arguments = @('clone', '--depth', '1', '--branch', $Branch)
    if ($Recurse) {
        $arguments += @('--recurse-submodules', '--shallow-submodules')
    }
    $arguments += @($Url, $temp)
    try {
        Invoke-Checked 'git' $arguments
        if (Test-Path $Path) {
            Remove-Item $Path -Recurse -Force
        }
        Move-Item -Path $temp -Destination $Path
    }
    finally {
        if (Test-Path $temp) {
            Remove-Item $temp -Recurse -Force
        }
    }
}

Ensure-Repo -Path (Join-Path $thirdParty 'CTranslate2') `
    -Url 'https://github.com/OpenNMT/CTranslate2.git' `
    -Branch $ct2Tag -Recurse

Ensure-Repo -Path (Join-Path $thirdParty 'sentencepiece') `
    -Url 'https://github.com/google/sentencepiece.git' `
    -Branch $spmTag

Ensure-Repo -Path (Join-Path $thirdParty 'oneDNN') `
    -Url 'https://github.com/oneapi-src/oneDNN.git' `
    -Branch $dnnlTag -Recurse

$dnnlInstall = Join-Path $build 'dnnl-install-omp'
$dnnlLibrary = Join-Path $dnnlInstall 'lib\dnnl.lib'
$dnnlRuntime = Join-Path $dnnlInstall 'bin\dnnl.dll'
$dnnlHeaders = Join-Path $dnnlInstall 'include'
if ($Force -or -not $stampMatches -or -not (Test-Path $dnnlLibrary) -or -not (Test-Path $dnnlRuntime) -or -not (Test-Path $dnnlHeaders)) {
    Write-Host 'Building oneDNN (OMP)...'
    $dnnlBuild = Join-Path $build 'oneDNN'
    Initialize-BuildDir -Dir $dnnlBuild -Generator $vsGenerator
    Invoke-Checked $cmake @(
        '-S', (Join-Path $thirdParty 'oneDNN'),
        '-B', $dnnlBuild,
        '-G', $vsGenerator,
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
# 保留 include/oneapi/dnnl 目录层级；CT2 的 FindDNNL 按该布局查找 dnnl.hpp。
Copy-Item (Join-Path $dnnlInstall 'include\*') (Join-Path $dnnlLayout 'include') -Recurse -Force
Copy-Item (Join-Path $dnnlInstall 'lib\dnnl.lib') (Join-Path $dnnlLayout 'lib') -Force

$ct2Build = Join-Path $build 'ct2'
$ct2Library = Join-Path $build 'ctranslate2.lib'
$ct2Runtime = Join-Path $build 'ctranslate2.dll'
$dnnlRuntimeCopy = Join-Path $build 'dnnl.dll'
if ($Force -or
    -not $stampMatches -or
    -not (Test-Path $ct2Library) -or
    -not (Test-Path $ct2Runtime) -or
    -not (Test-Path $dnnlRuntimeCopy)) {
    Write-Host 'Building CTranslate2 (DNNL, OpenMP)...'
    Initialize-BuildDir -Dir $ct2Build -Generator $vsGenerator
    Invoke-Checked $cmake @(
        '-S', (Join-Path $thirdParty 'CTranslate2'),
        '-B', $ct2Build,
        '-G', $vsGenerator,
        '-A', 'x64',
        '-DCMAKE_BUILD_TYPE=Release',
        '-DCMAKE_CXX_STANDARD=17',
        # CI 的 CMake 4.x 移除了对 cmake_minimum_required(<3.5) 的兼容，
        # CT2 子模块 cpu_features 声明过老；用官方兼容开关放行
        '-DCMAKE_POLICY_VERSION_MINIMUM=3.5',
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
Initialize-BuildDir -Dir $coreBuild -Generator $vsGenerator
Invoke-Checked $cmake @(
    '-S', (Join-Path $native 'translation_core'),
    '-B', $coreBuild,
    '-G', $vsGenerator,
    '-A', 'x64',
    '-DCMAKE_BUILD_TYPE=Release'
)
Invoke-Checked $cmake @('--build', $coreBuild, '--config', 'Release', '--parallel', "$Jobs")

Set-Content -Path $stampPath -Value $stamp -Encoding ASCII

Write-Host ''
Write-Host "Native build complete: $build"
Get-ChildItem $build -File | Select-Object Name, Length
