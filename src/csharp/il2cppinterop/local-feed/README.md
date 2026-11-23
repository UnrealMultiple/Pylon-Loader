# local-feed

离线构建用的本地 NuGet 源。

## Disarm.2022.1.0-master.99.nupkg

Il2CppInterop 的 ARM64 xref 扫描依赖 `Disarm` 包（BepInEx 的 ARM64 反汇编器）。
本机无法访问 nuget.org 时，这个包由 BepInEx.Android.zip（旧 BepInEx-Android 构建产物）
解包出的 `Disarm.dll` 重新打包而成，只用于编译期引用，运行时不随 APK 分发。

有网络时可删除本目录并恢复 nuget.org 上的官方包；版本号必须与上游锁定值一致
（Disarm 2022.1.0-master.99，见本仓库的 `src/csharp/il2cppinterop/VENDOR.md` 记录的补丁出处）。
