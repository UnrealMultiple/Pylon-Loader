<#
  按 pin（dotnet-runtime.env，SHA-256 锚定）从 nuget.org 抓取 CoreCLR/BCL 运行时包。

  布局：
    native/*.so                          → runtime/bepinex-android/lib/
    lib/$TFM/*.dll + json                → runtime/bepinex-android/dotnet/
    native/*.so 再复制一份进 dotnet/     （PAL 按裸名从 BCL 同目录 dlopen）

  用法：pwsh -File fetch-dotnet-runtime.ps1 [-Force] [-RuntimeRoot <dir>]
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [string]$RuntimeRoot
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if (-not $RuntimeRoot) { $RuntimeRoot = Join-Path $root 'runtime\bepinex-android' }

function Say($m) { Write-Host "   $m" }
function Die($m) { Write-Host "   $m" -ForegroundColor Red; exit 1 }

# deploy 要用的 8 个 native 库：这 8 个齐了 + BCL 目录成形，才算"就绪"。
$deployLibs = @(
    'libcoreclr.so', 'libclrjit.so', 'libmscordaccore.so', 'libmscordbi.so',
    'libSystem.Native.so', 'libSystem.Globalization.Native.so',
    'libSystem.IO.Compression.Native.so', 'libSystem.Security.Cryptography.Native.Android.so'
)

# ---- pin ----
$pinFile = Join-Path $PSScriptRoot 'dotnet-runtime.env'
if (-not (Test-Path $pinFile)) { Die "缺少 pin 文件：$pinFile" }
$pin = @{}
foreach ($line in Get-Content $pinFile) {
    $t = $line.Trim()
    if (-not $t -or $t.StartsWith('#')) { continue }
    $i = $t.IndexOf('=')
    if ($i -lt 1) { continue }
    $pin[$t.Substring(0, $i).Trim()] = $t.Substring($i + 1).Trim()
}
$pack    = $pin['DOTNET_RUNTIME_PACK']
$version = $pin['DOTNET_RUNTIME_VERSION']
$shaPin  = $pin['DOTNET_RUNTIME_PACK_SHA256']
$tfm     = $pin['DOTNET_RUNTIME_TFM']
foreach ($kv in @(@('DOTNET_RUNTIME_PACK', $pack), @('DOTNET_RUNTIME_VERSION', $version),
                  @('DOTNET_RUNTIME_PACK_SHA256', $shaPin), @('DOTNET_RUNTIME_TFM', $tfm))) {
    if (-not $kv[1]) { Die "pin 文件缺少 $($kv[0])" }
}
$feed = if ($pin['DOTNET_RUNTIME_FEED']) { $pin['DOTNET_RUNTIME_FEED'] } else { 'https://api.nuget.org/v3-flatcontainer' }

$libOut    = Join-Path $RuntimeRoot 'lib'
$dotnetOut = Join-Path $RuntimeRoot 'dotnet'

# ---- 就绪判定（幂等）----
$ready = (-not $Force) -and
    ($deployLibs | Where-Object { -not (Test-Path (Join-Path $libOut $_)) }).Count -eq 0 -and
    (Test-Path (Join-Path $dotnetOut 'Microsoft.NETCore.App.runtimeconfig.json')) -and
    ((Get-ChildItem $dotnetOut -Filter '*.dll' -ErrorAction SilentlyContinue | Measure-Object).Count -gt 50)
if ($ready) {
    Say 'runtime/ 已就绪，跳过（-Force 强制重抓）'
    exit 0
}

Write-Host "== 抓取 .NET 运行时包" -ForegroundColor Cyan
Say ("pack    : {0}" -f $pack)
Say ("version : {0}" -f $version)
Say ("tfm     : {0}" -f $tfm)

# ---- 下载 ----
$nupkgName = '{0}.{1}.nupkg' -f $pack, $version
$url = '{0}/{1}/{2}/{3}' -f $feed, $pack, $version, $nupkgName
$nupkg = Join-Path ([IO.Path]::GetTempPath()) $nupkgName
Say "source  : $url"
if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
    & curl.exe --fail --location --silent --show-error --retry 3 -o $nupkg $url
    if ($LASTEXITCODE -ne 0) { Die "下载失败：$url" }
} else {
    try { Invoke-WebRequest -Uri $url -OutFile $nupkg -ErrorAction Stop } catch { Die "下载失败：$($_.Exception.Message)" }
}
Say ("下载完成  : {0:N1} MB" -f ((Get-Item $nupkg).Length / 1MB))

# ---- 字节校验（信任锚：先对哈希，再看包内）----
$shaActual = (Get-FileHash $nupkg -Algorithm SHA256).Hash.ToLower()
if ($shaActual -ne $shaPin) {
    Die ("SHA-256 不符：期望 {0}，实际 {1}" -f $shaPin, $shaActual)
}
Say ("SHA-256 ok: {0}" -f $shaActual)

# ---- 解包 ----
Add-Type -AssemblyName System.IO.Compression.FileSystem
$nativePrefix = 'runtimes/android-arm64/native/'
$bclPrefix    = 'runtimes/android-arm64/lib/' + $tfm + '/'

# 干净重建两个目录：runtime/ 是派生数据，重建即自愈
Remove-Item $libOut, $dotnetOut -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $libOut, $dotnetOut | Out-Null

$zip = [System.IO.Compression.ZipFile]::OpenRead($nupkg)
try {
    $nNative = 0; $nBcl = 0; $nPal = 0
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName.EndsWith('/')) { continue }
        $name = [IO.Path]::GetFileName($entry.FullName)
        if (-not $name) { continue }
        if ($entry.FullName.StartsWith($nativePrefix) -and $name.EndsWith('.so')) {
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $libOut $name), $true)
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $dotnetOut $name), $true)
            $nNative++; $nPal++
        }
        elseif ($entry.FullName.StartsWith($bclPrefix) -and ($name.EndsWith('.dll') -or $name.EndsWith('.json'))) {
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $dotnetOut $name), $true)
            if ($name.EndsWith('.dll')) { $nBcl++ }
        }
    }
} finally { $zip.Dispose() }
Remove-Item $nupkg -Force -ErrorAction SilentlyContinue

Say ("native   : {0} 个 .so → lib/（另有 {1} 个 PAL 副本 → dotnet/）" -f $nNative, $nPal)
Say ("BCL      : {0} 个程序集 + json → dotnet/" -f $nBcl)

# ---- 部署面自检：deploy 要的 8 个一个都不能少 ----
$missing = $deployLibs | Where-Object { -not (Test-Path (Join-Path $libOut $_)) }
if ($missing) { Die ("运行时包缺部署件：{0}" -f ($missing -join ', ')) }
if ($nBcl -lt 50) { Die ("BCL 程序集过少（{0} 个），包不完整" -f $nBcl) }
if (-not (Test-Path (Join-Path $dotnetOut 'Microsoft.NETCore.App.runtimeconfig.json'))) {
    Die '包内没有 Microsoft.NETCore.App.runtimeconfig.json（运行时身份文件）'
}
Say 'runtime/ 重建完成。'
