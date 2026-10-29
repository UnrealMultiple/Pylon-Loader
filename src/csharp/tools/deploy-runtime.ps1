<#
  层：工具 —— 把运行时与托管入口部署进 debug 变体

  为什么要进 APK 而不是放手机目录：
    · 运行时 native（libcoreclr.so 等）必须落在 app 的 nativeLibraryDir，Android 才允许 dlopen
      （这也是上游把 13 个 .so 放进 lib/arm64-v8a 的原因）
    · BCL 与托管程序集是**数据**，可以随 assets 走，首次运行时解到 filesDir
      （上游用的是 assets/dotnet.zip；我们用目录，省一层 zip）

  产物：
    src/app/src/debug/jniLibs/arm64-v8a/libcoreclr.so 等   ← 运行时 native
    src/app/src/debug/assets/pylon-dotnet/               ← BCL（含 PAL 副本，CoreCLR 要求同目录）
    src/app/src/debug/assets/pylon-managed/Pylon.Managed.dll ← 我们的托管入口

  用法：pwsh -File tools/deploy-runtime.ps1
#>
[CmdletBinding()]
param(
    [string]$RuntimeRoot = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path 'runtime\bepinex-android'),
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
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
$jniDir = Join-Path $RepoRoot 'src\app\src\debug\jniLibs\arm64-v8a'
$assetDotnet = Join-Path $RepoRoot 'src\app\src\debug\assets\pylon-dotnet'
$assetManaged = Join-Path $RepoRoot 'src\app\src\debug\assets\pylon-managed'
$assetInterop = Join-Path $RepoRoot 'src\app\src\debug\assets\pylon-interop'

foreach ($p in @($srcLib, $srcBcl)) { if (-not (Test-Path $p)) { throw "缺少 $p（先跑 extract-bepinex-android.ps1 + prepare-runtime.ps1）" } }

# ① 运行时 native → jniLibs
New-Item -ItemType Directory -Force -Path $jniDir | Out-Null
foreach ($name in $runtimeLibs) {
    $from = Join-Path $srcLib $name
    if (-not (Test-Path $from)) { throw "运行时缺少 $name" }
    Copy-Item $from (Join-Path $jniDir $name) -Force
}
Write-Host ("[部署] 运行时 native：{0} 个 -> src/app/src/debug/jniLibs/arm64-v8a" -f $runtimeLibs.Count)

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
Write-Host ("[部署] BCL：{0} 个文件 / {1:N1} MB -> src/app/src/debug/assets/pylon-dotnet（其中 PAL 副本 {2} 个）" -f `
    $bclFiles.Count, (($bclFiles | Measure-Object Length -Sum).Sum / 1MB), $palsInBcl)

# ③ 托管入口 → 构建 + assets/pylon-managed
$managedProj = Join-Path $RepoRoot 'src\csharp\managed\Pylon.Managed\Pylon.Managed.csproj'
$log = Join-Path $env:TEMP 'pylon-managed-build.log'
dotnet build $managedProj -c Release -v minimal --nologo -m:1 -p:MSBUILDNOINPROCNODE=1 -p:RestoreUseStaticGraphEvaluation=true > $log 2>&1
if ($LASTEXITCODE -ne 0) { Get-Content $log | Select-Object -Last 15; throw "托管入口编译失败" }

$built = Join-Path $RepoRoot 'src\csharp\managed\Pylon.Managed\bin\Release\net10.0\Pylon.Managed.dll'
if (-not (Test-Path $built)) { throw "找不到构建产物：$built" }
Remove-Item $assetManaged -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $assetManaged | Out-Null
Copy-Item $built $assetManaged -Force
Write-Host ("[部署] 托管入口：Pylon.Managed.dll ({0} KB) -> src/app/src/debug/assets/pylon-managed" -f [int]((Get-Item $built).Length / 1KB))

# Mod 契约 + 框架 + ImGui：和托管入口放同一目录 → 一起进 TPA → 所有 Mod 共享同一份类型
foreach ($lib in @('Pylon.Mod.Abstractions', 'Pylon.ModLoader', 'Pylon.ImGui')) {
    $dll = Join-Path $RepoRoot "src\csharp\managed\$lib\bin\Release\net10.0\$lib.dll"
    if (Test-Path $dll) {
        Copy-Item $dll $assetManaged -Force
        $label = if ($lib -like '*Abstractions') { 'Mod 契约' }
        elseif ($lib -like '*ImGui') { 'ImGui 绑定' }
        else { 'Mod 框架' }
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
foreach ($src in @($srcInterop)) {
    if (-not (Test-Path $src)) { Write-Warning "[部署] 缺少 $src（先跑 Il2CppInterop 生成）"; continue }
    Get-ChildItem $src -Filter '*.dll' | ForEach-Object {
        Copy-Item $_.FullName $assetInterop -Force
        $interopCount++
    }
}
$interopFiles = Get-ChildItem $assetInterop -File
Write-Host ("[部署] 代理程序集：{0} 个（含 Assembly-CSharp/Il2CppInterop.Runtime） / {1:N1} MB -> src/app/src/debug/assets/pylon-interop" -f `
    $interopCount, (($interopFiles | Measure-Object Length -Sum).Sum / 1MB))

# ③b' 仓库内自带的 Il2CppInterop（已打 ARM64/Disarm 补丁）覆盖预编译副本：
#      引擎（Il2CppInterop.Runtime.Hooking）就在这个程序集里，必须与托管层同源。
$vendoredBin = Join-Path $RepoRoot 'src\csharp\il2cppinterop\bin'
foreach ($name in @('Il2CppInterop.Runtime', 'Il2CppInterop.Common', 'Il2CppInterop.RuntimeDetour')) {
    # 必须从各项目自己的 bin 目录取：src/csharp/il2cppinterop/bin 下有多个同名副本（CLI/HookGen 会顺带拷贝），
    # -First 1 曾抓到旧的 RuntimeDetour.dll，导致真机跑的引擎不是最新（字面 InvalidProgram 复现）。
    $projectDir = if ($name -eq 'Il2CppInterop.RuntimeDetour') { 'Il2CppInterop.RuntimeDetour' }
                  elseif ($name -eq 'Il2CppInterop.Runtime') { 'Il2CppInterop.Runtime' }
                  else { 'Il2CppInterop.Common' }
    $dll = Get-ChildItem (Join-Path $vendoredBin $projectDir) -Recurse -Filter "$name.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($dll) {
        Copy-Item $dll.FullName (Join-Path $assetInterop "$name.dll") -Force
        Write-Host ("[部署] 自编 {0}：{1} KB（仓库内 Il2CppInterop）" -f $name, [int]($dll.Length / 1KB))
    } else {
        Write-Warning "[部署] 找不到自编 $name（先 dotnet build src/csharp/il2cppinterop/Il2CppInterop.Runtime）"
    }
}

# ③c Il2CppInterop.Runtime 的托管依赖（不在 .NET 运行时包里，是 NuGet 组件）
#     实测缺它会炸在 Terraria.Main..cctor()：
#       FileNotFoundException: Microsoft.Extensions.Logging.Abstractions, Version=6.0.0.0
#     上游把这些一起打进了 BepInEx.Android.zip，我们照抄它们的版本（同一构建）。
$srcBepinex = Join-Path $RuntimeRoot 'bepinex'
if (Test-Path $srcBepinex) {
    $deps = Get-ChildItem $srcBepinex -Recurse -Filter 'Microsoft.Extensions.*.dll'
    foreach ($dep in $deps) { Copy-Item $dep.FullName $assetInterop -Force }
    Write-Host ("[部署] Il2CppInterop 托管依赖：{0} 个 Microsoft.Extensions.* -> pylon-interop" -f $deps.Count)

    # ③d 不再部署上游 hook 栈（0Harmony / MonoMod / BepInEx / HarmonySupport）：
    #     hook 由 Il2CppInterop.Runtime.Hooking 直接调内核 Dobby 完成，生成的 On.* 在 pylon-managed 里。
    #     顺手清掉旧版本部署留下的文件，否则 assets 整树会把它们打进 APK。
    foreach ($stale in @('0Harmony.dll', 'Il2CppInterop.HarmonySupport.dll', 'MonoMod.Core.dll',
                         'MonoMod.Utils.dll', 'MonoMod.Backports.dll', 'MonoMod.RuntimeDetour.dll',
                         'MonoMod.ILHelpers.dll', 'MonoMod.Iced.dll', 'Iced.dll',
                         'BepInEx.Unity.IL2CPP.dll', 'BepInEx.Core.dll', 'BepInEx.Preloader.Core.dll',
                         'BepInEx.Unity.Common.dll')) {
        $path = Join-Path $assetInterop $stale
        if (Test-Path $path) { Remove-Item $path -Force }
    }
}

# ④ 内置示例 Mod 已从构建中移除：APK 不再携带任何随包 Mod。
#     示例源码仍在 samples/，要试就自己编译后把 dll 放进设备 Mods/。
#     这里顺手清理旧版本部署脚本留下的两个 assets 目录（pylon-sample-mod 是更早的
#     HelloMod 遗留物），否则 assets/ 整树仍会把它们打进 APK。
foreach ($legacy in @(
        (Join-Path $RepoRoot 'src\app\src\debug\assets\pylon-samples'),
        (Join-Path $RepoRoot 'src\app\src\debug\assets\pylon-sample-mod'))) {
    if (Test-Path $legacy) {
        Remove-Item $legacy -Recurse -Force
        Write-Host "[部署] 已移除内置示例 Mod：$legacy"
    }
}

Write-Host "[部署] 完成。接下来 :app 的 debug 构建会把这些打进 APK（不含任何随包 Mod）。"