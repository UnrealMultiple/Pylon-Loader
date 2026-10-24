<#
  层：工具 —— 只推托管 DLL（不重编 APK）

  为什么可行：宿主的 RuntimeDeploy 用"assets 指纹 + 版本"判断要不要重解；
  把新 DLL 直接 run-as 拷进 files/pylon-managed 后，标记与设备上 APK 里的 assets 仍然对得上，
  下次启动不会再解一遍，于是跑的就是推上去的这份（debuggable 包才能 run-as）。

  用法：
    pwsh -File tools/push-managed.ps1                # 构建 + 推四个 DLL
    pwsh -File tools/push-managed.ps1 -NoBuild       # 只推当前产物
#>
[CmdletBinding()]
param(
    [switch]$NoBuild,
    [string]$Serial = '3b5f101'
)

$ErrorActionPreference = 'Stop'
$adb = 'D:\SDK\platform-tools\adb.exe'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pkg = 'dev.pylon.loader'

function Adb { & $adb -s $Serial @args }
function Step($m) { Write-Host "[managed] $m" }

if (-not $NoBuild) {
    Step '构建托管程序集'
    foreach ($p in @('managed\Pylon.Mod.Abstractions', 'managed\Pylon.ModLoader', 'managed\Pylon.Managed')) {
        $log = Join-Path $env:TEMP ("pylon-push-" + (Split-Path $p -Leaf) + ".log")
        dotnet build (Join-Path $root $p) -c Release -v minimal --nologo -m:1 `
            -p:RestoreUseStaticGraphEvaluation=true > $log 2>&1
        if ($LASTEXITCODE -ne 0) { Get-Content $log | Select-Object -Last 12; throw "$p 编译失败" }
    }
    Step '构建 OK'
}

$files = @(
    'managed\Pylon.ModLoader\bin\Release\net10.0\Pylon.ModLoader.dll',
    'managed\Pylon.Managed\bin\Release\net10.0\Pylon.Managed.dll',
    'managed\Pylon.Mod.Abstractions\bin\Release\net10.0\Pylon.Mod.Abstractions.dll',
    'managed\Pylon.ImGui\bin\Release\net10.0\Pylon.ImGui.dll'
)

foreach ($rel in $files) {
    $from = Join-Path $root $rel
    if (-not (Test-Path $from)) { Write-Warning "缺少产物：$rel"; continue }
    $name = Split-Path $rel -Leaf
    Adb push $from "/data/local/tmp/$name" | Out-Null
    Adb shell "run-as $pkg cp /data/local/tmp/$name files/pylon-managed/$name"
    Adb shell "rm -f /data/local/tmp/$name" | Out-Null
    Write-Host ("[managed]   {0} ({1} KB)" -f $name, [int]((Get-Item $from).Length / 1KB))
}

Step '已推入 files/pylon-managed（下次启动生效）'
