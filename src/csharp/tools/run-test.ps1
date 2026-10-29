<#
  快速真机测试：启动后轮询日志，命中标记立刻返回（不傻等）。

    -PushMod     只改 Mod：直接推到 Mods/，不重装不重编
    -PushManaged 只改托管入口：run-as 拷进 filesDir，不重装
    -Apk         改了内核/资源才需要重装

  -WaitFor 是正则（pwsh -File 传不了数组），多个标记用 | 分隔。
#>
[CmdletBinding()]
param(
    [string]$Apk,
    # 注意：pwsh -File 传不了数组，所以这里是一个**正则**（多个标记用 | 分隔）
    [string]$WaitFor = 'Mod 加载完成|托管入口返回|✗',
    [int]$TimeoutSec = 60,
    [int]$SettleMs = 1500,
    [switch]$BindTest,
    [string]$PushMod,
    [string]$PushModName,
    [string]$PushManaged,
    [string]$Serial = '3b5f101'
)

$ErrorActionPreference = 'Stop'
$adb = 'D:\SDK\platform-tools\adb.exe'
$pkg = 'dev.pylon.loader'
$activity = "$pkg/.MainActivity"

function Adb { & $adb -s $Serial @args }

if ($Apk) {
    Write-Host "[测试] 安装 $Apk ..."
    $t = [Diagnostics.Stopwatch]::StartNew()
    Adb install -r -d $Apk | Select-Object -Last 1
    Write-Host ("[测试] 安装耗时 {0:N0}s" -f $t.Elapsed.TotalSeconds)
}

if ($PushMod) {
    if (-not $PushModName) { $PushModName = [System.IO.Path]::GetFileNameWithoutExtension($PushMod) }
    $modsRoot = "/storage/emulated/0/Android/data/$pkg/Mods/$PushModName"
    Adb shell "mkdir -p '$modsRoot'"
    Adb push $PushMod "$modsRoot/mod.dll" | Select-Object -Last 1
    Write-Host "[测试] 已推 Mod：$PushModName/mod.dll（无需重装）"
}

if ($PushManaged) {
    # filesDir 是 app 私有：先推到 /data/local/tmp，再用 run-as 拷进去（debuggable 包才能 run-as）
    Adb push $PushManaged /data/local/tmp/Pylon.Managed.dll | Select-Object -Last 1
    Adb shell "run-as $pkg cp /data/local/tmp/Pylon.Managed.dll files/pylon-managed/Pylon.Managed.dll"
    Adb shell "rm -f /data/local/tmp/Pylon.Managed.dll"
    Write-Host "[测试] 已推托管入口（无需重装）"
}

Adb shell am force-stop $pkg | Out-Null
Start-Sleep -Milliseconds 800
Adb logcat -c | Out-Null

$extras = @('--ez', 'launch', 'true', '--ei', 'settleMs', $SettleMs, '--ez', 'coreclr', 'true')
if ($BindTest) { $extras += @('--ez', 'bindTest', 'true') }
Adb shell am start -S -n $activity -a "$pkg.action.IMPORT" @extras | Out-Null

$sw = [Diagnostics.Stopwatch]::StartNew()
$hit = $null
while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
    Start-Sleep -Milliseconds 1200
    $log = Adb logcat -d -v time -s PylonLoader 2>&1
    $match = $log | Select-String -Pattern $WaitFor | Select-Object -First 1
    if ($match) { $hit = $match.Line.Trim(); break }
}

Write-Host ("[测试] 命中标记，耗时 {0:N1}s（超时 {1}s）" -f $sw.Elapsed.TotalSeconds, $TimeoutSec)
if (-not $hit) { Write-Host "[测试] 未命中，下面是现有日志：" }
($log | Select-String -Pattern 'Mod 扫描|\[hook\]|已挂|已撤|· |HookMod|DrawMenu|卸载|Mod 加载完成|✗|启动 CoreCLR|coreclr_initialize|已装') |
    ForEach-Object { $_.Line } | Select-Object -Last 30