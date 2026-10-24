<#
  层：内核层（core/）—— libpylon.so 的构建脚本

  为什么不用 AGP 的 externalNativeBuild / 直接 cmake+ninja：
  本机（DSH 沙箱）里 cmake 的"编译器 ABI 探测"要用**管道捕获 ninja 的输出**，而沙箱
  禁止子进程用管道交换数据，结果是 cmake 与 ninja 双双 0% CPU 永久阻塞：
      -- Detecting CXX compiler ABI info        ← 卡死在这里
  AGP 的 configureCMakeDebug / buildCMakeDebug 走的是同一条路，所以也会卡住。
  而**直接调 clang++ 编译**只需要文件重定向，实测 0.47s 就能出 .so。

  所以：内核用这个脚本构建，产物放进 app/src/main/jniLibs/arm64-v8a/，
  Gradle 只做打包（不设 externalNativeBuild）。换机器想用 CMake 也可以，
  但那样就得保证 cmake 能正常跑——这里的脚本是**本机受支持的路径**。

  用法：
      pwsh -File core/build.ps1                 # 自动找 NDK
      pwsh -File core/build.ps1 -Ndk D:\SDK\ndk\27.1.12297006 -Api 28
#>
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

# 与旧 CMakeLists 保持一致的编译契约（改了这里就等于改了内核的对外承诺）。
$target = "--target=aarch64-linux-android$Api"
$common = @(
    $target,
    '-std=c++20',
    '-O2',
    '-fPIC',
    '-fvisibility=hidden',      # 只导出 JNIEXPORT 的符号，别和内嵌的 libunity 抢名字
    '-fno-omit-frame-pointer',
    # 不带异常/RTTI：C# 异常在 IL2CPP 里是真正的 C++ 异常，由 libil2cpp 的 libgcc 展开器抛出，
    # 而我们这侧是 NDK 的 LLVM libunwind。我们自己的帧只要带 personality，两边就会打架把进程
    # 带崩（上游实测）。内核调托管方法一律走 il2cpp_runtime_invoke（它在自己的帧里捕获异常），
    # 所以这里从根上不给自己留 personality。
    '-fno-exceptions',
    '-fno-rtti',
    '-Wall', '-Wextra',
    "-I$(Join-Path $PSScriptRoot 'include')",
    "-I$srcRoot",
    "-I$(Join-Path $srcRoot 'imgui')",
    "-I$(Join-Path (Join-Path $srcRoot 'imgui') 'backends')",
    "-I$(Join-Path (Join-Path $srcRoot 'imgui') 'Graphics')",
    "-I$(Join-Path (Join-Path $srcRoot 'imgui') 'helper')"
)

# 源文件：*.cpp 之外还有 *.S（graphics/GuardedCall.S 的受保护调用 trampoline，走集成汇编器）。
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
    # 对象名带原扩展：GuardedCall.cpp 与 GuardedCall.S 不能同名（否则互相覆盖）。
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

$outSo = Join-Path $OutDir 'libpylon.so'
$linkLog = Join-Path $buildDir 'link.log'
# -static-libstdc++：把 libc++ 静态链进来，不依赖游戏那份 libc++_shared.so。
# 内核要用 std::string/vector/mutex，而游戏的 libc++ 版本由 Unity 的 NDK 决定；
# 动态链接等于把内核的生死绑在别人的 C++ 运行时上。
#
# 但静态库里的符号默认是**外部可见**的：不处理的话 libpylon.so 会导出几百个 libc++ 符号，
# 一旦被全局加载就可能去顶替游戏 libc++_shared.so 的同名符号（ABI 不一致 → 随机崩）。
# 所以：
#   --exclude-libs,ALL     把"来自静态库"的符号全部降为隐藏（我们自己的对象文件不受影响，
#                          所以 PYLON_MOD_EXPORT / JNIEXPORT 照旧可见）
#   --gc-sections + -ffunction-sections/-fdata-sections  顺手丢掉没被用到的代码
& $clang @common '-ffunction-sections' '-fdata-sections' '-shared' '-o' $outSo $objects `
    '-static-libstdc++' '-llog' '-ldl' '-lvulkan' '-landroid' `
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
}
