<#
  Pylon 构建 / 部署 / 真机测试的单一入口。

    pwsh -File src/csharp/tools/pylon.ps1 check
    pwsh -File src/csharp/tools/pylon.ps1 runtime -Apk D:\dl\app-release-aligned.apk
    pwsh -File src/csharp/tools/pylon.ps1 interop -DummyDir refs\DummyDll
    pwsh -File src/csharp/tools/pylon.ps1 build
    pwsh -File src/csharp/tools/pylon.ps1 run   -Apk -WaitFor 'Mod 加载完成'
    pwsh -File src/csharp/tools/pylon.ps1 push  -Samples FrameMod
    pwsh -File src/csharp/tools/pylon.ps1 all   -BepInExApk <apk> -DummyDir <dir>
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$Command = 'help',
    [string]$BepInExApk,
    [string]$Sha256File,
    [string]$DummyDir,
    [switch]$Force,
    [switch]$Apk,
    [string[]]$Samples = @('FrameMod'),
    [switch]$Managed,
    [switch]$NoBuild,
    [string]$WaitFor = '内容安装完成|✗|FATAL',
    [int]$Seconds = 90,
    [string]$Serial = '3b5f101',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
Set-Location $root

$pkg = 'dev.pylon.loader'
$apkPath = Join-Path $root 'src\app\build\outputs\apk\debug\app-debug.apk'
$logDir = Join-Path $root '.work\logs'
$jniMain = 'src\app\src\main\jniLibs\arm64-v8a'
$jniDebug = 'src\app\src\debug\jniLibs\arm64-v8a'
$assetDotnet = 'src\app\src\debug\assets\pylon-dotnet'
$assetManaged = 'src\app\src\debug\assets\pylon-managed'
$assetInterop = 'src\app\src\debug\assets\pylon-interop'
$runtimeRoot = 'runtime\bepinex-android'
$interopGen = 'interop\generated'

$adb = if ($env:PYLON_ADB) { $env:PYLON_ADB }
       elseif (Test-Path 'D:\SDK\platform-tools\adb.exe') { 'D:\SDK\platform-tools\adb.exe' }
       else { (Get-Command adb -ErrorAction SilentlyContinue).Source }

$env:JAVA_HOME = if ($env:JAVA_HOME) { $env:JAVA_HOME } else { 'D:\Android Studio\jbr' }
$env:GRADLE_USER_HOME = Join-Path $root '.gradle-home'
$env:TEMP = Join-Path $env:GRADLE_USER_HOME 'tmp'
$env:TMP = $env:TEMP
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = Join-Path $env:GRADLE_USER_HOME 'nuget-packages' }
foreach ($dir in @($env:TEMP, $env:NUGET_PACKAGES, $logDir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

function Step($message) { Write-Host "== $message" -ForegroundColor Cyan }
function Say($message) { Write-Host "   $message" }
function Warn($message) { Write-Host "   $message" -ForegroundColor Yellow }
function Die($message) { Write-Host "   $message" -ForegroundColor Red; exit 1 }
function Need($path, $hint) { if (-not (Test-Path $path)) { Die "缺少 $path（$hint）" } }

function Invoke-Dotnet($project, $extra) {
    $log = Join-Path $env:TEMP ('pylon-' + (Split-Path $project -Leaf) + '.log')
    $args = @('build', (Join-Path $root $project), '-c', $Configuration, '-v', 'minimal', '--nologo', '-m:1', '-p:RestoreUseStaticGraphEvaluation=true') + $extra
    & dotnet @args > $log 2>&1
    if ($LASTEXITCODE -ne 0) { Get-Content $log | Select-Object -Last 12; Die "$project 编译失败" }
}

function Invoke-Adb { & $adb -s $Serial @args }

# adb.exe 在 Windows 上按 OEM 代码页输出日志：先按 UTF-8 严格解码，失败再退回 OEM（中文机器是 936）。
function Get-LogText($path) {
    if (-not (Test-Path $path)) { return '' }
    try {
        $stream = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
        $buffer = New-Object System.IO.MemoryStream
        $stream.CopyTo($buffer)
        $stream.Dispose()
        $bytes = $buffer.ToArray()
    } catch { return '' }
    if ($bytes.Length -eq 0) { return '' }
    $strict = New-Object System.Text.UTF8Encoding($false, $true)
    try { return $strict.GetString($bytes) } catch { }
    # 个别坏字节不该让整份日志变成乱码：UTF-8 坏字符很少就按 UTF-8 解，否则按 OEM（adb 老路径）
    $utf8 = [System.Text.Encoding]::UTF8.GetString($bytes)
    $badCount = ([regex]::Matches($utf8, [string][char]0xFFFD)).Count
    if ($badCount -le [int]($bytes.Length / 200)) { return $utf8 }
    return [System.Text.Encoding]::GetEncoding([System.Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage).GetString($bytes)
}

function Build-Managed {
    foreach ($p in @('src\csharp\managed\Pylon.Mod.Abstractions', 'src\csharp\managed\Pylon.NaviteAPI',
                     'src\csharp\managed\Pylon.ModLoader', 'src\csharp\managed\Pylon.Managed')) {
        if (Test-Path (Join-Path $root $p)) { Invoke-Dotnet $p @() }
    }
    Invoke-Dotnet 'src\csharp\managed\Pylon.Managed' @()
}

function Command-Push {
    if (-not $NoBuild) { Build-Managed }
    Step '推托管件到 files/pylon-managed'
    foreach ($name in @('Pylon.ModLoader', 'Pylon.Managed', 'Pylon.Mod.Abstractions', 'Pylon.NaviteAPI')) {
        $from = Join-Path $root "src\csharp\managed\$name\bin\$Configuration\net10.0\$name.dll"
        if (-not (Test-Path $from)) { Warn "跳过 $name.dll（没有产物）"; continue }
        Invoke-Adb push $from "/data/local/tmp/$name.dll" | Out-Null
        Invoke-Adb shell "run-as $pkg cp /data/local/tmp/$name.dll files/pylon-managed/$name.dll"
        Invoke-Adb shell "rm -f /data/local/tmp/$name.dll" | Out-Null
        Say ("{0}.dll ({1} KB)" -f $name, [int]((Get-Item $from).Length / 1KB))

        # 同目录的 .pdb 一起推：有 PDB 时托管栈才带文件名与行号。
        $pdb = [IO.Path]::ChangeExtension($from, '.pdb')
        if (Test-Path $pdb) {
            Invoke-Adb push $pdb "/data/local/tmp/$name.pdb" | Out-Null
            Invoke-Adb shell "run-as $pkg cp /data/local/tmp/$name.pdb files/pylon-managed/$name.pdb"
            Invoke-Adb shell "rm -f /data/local/tmp/$name.pdb" | Out-Null
            Say ("{0}.pdb ({1} KB)" -f $name, [int]((Get-Item $pdb).Length / 1KB))
        }
    }
}

function Command-PushSamples {
    if (-not $Samples) { return }
    Step "推示例 Mod：$($Samples -join ', ')"
    $modsDir = "/storage/emulated/0/Android/data/$pkg/Mods"
    foreach ($name in $Samples) {
        $proj = "src\csharp\samples\$name\$name.csproj"
        if (-not (Test-Path (Join-Path $root $proj))) { Warn "跳过 $name（没有 $proj）"; continue }
        if (-not $NoBuild) { Invoke-Dotnet $proj @('--no-incremental') }
        $dll = Get-ChildItem (Join-Path $root "src\csharp\samples\$name\bin\$Configuration") -Recurse -Filter "$name.dll" | Select-Object -First 1
        if (-not $dll) { Warn "找不到 $name.dll"; continue }
        Invoke-Adb shell "mkdir -p $modsDir/$name" | Out-Null
        Invoke-Adb push $dll.FullName "$modsDir/$name/$name.dll" | Select-Object -Last 1 | Out-Null
        $modPdb = [IO.Path]::ChangeExtension($dll.FullName, '.pdb')
        if (Test-Path $modPdb) { Invoke-Adb push $modPdb "$modsDir/$name/$name.pdb" | Out-Null }
        foreach ($dep in Get-ChildItem (Join-Path $root "src\csharp\samples\$name\lib") -Filter *.dll -ErrorAction SilentlyContinue) {
            Invoke-Adb push $dep.FullName "$modsDir/$name/lib/$($dep.Name)" | Out-Null
        }
        $assets = Join-Path $root "src\csharp\samples\$name\assets"
        if (Test-Path $assets) { Invoke-Adb push $assets "$modsDir/$name" | Out-Null }
        Say "$name.dll -> $modsDir/$name"
    }
}

function Command-Install {
    if (-not (Test-Path $apkPath)) { Die "APK 不存在：$apkPath（先跑 build）" }
    Step "安装 $(Split-Path $apkPath -Leaf) 到 $Serial"
    Invoke-Adb install -r -d $apkPath | Select-Object -Last 1
    if ($LASTEXITCODE -ne 0) { Die '安装失败（设备未连接 / 签名冲突）' }
}

function Start-Game { Invoke-Adb shell am start -n "$pkg/.MainActivity" -a "$pkg.action.IMPORT" --ez launch true --ez coreclr true | Out-Null }

function Command-Run {
    if (-not $adb) { Die '找不到 adb（装 platform-tools，或设 PYLON_ADB）' }
    if ($Apk) { Command-Install }

    Invoke-Adb shell am force-stop $pkg | Out-Null
    Start-Sleep -Milliseconds 800
    Invoke-Adb logcat -c | Out-Null

    $logFile = Join-Path $logDir ((Get-Date -Format 'MMdd-HHmmss') + '.txt')
    # 用 Start-Process 重定向：让 adb 直接写文件，避免 pwsh 用自己的编码把日志转成乱码
    $proc = Start-Process -FilePath $adb -ArgumentList @('-s', $Serial, 'logcat', '-v', 'time') -RedirectStandardOutput $logFile -NoNewWindow -PassThru
    Start-Sleep -Milliseconds 1500
    Step "启动 $pkg（等 /$WaitFor/，上限 $Seconds s）"
    Start-Game

    $sw = [Diagnostics.Stopwatch]::StartNew()
    $hit = $null
    while ($sw.Elapsed.TotalSeconds -lt $Seconds) {
        Start-Sleep -Milliseconds 700
        $match = [regex]::Match((Get-LogText $logFile), $WaitFor)
        if ($match.Success) { $hit = $match.Value; break }
    }
    Start-Sleep -Milliseconds 400
    if ($proc -and -not $proc.HasExited) { $proc.Kill() }

    $verdict = if ($hit) { "命中：$hit" } else { "未命中（超时）" }
    Write-Host ("   {0}，耗时 {1:N1}s" -f $verdict, $sw.Elapsed.TotalSeconds) -ForegroundColor $(if ($hit) { 'Green' } else { 'Yellow' })
    Show-Log $logFile
}

function Show-Log($file) {
    if (-not (Test-Path $file)) { return }
    Step "日志 $file"
    (Get-LogText $file) -split "`r?`n" |
        Where-Object { $_ -match 'PylonLoader\s*\(' -and $_ -match '\[\w+\]|FATAL|✗|失败' } |
        Select-Object -Last 40
}

function Command-Check {
    $missing = 0
    function Item($label, $ok, $hint) {
        if ($ok) { Write-Host "   [ok] $label" -ForegroundColor Green; return }
        $script:missing++
        Write-Host "   [缺] $label" -ForegroundColor Yellow
        if ($hint) { Write-Host "        → $hint" -ForegroundColor DarkGray }
    }

    Step '工具链'
    $dotnetOk = (Get-Command dotnet -ErrorAction SilentlyContinue) -and ("$(& dotnet --version 2>$null)" -like '10.*')
    Item 'dotnet SDK 10' $dotnetOk 'https://dotnet.microsoft.com/download'
    Item 'JDK 17（JAVA_HOME / Android Studio jbr）' (Test-Path $env:JAVA_HOME) '设置 JAVA_HOME'
    $sdk = $null
    if (Test-Path (Join-Path $root 'local.properties')) {
        $sdk = ((Get-Content (Join-Path $root 'local.properties') | Where-Object { $_ -match '^\s*sdk\.dir=' }) -replace '^\s*sdk\.dir=', '').Trim()
        if ($sdk) { $sdk = $sdk -replace '\\:', ':' -replace '\\\\', '\' }
    }
    Item 'Android SDK（local.properties 的 sdk.dir）' ($sdk -and (Test-Path $sdk)) '在 local.properties 写 sdk.dir='
    if ($sdk -and (Test-Path $sdk)) {
        Item 'Android NDK（内核用 clang++ 直编）' (Test-Path (Join-Path $sdk 'ndk')) 'SDK Manager 装 NDK'
    }
    Item 'adb' ([bool]$adb) '装 platform-tools 或设 PYLON_ADB'

    Step '源码依赖'
    Item 'external/BepInEx-Android（子模块）' (Test-Path 'external\BepInEx-Android\.git') 'git submodule update --init --recursive'
    Item 'src/csharp/il2cppinterop（随仓库提供）' (Test-Path 'src\csharp\il2cppinterop\Il2CppInterop.CLI\Il2CppInterop.CLI.csproj') ''

    Step '游戏 dump（refs/，自己的正版 APK）'
    Item 'refs/lib/arm64-v8a/libil2cpp.so' (Test-Path 'refs\lib\arm64-v8a\libil2cpp.so') 'adb shell pm path $pkg + unzip'
    Item 'refs/assets/bin/Data/Managed/Metadata/global-metadata.dat' (Test-Path 'refs\assets\bin\Data\Managed\Metadata\global-metadata.dat') '同上，须与 libil2cpp.so 同一次构建'
    Item 'refs/dump.cs' (Test-Path 'refs\dump.cs') 'Il2CppDumper 生成'

    Step '运行时（runtime/）'
    Item "$runtimeRoot/lib" (Test-Path "$runtimeRoot\lib") 'pylon.ps1 runtime -Apk <BepInEx-Android APK>'
    Item "$runtimeRoot/dotnet + bepinex" ((Test-Path "$runtimeRoot\dotnet") -and (Test-Path "$runtimeRoot\bepinex")) '同上'

    Step '派生件（脚本生成，不进 git）'
    Item "$interopGen/Assembly-CSharp.dll" (Test-Path "$interopGen\Assembly-CSharp.dll") 'pylon.ps1 interop -DummyDir <DummyDll>'
    Item "$jniMain/libpylon.so（内核）" (Test-Path "$jniMain\libpylon.so") 'pylon.ps1 build'
    Item "$jniDebug/libcoreclr.so" (Test-Path "$jniDebug\libcoreclr.so") 'pylon.ps1 build'
    Item "$assetManaged/Pylon.Managed.dll" (Test-Path "$assetManaged\Pylon.Managed.dll") 'pylon.ps1 build'

    Write-Host ''
    if ($missing -eq 0) { Write-Host '全部就绪。' -ForegroundColor Green }
    else { Write-Host "还缺 $missing 项，按上面 → 逐条补齐。" -ForegroundColor Yellow; exit 1 }
}

function Command-Runtime {
    if (-not $BepInExApk) { Die '需要 -Apk <BepInEx-Android 的 release APK>' }
    Need $BepInExApk '检查路径'
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    if ($Sha256File -and (Test-Path $Sha256File)) {
        $expected = ((Get-Content $Sha256File -Raw).Trim() -split '\s+')[0].ToLower()
        $actual = (Get-FileHash $BepInExApk -Algorithm SHA256).Hash.ToLower()
        if ($expected -ne $actual) { Die "sha256 不符：期望 $expected，实际 $actual" }
    }

    Step "拆包 $(Split-Path $BepInExApk -Leaf)"
    $libOut = Join-Path $root "$runtimeRoot\lib"
    $assetOut = Join-Path $root "$runtimeRoot\assets"
    New-Item -ItemType Directory -Force -Path $libOut, $assetOut | Out-Null
    $zip = [System.IO.Compression.ZipFile]::OpenRead($BepInExApk)
    try {
        foreach ($entry in $zip.Entries | Where-Object { $_.FullName -like 'lib/arm64-v8a/*.so' }) {
            $name = [System.IO.Path]::GetFileName($entry.FullName)
            if ($name -in @('libdobby.so', 'libpine.so', 'libfusion.so')) { continue }
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $libOut $name), $true)
        }
        foreach ($entry in $zip.Entries | Where-Object { $_.FullName -like 'assets/*.zip' }) {
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $assetOut ([System.IO.Path]::GetFileName($entry.FullName))), $true)
        }
    } finally { $zip.Dispose() }
    Say ("native {0} 个，assets 压缩包 {1} 个" -f (Get-ChildItem $libOut -Filter *.so).Count, (Get-ChildItem $assetOut -Filter *.zip).Count)

    Step '摊开 dotnet.zip / BepInEx.Android.zip'
    foreach ($pair in @(@('dotnet.zip', 'dotnet'), @('BepInEx.Android.zip', 'bepinex'))) {
        $src = Join-Path $assetOut $pair[0]
        $dst = Join-Path $root "$runtimeRoot\$($pair[1])"
        Need $src '先确认上一步解出了这两个 zip'
        Remove-Item $dst -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path $dst | Out-Null
        [System.IO.Compression.ZipFile]::ExtractToDirectory($src, $dst)
        Say ("{0} -> {1}（{2} 个文件）" -f $pair[0], $pair[1], (Get-ChildItem $dst -Recurse -File).Count)
    }
}

function Command-Interop {
    Step '生成 Il2CppInterop 代理程序集'
    if (-not $DummyDir) {
        if ((Test-Path "$interopGen\Assembly-CSharp.dll") -and (Test-Path "$interopGen\Il2Cppmscorlib.dll") -and -not $Force) {
            Say 'interop/generated 已就绪，跳过'
            return
        }
        Die '需要 -DummyDir <Il2CppDumper 的 DummyDll 目录>'
    }
    Need $DummyDir '检查路径'

    $cliDll = "src\csharp\il2cppinterop\bin\Il2CppInterop.CLI\net10.0\Il2CppInterop.CLI.dll"
    if (-not (Test-Path $cliDll)) {
        Say '编译生成器'
        Invoke-Dotnet 'src\csharp\il2cppinterop\Il2CppInterop.CLI\Il2CppInterop.CLI.csproj' @()
    }
    $gameAsm = 'refs\lib\arm64-v8a\libil2cpp.so'
    $args = @($cliDll, 'generate', '--input', (Resolve-Path $DummyDir), '--output', (Join-Path $root $interopGen))
    if (Test-Path $gameAsm) { $args += @('--game-assembly', (Resolve-Path $gameAsm)) }
    & dotnet @args 2>&1 | Select-Object -Last 4
    if ($LASTEXITCODE -ne 0) { Die '生成失败' }
    foreach ($need in @('Assembly-CSharp.dll', 'Il2Cppmscorlib.dll', 'MethodAddressToToken.db')) {
        Need (Join-Path $interopGen $need) '生成器输出不完整'
    }

    Say '写 On.* 钩子'
    & dotnet $cliDll hookgen --input $interopGen 2>&1 | Select-Object -Last 2
    if ($LASTEXITCODE -ne 0) { Die 'hookgen 失败' }
}

function Command-Deploy {
    Need "$runtimeRoot\lib" 'pylon.ps1 runtime -Apk <APK>'
    Need "$runtimeRoot\dotnet" 'pylon.ps1 runtime -Apk <APK>'

    Step '运行时 native → debug jniLibs'
    $libs = @('libcoreclr.so', 'libclrjit.so', 'libmscordaccore.so', 'libmscordbi.so',
              'libSystem.Native.so', 'libSystem.Globalization.Native.so',
              'libSystem.IO.Compression.Native.so', 'libSystem.Security.Cryptography.Native.Android.so')
    New-Item -ItemType Directory -Force -Path $jniDebug | Out-Null
    foreach ($name in $libs) {
        Need "$runtimeRoot\lib\$name" '运行时 native 不全'
        Copy-Item "$runtimeRoot\lib\$name" (Join-Path $jniDebug $name) -Force
    }
    foreach ($stale in @('libfusion.so', 'libpine.so')) {
        Remove-Item (Join-Path $jniDebug $stale) -Force -ErrorAction SilentlyContinue
    }
    Say "$($libs.Count) 个 .so"

    Step 'BCL → debug assets/pylon-dotnet'
    Remove-Item $assetDotnet -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $assetDotnet | Out-Null
    Copy-Item (Join-Path $root "$runtimeRoot\dotnet\*") $assetDotnet -Recurse -Force
    $bcl = Get-ChildItem $assetDotnet -Recurse -File
    Say ("{0} 个文件 / {1:N1} MB" -f $bcl.Count, (($bcl | Measure-Object Length -Sum).Sum / 1MB))

    Step '托管件 → debug assets/pylon-managed'
    Build-Managed
    Remove-Item $assetManaged -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $assetManaged | Out-Null
    foreach ($name in @('Pylon.Managed', 'Pylon.Mod.Abstractions', 'Pylon.ModLoader', 'Pylon.NaviteAPI')) {
        $dll = "src\csharp\managed\$name\bin\$Configuration\net10.0\$name.dll"
        if (Test-Path $dll) { Copy-Item $dll $assetManaged -Force } else { Warn "缺 $name.dll" }
    }
    Say ("{0} 个 dll" -f (Get-ChildItem $assetManaged -Filter *.dll).Count)

    Step '代理程序集 → debug assets/pylon-interop'
    Remove-Item $assetInterop -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $assetInterop | Out-Null
    if (Test-Path $interopGen) {
        Copy-Item (Join-Path $interopGen '*.dll') $assetInterop -Force
    } else { Warn '缺 interop/generated（pylon.ps1 interop）' }
    foreach ($name in @('Il2CppInterop.Runtime', 'Il2CppInterop.Common', 'Il2CppInterop.RuntimeDetour')) {
        $dll = Get-ChildItem (Join-Path $root "src\csharp\il2cppinterop\bin\$name") -Recurse -Filter "$name.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($dll) { Copy-Item $dll.FullName (Join-Path $assetInterop "$name.dll") -Force } else { Warn "缺自编 $name.dll" }
    }
    foreach ($dep in Get-ChildItem (Join-Path $root "$runtimeRoot\bepinex") -Recurse -Filter 'Microsoft.Extensions.*.dll' -ErrorAction SilentlyContinue) {
        Copy-Item $dep.FullName $assetInterop -Force
    }
    foreach ($stale in @('0Harmony.dll', 'Il2CppInterop.HarmonySupport.dll', 'MonoMod.Core.dll', 'MonoMod.Utils.dll',
                         'MonoMod.Backports.dll', 'MonoMod.RuntimeDetour.dll', 'MonoMod.ILHelpers.dll', 'MonoMod.Iced.dll',
                         'Iced.dll', 'BepInEx.Unity.IL2CPP.dll', 'BepInEx.Core.dll', 'BepInEx.Preloader.Core.dll',
                         'BepInEx.Unity.Common.dll')) {
        Remove-Item (Join-Path $assetInterop $stale) -Force -ErrorAction SilentlyContinue
    }
    foreach ($legacy in @('src\app\src\debug\assets\pylon-samples', 'src\app\src\debug\assets\pylon-sample-mod')) {
        if (Test-Path $legacy) { Remove-Item $legacy -Recurse -Force }
    }
    Say ("{0} 个 dll" -f (Get-ChildItem $assetInterop -Filter *.dll).Count)
}

function Command-Build {
    Step '内核 libpylon.so'
    $so = "$jniMain\libpylon.so"
    $newest = Get-ChildItem 'src\core\src', 'src\core\include' -Recurse -File |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ((Test-Path $so) -and -not $Force -and (-not $newest -or $newest.LastWriteTime -le (Get-Item $so).LastWriteTime)) {
        Say '比源码新，跳过'
    } else {
        & pwsh -NoProfile -File 'src\core\build.ps1' | Select-Object -Last 4
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $so)) { Die 'src/core/build.ps1 失败' }
    }

    Command-Deploy

    Step '打包 APK'
    & (Join-Path $root 'gradlew.bat') --offline -q :app:assembleDebug --project-dir $root 2>&1 |
        Select-String -Pattern 'FAILED|error:' | Select-Object -Last 4
    if (-not (Test-Path $apkPath)) { Die 'APK 未生成' }
    $apk = Get-Item $apkPath
    Say ("{0:N1} MB  {1}" -f ($apk.Length / 1MB), $apk.LastWriteTime)
}

function Command-All {
    Step '子模块'
    if (-not (Test-Path 'external\BepInEx-Android\.git')) {
        & git submodule update --init --recursive 2>&1 | Select-Object -Last 2
        if (-not (Test-Path 'external\BepInEx-Android\.git')) { Die '子模块仍缺失' }
    }
    if (-not (Test-Path "$runtimeRoot\dotnet") -or $Force) { Command-Runtime }
    if ($DummyDir) { Command-Interop }
    Command-Build
    Command-Install
    Command-Run
}

switch ($Command.ToLower()) {
    'check'   { Command-Check }
    'runtime' { Command-Runtime }
    'interop' { Command-Interop }
    'deploy'  { Command-Deploy }
    'build'   { Command-Build }
    'install' { Command-Install }
    'run'     { Command-Run }
    'push'    { if ($Managed) { Command-Push } else { Command-PushSamples } }
    'managed' { Command-Push }
    'log'     { Show-Log (Get-ChildItem $logDir -Filter *.txt | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName }
    'all'     { Command-All }
    default   { Get-Content $PSCommandPath | Select-Object -First 10; exit 1 }
}
