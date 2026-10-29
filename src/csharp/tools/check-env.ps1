<#
  层：工具 —— 从零检查本机构建前提

  为什么需要它：仓库里**不含**三类二进制（游戏 dump / BepInEx 运行时 / Il2CppInterop 代理），
  因为它们要么有版权（游戏文件及其派生），要么上百 MB（CoreCLR + BCL）。
  新 clone 的仓库直接编会缺件，而且报错点很深（MSBuild 找不到引用 / Gradle 打包缺 assets）。
  这个脚本把"缺什么、上哪找、用哪条命令补"一次说清楚。

  用法：pwsh -File tools/check-env.ps1
#>
[CmdletBinding()]
param([switch]$Quiet)

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$missing = 0

function Check($label, $ok, $hint) {
    if ($ok) {
        if (-not $Quiet) { Write-Host ("  [ok]   {0}" -f $label) -ForegroundColor Green }
    } else {
        $script:missing++
        Write-Host ("  [缺]   {0}" -f $label) -ForegroundColor Yellow
        if ($hint) { Write-Host ("         → {0}" -f $hint) -ForegroundColor DarkGray }
    }
}

function Has($rel) { Test-Path (Join-Path $root $rel) }

Write-Host "`n== 外部依赖（子模块，只含源码）==" -ForegroundColor Cyan
Check "src/csharp/il2cppinterop（仓库内 Il2CppInterop：代理生成器 + ARM64 补丁 + Hook 引擎）" `
    (Has 'src\csharp\il2cppinterop\Il2CppInterop.CLI\Il2CppInterop.CLI.csproj') `
    "源码已随仓库提供，不需要 git submodule"
Check "external/BepInEx-Android（runtime 的构建系统与 provenance 锁定）" (Has 'external\BepInEx-Android\.git') `
    "git submodule update --init --recursive"

Write-Host "`n== 工具链 ==" -ForegroundColor Cyan
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetVersion = if ($dotnet) { (& dotnet --version) 2>$null } else { $null }
Check "dotnet SDK（需要 10.x）" ([bool]$dotnet -and "$dotnetVersion" -like '10.*') `
    "装 .NET SDK 10：https://dotnet.microsoft.com/download"

$javaHome = $env:JAVA_HOME
if (-not $javaHome) { $javaHome = 'D:\Android Studio\jbr'; if (-not (Test-Path $javaHome)) { $javaHome = $null } }
Check "JDK 17（JAVA_HOME 或 Android Studio 自带 jbr）" ([bool]$javaHome -and (Test-Path $javaHome)) `
    "设置 JAVA_HOME，或装 Android Studio（自带 jbr）"

$localProps = Join-Path $root 'local.properties'
$sdk = if (Test-Path $localProps) {
    ((Get-Content $localProps | Where-Object { $_ -match '^\s*sdk\.dir=' }) -replace '^\s*sdk\.dir=', '').Trim()
} else { $null }
if ($sdk) { $sdk = $sdk -replace '\\:', ':' -replace '\\\\', '\' }
Check "Android SDK（local.properties 的 sdk.dir 或 ANDROID_HOME）" ([bool]$sdk -and (Test-Path $sdk)) `
    "在 local.properties 写 sdk.dir=<SDK 路径>（此文件不进 git）"
if ($sdk -and (Test-Path $sdk)) {
    $ndkOk = (Test-Path (Join-Path $sdk 'ndk')) -and ((Get-ChildItem (Join-Path $sdk 'ndk') -Directory -ErrorAction SilentlyContinue).Count -gt 0)
    Check "Android NDK（src/core/build.ps1 用 clang++ 直接编内核）" $ndkOk `
        "SDK Manager 里装 NDK（任意近期版本，脚本会自动找）"
}

Write-Host "`n== ① 游戏 dump：refs/（从**你自己的正版** APK 解，不能分发）==" -ForegroundColor Cyan
Check "refs/lib/arm64-v8a/libil2cpp.so" (Has 'refs\lib\arm64-v8a\libil2cpp.so') `
    "adb shell pm path com.and.games505.Terraria + unzip（完整命令见 refs/README.md）"
Check "refs/assets/bin/Data/Managed/Metadata/global-metadata.dat" (Has 'refs\assets\bin\Data\Managed\Metadata\global-metadata.dat') `
    "同上；版本必须是 1.4.5.8.6，且与 libil2cpp.so 同一次构建"
Check "refs/assets/bin/Data（分析/可选 -PpylonStageRefs=true；默认不再打进 APK）" (Has 'refs\assets\bin\Data') `
    "同上；默认运行由启动器从已安装游戏导入，缺它不影响正式链路，只影响本地回退"
Check "refs/dump.cs（Il2CppDumper 输出，按名解析/查签名用）" (Has 'refs\dump.cs') `
    "用 Il2CppDumper 对 libil2cpp.so + global-metadata.dat 生成"

Write-Host "`n== ② 运行时：runtime/（从 BepInEx-Android 发行包解，上游二进制）==" -ForegroundColor Cyan
Check "runtime/bepinex-android/lib（coreclr/jit/PAL）" (Has 'runtime\bepinex-android\lib') `
    "pwsh -File src/csharp/tools/extract-bepinex-android.ps1 -Apk <BepInEx-Android 发行 APK>"
Check "runtime/bepinex-android/assets/dotnet.zip + BepInEx.Android.zip" `
    ((Has 'runtime\bepinex-android\assets\dotnet.zip') -and (Has 'runtime\bepinex-android\assets\BepInEx.Android.zip')) `
    "同上（extract-bepinex-android.ps1 会把这两个 zip 拉出来）"
Check "runtime/bepinex-android/dotnet + bepinex（摊开后的 BCL 与托管栈）" `
    ((Has 'runtime\bepinex-android\dotnet') -and (Has 'runtime\bepinex-android\bepinex')) `
    "pwsh -File src/csharp/tools/prepare-runtime.ps1"

Write-Host "`n== ③ Il2CppInterop 代理程序集：interop/generated/（游戏代码的派生，不能分发）==" -ForegroundColor Cyan
Check "interop/generated/Assembly-CSharp.dll" (Has 'interop\generated\Assembly-CSharp.dll') `
    "用 Il2CppInterop 对游戏程序集生成（版本必须与 Il2CppInterop.Runtime 1.5.3 对齐，见 docs/构建-从零开始.md）"
Check "interop/generated/Il2Cppmscorlib.dll" (Has 'interop\generated\Il2Cppmscorlib.dll') `
    "同上（代理程序集之间的依赖，生成时一起产出）"

Write-Host "`n== ④ 部署到 debug 变体的件（脚本生成，不进 git）==" -ForegroundColor Cyan
$interop = Join-Path $root 'src\app\src\debug\assets\pylon-interop'
Check "src/csharp/il2cppinterop/Il2CppInterop.Runtime（仓库内已打 ARM64 补丁的 interop + Hook 引擎）" `
    (Test-Path (Join-Path $root 'src\csharp\il2cppinterop\Il2CppInterop.Runtime\Il2CppInterop.Runtime.csproj')) `
    "子模块 external/Il2CppInterop 已不再使用；源码在 src/csharp/il2cppinterop"
Check "src/app/src/debug/assets/pylon-interop/Il2CppInterop.RuntimeDetour.dll（Hook 引擎）" (Test-Path (Join-Path $root 'src\app\src\debug\assets\pylon-interop\Il2CppInterop.RuntimeDetour.dll')) "pwsh -File src/csharp/tools/deploy-runtime.ps1（会构建 src/csharp/il2cppinterop）"
Check "interop/generated/Assembly-CSharp.dll 含 On.* 钩子" (Test-Path (Join-Path $root 'interop\generated\Assembly-CSharp.dll')) "pipeline 的 interop 阶段会跑 hookgen"
Check "src/app/src/debug/assets/pylon-interop/Il2CppInterop.Runtime.dll" (Test-Path (Join-Path $interop 'Il2CppInterop.Runtime.dll')) `
    "同上"
Check "src/app/src/debug/jniLibs/arm64-v8a/libcoreclr.so" (Has 'src\app\src\debug\jniLibs\arm64-v8a\libcoreclr.so') `
    "同上（运行时 native 必须进 APK 的 nativeLibraryDir，否则 dlopen 被 namespace 挡住）"
Check "src/app/src/main/jniLibs/arm64-v8a/libpylon.so（内核）" (Has 'src\app\src\main\jniLibs\arm64-v8a\libpylon.so') `
    "pwsh -File src/core/build.ps1"
Check "On.* 钩子已生成进 interop/generated/Assembly-CSharp.dll" (Test-Path (Join-Path $root 'interop\generated\Assembly-CSharp.dll')) "pipeline 的 interop 阶段会跑 Il2CppInterop.CLI hookgen"

Write-Host "`n== ⑤ 本地签名材料 ==" -ForegroundColor Cyan
Check "debug.keystore（本机调试签名，不进 git）" (Has 'debug.keystore') `
    "keytool -genkeypair -keystore debug.keystore -alias androiddebugkey -storepass android -keypass android -dname CN=Android -keyalg RSA -validity 10000"

Write-Host ""
if ($missing -eq 0) {
    Write-Host "全部就绪。可以：pwsh -File src/csharp/tools/deploy-runtime.ps1 → pwsh -File src/core/build.ps1 → gradlew :app:assembleDebug" -ForegroundColor Green
} else {
    Write-Host ("还缺 {0} 项。按上面 → 后面的命令逐条补齐；完整顺序见 docs/构建-从零开始.md" -f $missing) -ForegroundColor Yellow
    exit 1
}