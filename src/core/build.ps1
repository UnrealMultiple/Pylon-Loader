
[CmdletBinding()]
param(
    [string]$Ndk = $env:ANDROID_NDK_HOME,
    [string]$OutDir = (Join-Path (Join-Path $PSScriptRoot '..') 'app/src/main/jniLibs/arm64-v8a'),
    [int]$Api = 28,
    [switch]$Rebuild
)

$ErrorActionPreference = 'Stop'

# 交叉编译工具链按宿主分目录；Windows 上可执行文件带 .exe（CI 在 Ubuntu 上跑同一份脚本）。
$hostTag = if ($IsWindows) { 'windows-x86_64' } elseif ($IsMacOS) { 'darwin-x86_64' } else { 'linux-x86_64' }
$exe = if ($IsWindows) { '.exe' } else { '' }

function Find-Ndk {
    param([string]$Hint)
    if ($Hint -and (Test-Path (Join-Path $Hint 'toolchains/llvm/prebuilt'))) { return $Hint }
    $roots = @(
        'D:\SDK\ndk', "$env:LOCALAPPDATA\Android\Sdk\ndk", 'C:\Android\Sdk\ndk',
        "$env:ANDROID_HOME/ndk", "$env:ANDROID_SDK_ROOT/ndk",
        "$HOME/Android/Sdk/ndk", '/usr/local/lib/android/sdk/ndk'
    )
    foreach ($root in $roots) {
        if (-not $root -or -not (Test-Path $root)) { continue }
        # 取版本号最大的一个；排序用 [version] 会因 "27.1.12297006" 之类非标准串失败，用字符串倒序即可。
        $cand = Get-ChildItem $root -Directory -ErrorAction SilentlyContinue |
            Where-Object { Test-Path (Join-Path $_.FullName 'toolchains/llvm/prebuilt') } |
            Sort-Object Name -Descending | Select-Object -First 1
        if ($cand) { return $cand.FullName }
    }
    throw "找不到 NDK：请用 -Ndk 指定，或设置 ANDROID_NDK_HOME"
}

$ndkRoot = Find-Ndk -Hint $Ndk
$toolchainBin = Join-Path $ndkRoot "toolchains/llvm/prebuilt/$hostTag/bin"
$clang = Join-Path $toolchainBin "clang++$exe"
if (-not (Test-Path $clang)) { throw "找不到 clang++：$clang" }

$srcRoot = Join-Path $PSScriptRoot 'src'
$buildDir = Join-Path $PSScriptRoot 'build-arm64'
if ($Rebuild -and (Test-Path $buildDir)) { Remove-Item $buildDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $buildDir, $OutDir | Out-Null

$shSrc = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..\..') 'external\shadowhook\shadowhook\src\main\cpp')).Path
if (-not (Test-Path (Join-Path $shSrc 'shadowhook.c'))) {
    throw "找不到 shadowhook 子模块源码：$shSrc（git submodule update --init --recursive）"
}

# 与旧 CMakeLists 保持一致的编译契约（改了这里就等于改了内核的对外承诺）。
$target = "--target=aarch64-linux-android$Api"
$common = @(
    $target,
    '-std=c++20',
    '-O2',
    '-fPIC',
    '-fvisibility=hidden',      # 只导出 JNIEXPORT 的符号，别和内嵌的 libunity 抢名字
    '-fno-omit-frame-pointer',
    
    '-fno-exceptions',
    '-fno-rtti',
    '-Wall', '-Wextra',
    "-I$(Join-Path $PSScriptRoot 'include')",
    "-I$srcRoot",
    "-I$(Join-Path $srcRoot 'imgui')",
    "-I$(Join-Path (Join-Path $srcRoot 'imgui') 'vendor')",
    "-I$(Join-Path $shSrc 'include')"
)

# 源文件：*.cpp 之外还有 *.S（il2cpp/Guard.S 的受保护调用 trampoline，走集成汇编器）。
$cppSources = Get-ChildItem $srcRoot -Recurse -Filter '*.cpp' | Where-Object { $_.Name -notmatch '\.hold$' }
$asmSources = Get-ChildItem $srcRoot -Recurse -Filter '*.S' | Where-Object { $_.Name -notmatch '\.hold$' }
$sources = @($cppSources + $asmSources) | Sort-Object FullName
if ($sources.Count -eq 0) { throw "没有找到任何源文件：$srcRoot" }

Write-Host "[pylon] NDK      : $ndkRoot"
Write-Host "[pylon] clang++  : $clang"
Write-Host "[pylon] 源文件    : $($sources.Count) 个"

$objects = @()
foreach ($source in $sources) {
    $relative = $source.FullName.Substring($srcRoot.Length + 1) -replace '[\\/]', '_'
    # 对象名带原扩展：Guard.cpp 与 Guard.S 不能同名（否则互相覆盖）。
    $object = Join-Path $buildDir ($relative -replace '\.(cpp|S)$', '_$1.o')
    $objects += $object
    $log = "$object.log"
    # 用 PowerShell 的重定向（写成文件），**不用管道**——管道正是卡住的原因。
    & $clang @common -c $source.FullName -o $object > $log 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host (Get-Content $log -Raw)
        throw "编译失败：$($source.Name)"
    }
    Write-Host "[pylon]   ok  $relative"
}

$shBuild = Join-Path $buildDir 'shadowhook'
New-Item -ItemType Directory -Force -Path $shBuild | Out-Null

$clangC = Join-Path $toolchainBin "clang$exe"
if (-not (Test-Path $clangC)) { throw "找不到 clang：$clangC" }

$shIncludes = @(
    $shSrc,
    (Join-Path $shSrc 'include'),
    (Join-Path $shSrc 'arch\arm64'),
    (Join-Path $shSrc 'common'),
    (Join-Path $shSrc 'third_party\xdl'),
    (Join-Path $shSrc 'third_party\bsd'),
    (Join-Path $shSrc 'third_party\lss')
) | ForEach-Object { "-I$_" }

$shSources = @(
    (Get-ChildItem $shSrc -Filter '*.c' | Where-Object { $_.Name -ne 'sh_jni.c' }) +
    (Get-ChildItem (Join-Path $shSrc 'arch\arm64') -Filter '*.c') +
    (Get-ChildItem (Join-Path $shSrc 'common') -Filter '*.c') +
    (Get-ChildItem (Join-Path $shSrc 'third_party') -Recurse -Filter '*.c')
) | Sort-Object FullName

Write-Host "[shadowhook] 源文件 : $($shSources.Count) 个"

$shCommon = @(
    $target,
    '-std=c17',
    '-Oz',
    '-fPIC',
    '-fvisibility=hidden',
    '-ffunction-sections',
    '-fdata-sections'
) + $shIncludes

$shObjects = @()
foreach ($shFile in $shSources) {
    $shRelative = $shFile.FullName.Substring($shSrc.Length + 1) -replace '[\\/]', '_'
    $shObject = Join-Path $shBuild ($shRelative -replace '\.c$', '_c.o')
    $shLog = "$shObject.log"
    & $clangC @shCommon -c $shFile.FullName -o $shObject > $shLog 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host (Get-Content $shLog -Raw)
        throw "编译失败：$($shFile.Name)"
    }
    $shObjects += $shObject
}

$linkObjects = $objects + $shObjects

$outSo = Join-Path $OutDir 'libpylon.so'
$linkLog = Join-Path $buildDir 'link.log'

& $clang @common '-ffunction-sections' '-fdata-sections' '-shared' '-o' $outSo $linkObjects `
    '-static-libstdc++' '-llog' '-ldl' '-lGLESv3' '-landroid' `
    '-Wl,--exclude-libs,ALL' '-Wl,--gc-sections' '-Wl,-z,max-page-size=16384' > $linkLog 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host (Get-Content $linkLog -Raw)
    throw "链接失败"
}

$so = Get-Item $outSo
Write-Host "[pylon] 产物      : $($so.FullName)（$([int]($so.Length / 1024)) KB）"

# 导出自检：Mod ABI 与 Fx ABI 必须可见，libc++ 符号必须不可见。这两条都曾经悄悄不成立过。
$readelf = Join-Path $toolchainBin "llvm-readelf$exe"
if (Test-Path $readelf) {
    $dyn = & $readelf --dyn-syms --wide $outSo | Select-String 'FUNC.*DEFAULT.*[0-9]+ '
    $exported = $dyn | ForEach-Object { ($_.Line -split '\s+')[-1] } | Where-Object { $_ -match '^_?Z|^PylonMod_|^PylonFx_|^Java_' }
    $modCount = ($exported | Where-Object { $_ -like 'PylonMod_*' }).Count
    $fxCount = ($exported | Where-Object { $_ -like 'PylonFx_*' }).Count
    $cxxCount = ($exported | Where-Object { $_ -notlike 'PylonMod_*' -and $_ -notlike 'PylonFx_*' -and $_ -notlike 'Java_*' }).Count
    Write-Host "[pylon] 导出检查  : PylonMod_* = $modCount，PylonFx_* = $fxCount，JNI = $(($exported | Where-Object { $_ -like 'Java_*' }).Count)，libc++ 残留 = $cxxCount"
    if ($modCount -lt 25) { throw "Mod ABI 导出不足（$modCount 个），检查 PYLON_MOD_EXPORT 与 -fvisibility" }
    if ($fxCount -lt 12) { throw "Fx ABI 导出不足（$fxCount 个），检查 PYLON_FX_EXPORT 与 -fvisibility" }
    if ($cxxCount -gt 0) { throw "仍有 $cxxCount 个 libc++ 符号被导出，检查 -Wl,--exclude-libs,ALL" }

   
    $systemPrefix = '^(vk|gl|egl|ANativeWindow|AAsset|__android_log)'
    $strongUndef = @(& $readelf --dyn-syms --wide $outSo |
        Select-String ' UND ' |
        Where-Object { $_.Line -notmatch '\bWEAK\b' } |
        ForEach-Object { ($_.Line -split '\s+')[-1] } |
        ForEach-Object { $_ } |
        Where-Object { $_ -and $_ -notmatch '@' -and $_ -notmatch $systemPrefix } |
        Sort-Object -Unique)
    if ($strongUndef.Count -gt 0) {
        $strongUndef | ForEach-Object { Write-Host "          $_" }
        throw "有 $($strongUndef.Count) 个强未定义符号，dlopen 会失败；多半是某个 .cpp 没编进来"
    }
    Write-Host "[pylon] 未定义符号  : 无强未定义（dlopen 安全）"
}

# shadowhook 符号必须隐藏（-fvisibility=hidden），内核只导出自己的 ABI。
if (Test-Path $readelf) {
    $shLeak = @(& $readelf --dyn-syms --wide $outSo |
        Select-String 'FUNC.*DEFAULT.*[0-9]+ ' |
        ForEach-Object { ($_.Line -split '\s+')[-1] } |
        Where-Object { $_ -like 'shadowhook_*' })
    if ($shLeak.Count -gt 0) {
        $shLeak | ForEach-Object { Write-Host "          $_" }
        throw "shadowhook 符号被内核导出（$($shLeak.Count) 个）：shadowhook 段少了 -fvisibility=hidden"
    }
    Write-Host "[pylon] shadowhook  : 已内嵌进内核，符号未外泄"
}
