<#
  层：工具 —— 一条命令跑完「从空 clone 到可安装 APK」

  为什么需要它：仓库故意不含三类二进制（游戏 dump / BepInEx 运行时 / Il2CppInterop 代理），
  补齐它们分属不同工具（拆包、生成器、NDK、dotnet、Gradle），散落成七八条命令时极容易漏步。
  本脚本按阶段串起来，**每一阶段幂等**：已就绪就跳过，缺件就给出确切缺什么、要传哪个参数。

  阶段：
    deps     子模块（external/Il2CppInterop、external/BepInEx-Android）
    runtime  runtime/  ← 需要 -BepInExApk（上游 BepInEx-Android release APK）
    interop  interop/generated/ ← 需要 -DummyDir（Il2CppDumper 的 DummyDll 目录）
    core     src/app/src/main/jniLibs/arm64-v8a/libpylon.so
    managed  src/app/src/debug/assets/pylon-{managed,interop,dotnet}/ + 示例 Mod
    apk      src/app/build/outputs/apk/debug/app-debug.apk
    install  装到设备（需 -Install）

  用法：
    pwsh -File src/csharp/tools/pipeline.ps1 -BepInExApk D:\dl\app-release-aligned.apk -DummyDir refs\DummyDll
    pwsh -File tools/pipeline.ps1 -Stages apk            # 只重打 APK
    pwsh -File tools/pipeline.ps1 -Install -Serial 3b5f101
#>
[CmdletBinding()]
param(
    # 上游 BepInEx-Android 的 release APK；缺 runtime/ 时必填
    [string]$BepInExApk,
    # Il2CppDumper 输出的 DummyDll 目录；缺 interop/generated/ 时必填
    [string]$DummyDir,
    # 只跑这些阶段（默认全跑）
    [string[]]$Stages = @('deps', 'runtime', 'interop', 'core', 'managed', 'apk'),
    # 已就绪也重做（runtime/interop 会重新解包/重新生成）
    [switch]$Force,
    [switch]$Install,
    [string]$Serial = '3b5f101',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Set-Location $root

$adb = 'D:\SDK\platform-tools\adb.exe'
$env:JAVA_HOME = if ($env:JAVA_HOME) { $env:JAVA_HOME } else { 'D:\Android Studio\jbr' }
$env:GRADLE_USER_HOME = Join-Path $root '.gradle-home'
# 沙箱/无家目录机器上，MSBuild 与 NuGet 都收进工作区（与 .gitignore 的 .gradle-home 同思路）
$env:TEMP = Join-Path $env:GRADLE_USER_HOME 'msbuild-temp'
$env:TMP = $env:TEMP
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $env:GRADLE_USER_HOME 'nuget-packages' }
New-Item -ItemType Directory -Force -Path $env:TEMP, $env:NUGET_PACKAGES | Out-Null

function Stage($name) { Write-Host "`n=== [$name] ===" -ForegroundColor Cyan }
function Ok($m) { Write-Host "  [ok]   $m" -ForegroundColor Green }
function Skip($m) { Write-Host "  [跳过] $m" -ForegroundColor DarkGray }
function Todo($m) { Write-Host "  [跑]   $m" -ForegroundColor Yellow }
function Fail($m) { Write-Host "  [失败] $m" -ForegroundColor Red; exit 1 }

function Want($name) { $Stages -contains $name }

# ---------- deps：子模块 ----------
if (Want 'deps') {
    Stage 'deps'
    $missing = @('external\BepInEx-Android') | Where-Object { -not (Test-Path (Join-Path $_ '.git')) }
    if ($missing.Count -eq 0) {
        Skip '子模块已就绪'
    } else {
        Todo ('补齐子模块：' + ($missing -join ', '))
        # 用 clone 而不是 git submodule：某些受限环境里 submodule 的 shell 脚本起不来
        & git submodule update --init --recursive 2>&1 | Select-Object -Last 2
        if ($LASTEXITCODE -ne 0) {
            foreach ($m in $missing) {
                & git clone -q 'https://github.com/Tb1281/BepInEx-Android.git' $m 2>&1 | Select-Object -Last 1
            }
        }
        foreach ($m in $missing) { if (-not (Test-Path (Join-Path $m '.git'))) { Fail "子模块仍缺失：$m（手动 git submodule update --init --recursive）" } }
        Ok '子模块已补齐'
    }
}

# ---------- runtime：dotnet.zip / BepInEx.Android.zip → runtime/ ----------
if (Want 'runtime') {
    Stage 'runtime'
    $rt = 'runtime\bepinex-android'
    $ready = (Test-Path "$rt\lib") -and (Test-Path "$rt\dotnet") -and (Test-Path "$rt\bepinex")
    if ($ready -and -not $Force) {
        Skip 'runtime/bepinex-android 已就绪（BCL + 托管栈 + native）'
    } else {
        if (-not $BepInExApk) { Fail '缺 runtime/：请传 -BepInExApk <上游 BepInEx-Android release APK>（见 docs/构建-从零开始.md 第 3 步）' }
        if (-not (Test-Path $BepInExApk)) { Fail "找不到 APK：$BepInExApk" }
        Todo "拆包 $BepInExApk"
        & pwsh -NoProfile -File src\csharp\tools\extract-bepinex-android.ps1 -Apk $BepInExApk | Select-Object -Last 3
        if ($LASTEXITCODE -ne 0) { Fail 'extract-bepinex-android.ps1 失败' }
        Todo '摊开 dotnet.zip / BepInEx.Android.zip'
        & pwsh -NoProfile -File src\csharp\tools\prepare-runtime.ps1 | Select-Object -Last 3
        if ($LASTEXITCODE -ne 0) { Fail 'prepare-runtime.ps1 失败' }
        Ok 'runtime/ 已就绪'
    }
}

# ---------- interop：Il2CppInterop 代理程序集 ----------
if (Want 'interop') {
    Stage 'interop'
    $gen = 'interop\generated'
    $ready = (Test-Path "$gen\Assembly-CSharp.dll") -and (Test-Path "$gen\Il2Cppmscorlib.dll")
    if ($ready -and -not $Force) {
        Skip 'interop/generated 已就绪'
    } else {
        if (-not $DummyDir) { Fail '缺 interop/generated：请传 -DummyDir <Il2CppDumper 输出的 DummyDll 目录>（见 docs/构建-从零开始.md 第 4 步）' }
        if (-not (Test-Path $DummyDir)) { Fail "找不到 DummyDll 目录：$DummyDir" }

        $cli = 'src\csharp\il2cppinterop\Il2CppInterop.CLI\Il2CppInterop.CLI.csproj'
        $cliDll = 'src\csharp\il2cppinterop\bin\Il2CppInterop.CLI\net10.0\Il2CppInterop.CLI.dll'
        if (-not (Test-Path $cliDll)) {
            Todo '编译 Il2CppInterop.CLI（仓库内生成器）'
            & dotnet build $cli -c $Configuration --nologo -v q -m:1 -nodeReuse:false -p:MSBUILDNOINPROCNODE=1 -p:UseSharedCompilation=false | Select-Object -Last 4
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path $cliDll)) {
                Fail '生成器编译失败（常见原因：无法访问 nuget.org 还包，或受限环境 MSBuild 起不了节点；到正常机器/CI 上跑这一步）'
            }
        }

        Todo "生成代理程序集：$DummyDir → $gen"
        $gameAsm = 'refs\lib\arm64-v8a\libil2cpp.so'
        $cliArgs = @($cliDll, 'generate', '--input', (Resolve-Path $DummyDir), '--output', (Join-Path $root $gen))
        if (Test-Path $gameAsm) { $cliArgs += @('--game-assembly', (Resolve-Path $gameAsm)) } else { Write-Host "  [注意] 没有 $gameAsm，部分分析会退化为运行时扫描" -ForegroundColor DarkYellow }
        & dotnet @cliArgs 2>&1 | Select-Object -Last 6
        if ($LASTEXITCODE -ne 0) { Fail 'Il2CppInterop 生成失败' }
        foreach ($need in @('Assembly-CSharp.dll', 'Il2Cppmscorlib.dll', 'MethodAddressToToken.db')) {
            if (-not (Test-Path (Join-Path $gen $need))) { Fail "生成结果缺 $need（生成器输出不完整）" }
        }
        Ok 'interop/generated 已就绪（含 xref 缓存）'
    }

    # 无论刚生成还是已就绪，都（幂等）补一遍 On.*：hookgen 会先摘掉上一次的 On.* 再重发。
    $cli = 'src\csharp\il2cppinterop\Il2CppInterop.CLI\Il2CppInterop.CLI.csproj'
    $cliDll = 'src\csharp\il2cppinterop\bin\Il2CppInterop.CLI\net10.0\Il2CppInterop.CLI.dll'
    if (-not (Test-Path $cliDll)) {
        Todo '编译 Il2CppInterop.CLI（On.* 生成器）'
        & dotnet build $cli -c $Configuration --nologo -v q -m:1 -nodeReuse:false -p:MSBUILDNOINPROCNODE=1 -p:UseSharedCompilation=false | Select-Object -Last 4
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $cliDll)) { Fail '生成器编译失败' }
    }
    Todo '把 On.* 钩子写进代理程序集'
    & dotnet $cliDll hookgen --input $gen 2>&1 | Select-Object -Last 2
    if ($LASTEXITCODE -ne 0) { Fail 'On.* 生成失败' }
    Ok 'On.* 已写入 interop/generated/Assembly-CSharp.dll'
}

# ---------- core：libpylon.so ----------
if (Want 'core') {
    Stage 'core'
    $so = 'src\app\src\main\jniLibs\arm64-v8a\libpylon.so'
    $srcNewest = Get-ChildItem 'src\core\src', 'src\core\include' -Recurse -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $stale = (-not (Test-Path $so)) -or ($Force) -or ($srcNewest -and $srcNewest.LastWriteTime -gt (Get-Item $so).LastWriteTime)
    if (-not $stale) {
        Skip 'libpylon.so 比内核源码新'
    } else {
        Todo '编译内核（NDK clang++）'
        & pwsh -NoProfile -File src\core\build.ps1 | Select-Object -Last 4
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $so)) { Fail 'src/core/build.ps1 失败' }
        Ok 'libpylon.so 已更新'
    }
}

# ---------- managed：托管件 + 运行时进 debug 变体 ----------
if (Want 'managed') {
    Stage 'managed'
    $probe = 'src\app\src\debug\assets\pylon-managed\Pylon.ModLoader.dll'
    if ((Test-Path $probe) -and -not $Force) {
        Skip 'debug 变体的运行时/托管件已部署'
    } else {
        Todo '部署运行时与托管件（会顺带编托管件、Hook 引擎与生成的 On.*）'
        & pwsh -NoProfile -File src\csharp\tools\deploy-runtime.ps1 2>&1 | Select-Object -Last 4
        if ($LASTEXITCODE -ne 0) { Fail 'deploy-runtime.ps1 失败' }
        Ok 'debug 变体已部署'
    }
}

# ---------- apk ----------
if (Want 'apk') {
    Stage 'apk'
    Todo '打包 APK'
    & .\gradlew.bat --offline -q :app:assembleDebug --project-dir $root 2>&1 |
        Select-String -Pattern 'FAILED|error|BUILD' | Select-Object -Last 4
    $apk = 'src\app\build\outputs\apk\debug\app-debug.apk'
    if (-not (Test-Path $apk)) { Fail 'APK 未生成' }
    $f = Get-Item $apk
    Ok ("APK {0:N1} MB  {1}" -f ($f.Length / 1MB), $f.LastWriteTime)
}

# ---------- install ----------
if ($Install) {
    Stage 'install'
    $apk = 'src\app\build\outputs\apk\debug\app-debug.apk'
    if (-not (Test-Path $adb)) { Fail "找不到 adb：$adb" }
    Todo "安装到 $Serial"
    & $adb -s $Serial install -r -d $apk 2>&1 | Select-Object -Last 1
    if ($LASTEXITCODE -ne 0) { Fail '安装失败（设备未连接/签名冲突）' }
    & $adb -s $Serial shell am force-stop dev.pylon.loader | Out-Null
    & $adb -s $Serial shell am start -n dev.pylon.loader/.MainActivity -a dev.pylon.loader.action.IMPORT --ez launch true --ei settleMs 1500 --ez coreclr true | Out-Null
    Ok '已安装并启动（日志：adb logcat -s PylonLoader）'
}

Write-Host "`n完成。" -ForegroundColor Green