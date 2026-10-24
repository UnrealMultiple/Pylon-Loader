<#
  层：工具 —— 从 BepInEx-Android 的发行 APK 里取出我们要的件

  为什么走"拆别人的发行包"：那个仓库的 Releases 只发启动器 APK（app-release-aligned.apk），
  不发 prebuilts 压缩包（prebuilts 是他们 CI 内部产物）。而启动器 APK 里本来就带着：
      lib/arm64-v8a/libcoreclr.so 等运行时 native
      assets/dotnet.zip           BCL（要跟 native 同构建）
      assets/BepInEx.Android.zip  托管框架（内含 Il2CppInterop.Runtime 的 ARM64 补丁版）
  所以下一个 APK 就够了。

  用法：
      pwsh -File tools/extract-bepinex-android.ps1 -Apk <path\to\app-release-aligned.apk>
      # 可选：-Sha256File <path\to\*.apk.sha256> 先核对
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Apk,
    [string]$Sha256File,
    [string]$OutDir = (Join-Path $PSScriptRoot '..\runtime\bepinex-android')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path $Apk)) { throw "找不到 APK：$Apk" }
$apkItem = Get-Item $Apk
Write-Host "[拆包] $($apkItem.FullName)（$([int]($apkItem.Length / 1MB)) MB）"

# ① 可选：核对 sha256（他们 release 里带 .sha256 文件）
if ($Sha256File -and (Test-Path $Sha256File)) {
    $expected = ((Get-Content $Sha256File -Raw).Trim() -split '\s+')[0].ToLower()
    $actual = (Get-FileHash $apkItem.FullName -Algorithm SHA256).Hash.ToLower()
    if ($expected -ne $actual) { throw "sha256 不符：期望 $expected，实际 $actual" }
    Write-Host "[拆包] sha256 核对通过：$actual"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$zip = [System.IO.Compression.ZipFile]::OpenRead($apkItem.FullName)
try {
    # 我们**不要** dobby / pine（那是他们注入别人进程用的 hook 引擎；我们自己就是宿主）。
    $skipLibs = @('libdobby.so', 'libpine.so')
    $libs = $zip.Entries | Where-Object { $_.FullName -like 'lib/arm64-v8a/*.so' }
    $assets = $zip.Entries | Where-Object { $_.FullName -like 'assets/*.zip' }

    Write-Host "`n=== APK 里的 native 库 ==="
    foreach ($e in $libs | Sort-Object FullName) {
        $isSkipped = $skipLibs -contains [System.IO.Path]::GetFileName($e.FullName)
        "  {0,-52} {1,8:N2} MB  {2}" -f (Split-Path $e.FullName -Leaf), ($e.Length / 1MB), $(if ($isSkipped) { '(跳过：注入用 hook 引擎)' } else { '' })
    }

    Write-Host "`n=== APK 里的 assets 压缩包 ==="
    foreach ($e in $assets | Sort-Object FullName) {
        "  {0,-52} {1,8:N2} MB" -f $e.FullName, ($e.Length / 1MB)
    }

    # ② 抽出来
    $libOut = Join-Path $OutDir 'lib'
    $assetOut = Join-Path $OutDir 'assets'
    New-Item -ItemType Directory -Force -Path $libOut, $assetOut | Out-Null

    foreach ($e in $libs) {
        $name = Split-Path $e.FullName -Leaf
        if ($skipLibs -contains $name) { continue }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, (Join-Path $libOut $name), $true)
    }
    foreach ($e in $assets) {
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, (Join-Path $assetOut (Split-Path $e.FullName -Leaf)), $true)
    }

    # ③ 清点 dotnet.zip / BepInEx.Android.zip 里有什么（重点找 Il2CppInterop.Runtime）
    foreach ($inner in Get-ChildItem $assetOut -Filter '*.zip') {
        Write-Host "`n=== $($inner.Name) 内容概览 ==="
        $innerZip = [System.IO.Compression.ZipFile]::OpenRead($inner.FullName)
        try {
            $interesting = $innerZip.Entries | Where-Object {
                $_.Name -match 'Il2CppInterop|Cpp2IL|MonoMod|BepInEx\.(Core|Unity)|Il2CppInterop\.Runtime|\.runtimeconfig\.json$'
            }
            if ($interesting) {
                $interesting | Select-Object -First 25 | ForEach-Object { "  $($_.FullName)  ($([int]($_.Length / 1KB)) KB)" }
            } else {
                "  条目数 $($innerZip.Entries.Count)，前 20 个："
                $innerZip.Entries | Select-Object -First 20 | ForEach-Object { "  $($_.FullName)" }
            }
        } finally { $innerZip.Dispose() }
    }
} finally { $zip.Dispose() }

Write-Host "`n[拆包] 产物目录：$OutDir"
Write-Host "[拆包] 注意：libc++_shared.so 有冲突风险（.NET 运行时自带一份，游戏也带一份），实测后再决定用谁的。"
