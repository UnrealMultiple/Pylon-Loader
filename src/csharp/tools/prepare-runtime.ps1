<#
  层：工具 —— 把 BepInEx-Android 发行 APK 里的运行时摊成我们能直接用的布局

  前置：先跑 tools/extract-bepinex-android.ps1 得到
        runtime/bepinex-android/lib/*.so        运行时 native（coreclr/jit/PAL）
        runtime/bepinex-android/assets/*.zip    dotnet.zip(BCL) + BepInEx.Android.zip(托管框架)

  本脚本把两个 zip 解开：
    assets/dotnet.zip          -> runtime/bepinex-android/dotnet/    (BCL + PAL 副本)
    assets/BepInEx.Android.zip -> runtime/bepinex-android/bepinex/ (托管栈)
  Il2CppInterop 不再从这里拷：源码与 ARM64 补丁在仓库内 src/csharp/il2cppinterop。

  用法：pwsh -File tools/prepare-runtime.ps1
#>
[CmdletBinding()]
param(
    [string]$Root = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path 'runtime\bepinex-android')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$assetsDir = Join-Path $Root 'assets'
if (-not (Test-Path $assetsDir)) { throw "先跑 src/csharp/tools/extract-bepinex-android.ps1：找不到 $assetsDir" }

function Expand-Zip($zipPath, $dest) {
    if (-not (Test-Path $zipPath)) { throw "缺少 $zipPath" }
    Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $dest)
    $count = (Get-ChildItem $dest -Recurse -File).Count
    Write-Host ("[准备] {0} -> {1}（{2} 个文件）" -f (Split-Path $zipPath -Leaf), $dest, $count)
}

# ① 摊开两个 zip
Expand-Zip (Join-Path $assetsDir 'dotnet.zip') (Join-Path $Root 'dotnet')
Expand-Zip (Join-Path $assetsDir 'BepInEx.Android.zip') (Join-Path $Root 'bepinex')

# ② 清点：运行时 native / BCL 规模 / 托管栈关键件
Write-Host "`n=== 运行时 native（要从这里进 APK 的 jniLibs/arm64-v8a） ==="
Get-ChildItem (Join-Path $Root 'lib') -Filter '*.so' | Sort-Object Name |
    ForEach-Object { "  {0,-52} {1,7:N2} MB" -f $_.Name, ($_.Length / 1MB) }

$bcl = Get-ChildItem (Join-Path $Root 'dotnet') -Filter '*.dll' -ErrorAction SilentlyContinue
Write-Host "`n=== BCL：$($bcl.Count) 个 dll，合计 $([int](($bcl | Measure-Object Length -Sum).Sum / 1MB)) MB ==="
$bcl | Where-Object { $_.Name -match '^System\.(Private\.CoreLib|Runtime|Console)\.dll$|runtimeconfig' } |
    ForEach-Object { "  $($_.Name)" }

Write-Host "`n=== 托管栈关键件（bepinex/） ==="
foreach ($n in @('BepInEx.Unity.IL2CPP.dll', 'Il2CppInterop.Runtime.dll', 'Cpp2IL.Core.dll', 'Disarm.dll', 'MonoMod.Core.dll')) {
    $f = Get-ChildItem $src -Recurse -Filter $n | Select-Object -First 1
    if ($f) { "  {0,-40} {1,7:N0} KB" -f $n, ($f.Length / 1KB) } else { "  $n  (缺失)" }
}

Write-Host "`n[准备] 完成。目录：$Root"