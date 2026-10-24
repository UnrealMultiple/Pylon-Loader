# 一次走完：编译托管 → 部署进 assets → 编 APK → 装 → 只启动一次 → 崩溃后 dump 日志
$ErrorActionPreference = 'Continue'
$adb = 'D:\SDK\platform-tools\adb.exe'
$root = 'C:\Users\admin\Desktop\Pylon Loader'
$out = Join-Path $env:TEMP 'pylon-test'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$env:JAVA_HOME = 'D:\Android Studio\jbr'
$env:GRADLE_USER_HOME = Join-Path $root '.gradle-home'

function Step($m) { Write-Host "[run] $m" }

Step '① 编译托管的三个程序集 + 示例 Mod'
foreach ($p in @('managed\Pylon.Mod.Abstractions', 'managed\Pylon.ModLoader', 'managed\Pylon.Managed', 'samples\FrameMod')) {
    & dotnet build (Join-Path $root $p) -c Release -v q  --nologo -m:1 -p:RestoreUseStaticGraphEvaluation=true 2>&1 | Select-Object -Last 3
}

Step '② 部署运行时与托管入口进 assets（这一步会把新 DLL 打进 APK）'
& pwsh -NoProfile -File (Join-Path $root 'tools\deploy-runtime.ps1') 2>&1 | Select-String -Pattern '托管入口|Mod 框架|示例 Mod FrameMod|完成|失败' | ForEach-Object { $_.Line }

Step '③ 编 APK'
& (Join-Path $root 'gradlew.bat') --offline -q :app:assembleDebug --project-dir $root 2>&1 | Select-Object -Last 5
$apk = Join-Path $root 'app\build\outputs\apk\debug\app-debug.apk'
Write-Host ("[run] APK: {0:N1} MB  {1}" -f ((Get-Item $apk).Length / 1MB), (Get-Item $apk).LastWriteTime)

Step '④ 安装'
& $adb install -r -d $apk 2>&1 | Select-Object -Last 1

Step '⑤ 清日志、只启动一次'
& $adb shell am force-stop dev.pylon.loader | Out-Null
Start-Sleep -Milliseconds 800
& $adb logcat -c
& $adb shell am start -n dev.pylon.loader/.MainActivity -a dev.pylon.loader.action.IMPORT `
    --ez launch true --ei settleMs 1500 --ez coreclr true | Out-Null

Step '⑥ 等崩溃后 dump（25s）'
Start-Sleep -Seconds 25
& $adb shell "logcat -d -v time > /data/local/tmp/pylon-log.txt"
& $adb pull /data/local/tmp/pylon-log.txt (Join-Path $out 'run.txt') | Out-Null
Write-Host ("[run] 日志 -> " + (Join-Path $out 'run.txt'))
