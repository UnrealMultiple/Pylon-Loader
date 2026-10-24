# 装 APK + 启动 + 抓 ImGui 验证日志（一次性脚本，由 pwsh -File 调用）
param(
    [string]$Apk = 'C:\Users\admin\Desktop\Pylon Loader\app\build\outputs\apk\debug\app-debug.apk',
    [string]$Serial = '3b5f101'
)
$adb = 'D:\SDK\platform-tools\adb.exe'
$start = Get-Date
Write-Host "[imgui-test] 安装 $Apk ..."
& $adb -s $Serial install -r -d $Apk 2>&1 | Select-Object -Last 1
Write-Host "[imgui-test] 清日志并启动 ..."
& $adb -s $Serial logcat -c | Out-Null
& $adb -s $Serial shell am start -n dev.pylon.loader/.MainActivity -a dev.pylon.loader.action.IMPORT --ez launch true --ei settleMs 1500 --ez coreclr true | Out-Null
Write-Host "[imgui-test] 等待 45 秒进游戏 ..."
Start-Sleep -Seconds 45
$log = & $adb -s $Serial logcat -d -v time -s PylonLoader 2>&1
Write-Host "=== ImGui 相关 ==="
$log | Select-String -Pattern 'ImGui|PylonImgui|imgui' | ForEach-Object { $_.Line } | Select-Object -Last 15
Write-Host "=== 崩溃? ==="
$all = (& $adb -s $Serial logcat -d 2>&1) | Out-String -Stream | Where-Object { $_.Length -gt 0 }
$f = $all | Select-String -Pattern 'Fatal signal' | Select-Object -Last 1
if ($f) { $f.Line } else { Write-Host "无崩溃" }
Write-Host "=== 进程 ==="
& $adb -s $Serial shell "ps -A | grep 'pylon.loader:game' | head -1" 2>&1
Write-Host ("[imgui-test] 完成，耗时 " + ((Get-Date) - $start).TotalSeconds + "s")