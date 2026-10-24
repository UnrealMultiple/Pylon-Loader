<#
  层：工具 —— 把运行时与托管入口部署进 debug 变体

  为什么要进 APK 而不是放手机目录：
    · 运行时 native（libcoreclr.so 等）必须落在 app 的 nativeLibraryDir，Android 才允许 dlopen
      （这也是上游把 13 个 .so 放进 lib/arm64-v8a 的原因）
    · BCL 与托管程序集是**数据**，可以随 assets 走，首次运行时解到 filesDir
      （上游用的是 assets/dotnet.zip；我们用目录，省一层 zip）

  产物：
    app/src/debug/jniLibs/arm64-v8a/libcoreclr.so 等   ← 运行时 native
    app/src/debug/assets/pylon-dotnet/                 ← BCL（含 PAL 副本，CoreCLR 要求同目录）
    app/src/debug/assets/pylon-managed/Pylon.Managed.dll ← 我们的托管入口

  用法：pwsh -File tools/deploy-runtime.ps1
#>
[CmdletBinding()]
param(
    [string]$RuntimeRoot = (Join-Path $PSScriptRoot '..\runtime\bepinex-android'),
    [string]$RepoRoot = (Join-Path $PSScriptRoot '..')
)

$ErrorActionPreference = 'Stop'

# 只要运行时的 native；libfusion/libmain/libpine/libandroidx* 是上游启动器自己的东西，不要（hook 用 Dobby）。
$runtimeLibs = @(
    'libcoreclr.so', 'libclrjit.so', 'libmscordaccore.so', 'libmscordbi.so',
    'libSystem.Native.so', 'libSystem.Globalization.Native.so',
    'libSystem.IO.Compression.Native.so',
    'libSystem.Security.Cryptography.Native.Android.so',
    # 原生 hook 引擎：我们自己的 provider 通过内核调 Dobby（Apache-2.0，无 libc++ 依赖）。
    # 不再需要上游的 libfusion.so（GPL 系，且它没 DT_NEEDED libc++ 却要用其符号 —— 链接层走不通）。
    'libdobby.so'
)

$srcLib = Join-Path $RuntimeRoot 'lib'
$srcBcl = Join-Path $RuntimeRoot 'dotnet'
$srcInterop = Join-Path $RepoRoot 'interop\generated'
$srcInteropRuntime = Join-Path $RepoRoot 'interop\runtime\net10.0'
$jniDir = Join-Path $RepoRoot 'app\src\debug\jniLibs\arm64-v8a'
$assetDotnet = Join-Path $RepoRoot 'app\src\debug\assets\pylon-dotnet'
$assetManaged = Join-Path $RepoRoot 'app\src\debug\assets\pylon-managed'
$assetInterop = Join-Path $RepoRoot 'app\src\debug\assets\pylon-interop'

foreach ($p in @($srcLib, $srcBcl)) { if (-not (Test-Path $p)) { throw "缺少 $p（先跑 extract-bepinex-android.ps1 + prepare-runtime.ps1）" } }

# ① 运行时 native → jniLibs
New-Item -ItemType Directory -Force -Path $jniDir | Out-Null
foreach ($name in $runtimeLibs) {
    $from = Join-Path $srcLib $name
    if (-not (Test-Path $from)) { throw "运行时缺少 $name" }
    Copy-Item $from (Join-Path $jniDir $name) -Force
}
Write-Host ("[部署] 运行时 native：{0} 个 -> app/src/debug/jniLibs/arm64-v8a" -f $runtimeLibs.Count)

# 清掉曾经在列表里、现在不再需要的件：本脚本只拷贝不删除，留着会被 Gradle 打进 APK。
foreach ($stale in @('libfusion.so', 'libpine.so')) {
    $path = Join-Path $jniDir $stale
    if (Test-Path $path) { Remove-Item $path -Force; Write-Host "[部署] 清理遗留：$stale" }
}

# ② BCL（含 PAL 副本）→ assets/pylon-dotnet
Remove-Item $assetDotnet -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $assetDotnet | Out-Null
Copy-Item (Join-Path $srcBcl '*') $assetDotnet -Recurse -Force
$bclFiles = Get-ChildItem $assetDotnet -Recurse -File
$palsInBcl = ($bclFiles | Where-Object { $_.Name -like 'libSystem*Native*.so' }).Count
Write-Host ("[部署] BCL：{0} 个文件 / {1:N1} MB -> app/src/debug/assets/pylon-dotnet（其中 PAL 副本 {2} 个）" -f `
    $bclFiles.Count, (($bclFiles | Measure-Object Length -Sum).Sum / 1MB), $palsInBcl)

# ③ 托管入口 → 构建 + assets/pylon-managed
$managedProj = Join-Path $RepoRoot 'managed\Pylon.Managed\Pylon.Managed.csproj'
$log = Join-Path $env:TEMP 'pylon-managed-build.log'
dotnet build $managedProj -c Release -v minimal  --nologo -m:1 -p:RestoreUseStaticGraphEvaluation=true > $log 2>&1
if ($LASTEXITCODE -ne 0) { Get-Content $log | Select-Object -Last 15; throw "托管入口编译失败" }

$built = Join-Path $RepoRoot 'managed\Pylon.Managed\bin\Release\net10.0\Pylon.Managed.dll'
if (-not (Test-Path $built)) { throw "找不到构建产物：$built" }
Remove-Item $assetManaged -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $assetManaged | Out-Null
Copy-Item $built $assetManaged -Force
Write-Host ("[部署] 托管入口：Pylon.Managed.dll ({0} KB) -> app/src/debug/assets/pylon-managed" -f [int]((Get-Item $built).Length / 1KB))

# Mod 契约 + 框架 + ImGui：和托管入口放同一目录 → 一起进 TPA → 所有 Mod 共享同一份类型
foreach ($lib in @('Pylon.Mod.Abstractions', 'Pylon.ModLoader', 'Pylon.ImGui')) {
    $dll = Join-Path $RepoRoot "managed\$lib\bin\Release\net10.0\$lib.dll"
    if (Test-Path $dll) {
        Copy-Item $dll $assetManaged -Force
        $label = if ($lib -like '*Abstractions') { 'Mod 契约' } elseif ($lib -like '*ImGui') { 'ImGui 绑定' } else { 'Mod 框架' }
        Write-Host ("[部署] {0}：{1}.dll ({2} KB)" -f $label, $lib, [int]((Get-Item $dll).Length / 1KB))
    } else {
        Write-Warning "[部署] 找不到 $lib 产物（先构建 managed\$lib）"
    }
}

# ③b 代理程序集 + ARM64 版 Il2CppInterop → assets/pylon-interop
#     Mod 编译时引用它们，运行时必须能被解析到（内核把整个目录列进 TPA）。
Remove-Item $assetInterop -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $assetInterop | Out-Null
$interopCount = 0
foreach ($src in @($srcInterop, $srcInteropRuntime)) {
    if (-not (Test-Path $src)) { Write-Warning "[部署] 缺少 $src（先跑 Il2CppInterop 生成 / prepare-runtime.ps1）"; continue }
    Get-ChildItem $src -Filter '*.dll' | ForEach-Object {
        Copy-Item $_.FullName $assetInterop -Force
        $interopCount++
    }
}
$interopFiles = Get-ChildItem $assetInterop -File
Write-Host ("[部署] 代理程序集：{0} 个（含 Assembly-CSharp/Il2CppInterop.Runtime） / {1:N1} MB -> app/src/debug/assets/pylon-interop" -f `
    $interopCount, (($interopFiles | Measure-Object Length -Sum).Sum / 1MB))

# ③c Il2CppInterop.Runtime 的托管依赖（不在 .NET 运行时包里，是 NuGet 组件）
#     实测缺它会炸在 Terraria.Main..cctor()：
#       FileNotFoundException: Microsoft.Extensions.Logging.Abstractions, Version=6.0.0.0
#     上游把这些一起打进了 BepInEx.Android.zip，我们照抄它们的版本（同一构建）。
$srcBepinex = Join-Path $RuntimeRoot 'bepinex'
if (Test-Path $srcBepinex) {
    $deps = Get-ChildItem $srcBepinex -Recurse -Filter 'Microsoft.Extensions.*.dll'
    foreach ($dep in $deps) { Copy-Item $dep.FullName $assetInterop -Force }
    Write-Host ("[部署] Il2CppInterop 托管依赖：{0} 个 Microsoft.Extensions.* -> pylon-interop" -f $deps.Count)

    # ③d hook 栈：Harmony（一个函数多 hook 的正解）+ MonoMod（Android 补丁版）+ IL2CPP 桥
    #     注意：官方 MonoMod 在 Android 上直接抛 NotImplementedException，必须用上游这份补丁版
    #     （provenance/monomod.env：MONOMOD_PACKAGE_VERSION=25.3.7-android.1）。
    $hookStack = @(
        '0Harmony.dll',
        'Mono.Cecil.dll', 'Mono.Cecil.Mdb.dll', 'Mono.Cecil.Pdb.dll', 'Mono.Cecil.Rocks.dll',
        'MonoMod.Backports.dll', 'MonoMod.Core.dll', 'MonoMod.ILHelpers.dll', 'MonoMod.Iced.dll',
        'MonoMod.RuntimeDetour.dll', 'MonoMod.Utils.dll',
        'Disarm.dll', 'Iced.dll',
        # DetourProvider 的具体实现在这里（NativeDetourProvider，背后是 MonoMod）：
        # 上游 Il2CppInteropRuntime 自己不提供 detour 引擎，不给它 Harmony 补丁就会 NRE。
        'BepInEx.Unity.IL2CPP.dll', 'BepInEx.Core.dll', 'BepInEx.Preloader.Core.dll',
        'BepInEx.Unity.Common.dll'
    )
    $copied = 0
    foreach ($name in $hookStack) {
        $from = Join-Path $srcBepinex "core\$name"
        if (Test-Path $from) { Copy-Item $from $assetInterop -Force; $copied++ }
        else { Write-Warning "[部署] hook 栈缺少 $name" }
    }
    Write-Host ("[部署] hook 栈（Harmony/MonoMod/HarmonySupport）：{0}/{1} 个 -> pylon-interop" -f $copied, $hookStack.Count)
}

# ③e Harmony ↔ IL2CPP 桥：**我们自己编译**（值类型参数修复，见 interop/harmonysupport）。
#     绝不能用 BepInEx 原包那份：缺了修复，被 Harmony 挂钩的
#     GUIPanel.Draw(..., Nullable<Color>×3) 一调用就 AccessViolationException 把进程 abort。
$hsProj = Join-Path $RepoRoot 'interop\harmonysupport\Il2CppInterop.HarmonySupport.csproj'
$hsDll = Join-Path $RepoRoot 'interop\harmonysupport\bin\Release\net10.0\Il2CppInterop.HarmonySupport.dll'
if (-not (Test-Path $hsDll)) {
    $hsLog = Join-Path $env:TEMP 'pylon-harmonysupport-build.log'
    dotnet build $hsProj -c Release --nologo -v q -m:1 > $hsLog 2>&1
    if ($LASTEXITCODE -ne 0) { Get-Content $hsLog | Select-Object -Last 10; throw "HarmonySupport 编译失败" }
}
if (-not (Test-Path $hsDll)) { throw "找不到 $hsDll" }
Copy-Item $hsDll (Join-Path $assetInterop 'Il2CppInterop.HarmonySupport.dll') -Force
Write-Host ("[部署] Harmony 桥：Il2CppInterop.HarmonySupport.dll（自编，{0} KB）" -f [int]((Get-Item $hsDll).Length / 1KB))

# ④ 内置示例 Mod 已从构建中移除：APK 不再携带任何随包 Mod。
#     示例源码仍在 samples/，要试就自己编译后把 dll 放进设备 Mods/。
#     这里顺手清理旧版本部署脚本留下的两个 assets 目录（pylon-sample-mod 是更早的
#     HelloMod 遗留物），否则 assets/ 整树仍会把它们打进 APK。
foreach ($legacy in @(
        (Join-Path $RepoRoot 'app\src\debug\assets\pylon-samples'),
        (Join-Path $RepoRoot 'app\src\debug\assets\pylon-sample-mod'))) {
    if (Test-Path $legacy) {
        Remove-Item $legacy -Recurse -Force
        Write-Host "[部署] 已移除内置示例 Mod：$legacy"
    }
}

Write-Host "[部署] 完成。接下来 :app 的 debug 构建会把这些打进 APK（不含任何随包 Mod）。"