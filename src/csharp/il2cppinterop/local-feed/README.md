# local-feed

离线构建用的本地 NuGet 源。

## Disarm.2022.1.0-master.99.nupkg

Il2CppInterop 的 ARM64 xref 扫描依赖 `Disarm` 包（BepInEx 的 ARM64 反汇编器）。
本机无法访问 nuget.org 时，这个包由 `runtime/bepinex-android/bepinex/core/Disarm.dll`
（BepInEx-Android 构建实际链接的那一份，随 `tools/deploy-runtime.ps1` 一起部署）重新打包而成，
只用于编译期引用，运行时不额外分发（真正的运行时副本来自 BepInEx 运行时目录）。

有网络时可删除本目录并恢复 nuget.org 上的官方包；版本号必须与上游锁定值一致
（见 `external/BepInEx-Android/provenance/il2cppinterop.env`）。
