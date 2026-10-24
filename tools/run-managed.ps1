<#
  层：工具 —— 只管托管侧的快速迭代回路（不重新编 APK）

  为什么能这么快：
    · RuntimeDeploy 对 assets/pylon-managed 用的是**指纹**（文件数 + 总字节），
      Pylon.ModLoader.dll / Pylon.Managed.dll 一变，宿主下次启动就自己重解，
      所以改框架**不用**重编 APK、也不用 +1 DEPLOY_VERSION。
    · 示例 Mod 是普通文件，直接 adb push 进 Mods/<名字>/ 就行；
      改宿主-native / jniLibs / manifest 才需要 tools/full-test.ps1 重编 APK。

  用法：
    pwsh -File tools/run-managed.ps1                     # 编译 + 推全部示例 + 重启 + 抓日志
    pwsh -File tools/run-managed.ps1 -Samples FrameMod   # 只推指定的示例
    pwsh -File tools/run-managed.ps1 -Seconds 20         # 改抓日志的时长
#>
[CmdletBinding()]
param(
    [string[]]$Samples = @('FrameMod'),
    [int]$Seconds = 25,
    [switch]$NoDeploy
)

$ErrorActionPreference = 'Continue'
$adb = 'D:\SDK\platform-tools\adb.exe'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$pkg = 'dev.pylon.loader'
$modsDir = "/storage/emulated/0/Android/data/$pkg/Mods"
$out = Join-Path $env:TEMP 'pylon-managed'
New-Item -ItemType Directory -Force -Path $out | Out-Null

function Step($m) { Write-Host "[managed] $m" }

# ① 框架 + 托管入口
Step '① 编译框架与托管入口'
foreach ($p in @('managed\Pylon.Mod.Abstractions', 'managed\Pylon.ModLoader', 'managed\Pylon.Managed')) {
    $log = Join-Path $env:TEMP ("pylon-" + (Split-Path $p -Leaf) + ".log")
    dotnet build (Join-Path $root $p) -c Release -v minimal  --nologo -m:1 -p:RestoreUseStaticGraphEvaluation=true > $log 2>&1
    if ($LASTEXITCODE -ne 0) { Get-Content $log | Select-Object -Last 12; throw "$p 编译失败" }
}
Step '框架与托管入口 OK'

# ② 推进 assets（宿主靠指纹自己重解；不重编 APK）
if (-not $NoDeploy) {
    Step '② 更新 assets/pylon-managed'
    & pwsh -NoProfile -File (Join-Path $root 'tools\deploy-runtime.ps1') 2>&1 |
        Select-String -Pattern '托管入口|Mod 契约|Mod 框架|完成' | ForEach-Object { "    " + $_.Line }
}

# ③ 示例 Mod：编好直接 adb push
Step '③ 推示例 Mod 进 Mods/'
foreach ($name in $Samples) {
    $proj = Join-Path $root "samples\$name\$name.csproj"
    if (-not (Test-Path $proj)) { Write-Warning "跳过 $name（没有 $proj）"; continue }

    # 示例按 DLL 引用框架产物 → 框架换了必须强制重编，否则 dotnet 的增量检查不会发现 HintPath 变了
    $log = Join-Path $env:TEMP "pylon-sample-$name.log"
    dotnet build $proj -c Release --no-incremental -v minimal  --nologo -m:1 -p:RestoreUseStaticGraphEvaluation=true > $log 2>&1
    if ($LASTEXITCODE -ne 0) { Get-Content $log | Select-Object -Last 12; Write-Warning "$name 编译失败，跳过"; continue }

    $dll = Get-ChildItem (Join-Path $root "samples\$name\bin\Release") -Recurse -Filter "$name.dll" | Select-Object -First 1
    if (-not $dll) { Write-Warning "找不到 $name.dll"; continue }

    & $adb shell "mkdir -p $modsDir/$name" | Out-Null
    & $adb push $dll.FullName "$modsDir/$name/$name.dll" 2>&1 | Select-Object -Last 1 | ForEach-Object { "    $name.dll -> $_" }

    # 私有依赖（DepMod 的 lib/）
    $libDir = Join-Path $root "samples\$name\lib"
    if (Test-Path $libDir) {
        foreach ($dep in Get-ChildItem $libDir -Filter *.dll) {
            & $adb push $dep.FullName "$modsDir/$name/lib/$dep.Name" 2>&1 | Out-Null
        }
    }
    # 贴图等资源
    # 注意 adb push 的目录语义：本地目录推进**已存在**的远端目录会变成 assets/assets。
    # 用通配符把子树内容铺到 assets/ 里才是 Mod 的布局。
    $assetsDir = Join-Path $root "samples\$name\assets"
    if (Test-Path $assetsDir) {
        & $adb shell "mkdir -p '$modsDir/$name/assets'" | Out-Null
        # adb 不展开通配符：直接推目录本身（目标已存在时推父目录，避免 assets/assets）
        & $adb push $assetsDir "$modsDir/$name" 2>&1 | Select-Object -Last 1 | ForEach-Object { "    assets -> $_" }
    }

    # manifest：只在设备上没有时写一份
    $manifest = Join-Path $root "samples\$name\manifest.json"
    if (-not (Test-Path $manifest)) {
        $names = @{
            FrameMod = '框架示例：五条管线自检（坐骑/Buff/NPC/配方/前缀）+ ImGui Tab + ModConfig'
        }
        $json = @{ id = $name; name = $names[$name]; version = '1.0.0'; enabled = $true } | ConvertTo-Json
        $tmp = Join-Path $out "$name-manifest.json"
        Set-Content -Path $tmp -Value $json -Encoding UTF8
        & $adb push $tmp "$modsDir/$name/manifest.json" 2>&1 | Out-Null
    }
}

# ④ 冷启动一次（force-stop 后 Mods 会重新扫描）+ 抓日志
Step "④ 重启一次并抓 $Seconds 秒日志"
& $adb shell am force-stop $pkg | Out-Null
Start-Sleep -Milliseconds 800
& $adb logcat -c
& $adb shell am start -n "$pkg/.MainActivity" -a "$pkg.action.IMPORT" --ez launch true --ei settleMs 1500 --ez coreclr true | Out-Null
Start-Sleep -Seconds $Seconds
& $adb shell "logcat -d -v time > /data/local/tmp/pylon-managed-log.txt"
$logFile = Join-Path $out 'run.txt'
& $adb pull /data/local/tmp/pylon-managed-log.txt $logFile 2>&1 | Out-Null
Step "日志 -> $logFile"

# ⑤ 直接把 Mod 配置界面相关的行打出来
$lines = [System.IO.File]::ReadAllLines($logFile)
$lines | Where-Object { $_ -match 'PylonLoader' -and $_ -match 'Mod 配置|\[\w+\]|警告|错误|✗' } |
    ForEach-Object { ($_ -replace '^\d+-\d+ \d+:\d+:\d+\.\d+\s+\d+\s+\d+\s+', '').Trim() } |
    Select-Object -Last 60
