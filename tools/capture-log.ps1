# 单次运行 + 整份抓日志（不重复启动；日志落盘后再分析）
$ErrorActionPreference = 'Continue'
$adb = 'D:\SDK\platform-tools\adb.exe'
$out = Join-Path $env:TEMP 'pylon-test'
New-Item -ItemType Directory -Force -Path $out | Out-Null

& $adb shell am force-stop dev.pylon.loader | Out-Null
Start-Sleep -Milliseconds 800
& $adb logcat -c
& $adb logcat -v time > (Join-Path $out 'logcat.txt') 2>&1
