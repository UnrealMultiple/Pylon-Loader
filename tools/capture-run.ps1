# 一次启动 + 长窗口抓取，跑到崩溃为止（落盘后再分析，不重复启动）
$ErrorActionPreference = 'Continue'
$adb = 'D:\SDK\platform-tools\adb.exe'
$out = Join-Path $env:TEMP 'pylon-test'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$log = Join-Path $out 'run.txt'

& $adb shell am force-stop dev.pylon.loader | Out-Null
Start-Sleep -Milliseconds 800
& $adb logcat -c

$job = Start-Job -ScriptBlock {
    param($adb, $log)
    & $adb logcat -v time > $log 2>&1
} -ArgumentList $adb, $log
Start-Sleep -Seconds 2

& $adb shell am start -n dev.pylon.loader/.MainActivity -a dev.pylon.loader.action.IMPORT `
    --ez launch true --ei settleMs 1500 --ez coreclr true | Out-Null

# 100 秒足够跑到内容就绪 + 纹理上传 + 崩溃
Start-Sleep -Seconds 100
Stop-Job $job; Remove-Job $job -Force

# 顺带把最新 tombstone 与进程状态一起落盘
& $adb shell "ls -t /data/tombstones/tombstone_*" > (Join-Path $out 'tombstones.txt') 2>&1
$newest = (Get-Content (Join-Path $out 'tombstones.txt') | Where-Object { $_ -notmatch '\.pb' } | Select-Object -First 1)
if ($newest) {
    $newest = $newest.Trim()
    & $adb shell "cat $newest" > (Join-Path $out 'tombstone.txt') 2>&1
    Set-Content (Join-Path $out 'tombstone-path.txt') $newest
}
& $adb shell "ps -A" > (Join-Path $out 'ps.txt') 2>&1
Write-Host "[capture] 完成 -> $log"
