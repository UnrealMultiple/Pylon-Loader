# ms-extensions

deploy 时拷进 `debug/assets/pylon-interop` 的 BepInEx 栈成员——也是 BepInEx 运行时
栈里**唯一**会被 Pylon APK 实际装载的部分（`0Harmony` / `BepInEx.Core` / `MonoMod.*` 等
在 `pylon.ps1 deploy` 里被显式剔除，运行时不加载）。

| 文件 | 程序集版本 |
|---|---|
| Microsoft.Extensions.DependencyInjection.Abstractions.dll | 6.0.0.0 |
| Microsoft.Extensions.DependencyInjection.dll | 6.0.0.0 |
| Microsoft.Extensions.Logging.Abstractions.dll | 6.0.0.0 |
| Microsoft.Extensions.Logging.dll | 6.0.0.0 |
| Microsoft.Extensions.Options.dll | 6.0.0.0 |
| Microsoft.Extensions.Primitives.dll | 6.0.0.0 |

## 出处

从 BepInEx.Android.zip（旧 BepInEx-Android 子模块 @ 625d8b1 时代的构建产物）
解包出的 `bepinex/core/Microsoft.Extensions.*.dll` 原样拷贝，字节不变。

这 6 个 DLL 本身是微软官方 NuGet 包（6.0.x）的产物，并非"修补件"；提交固定字节的
原因是：版本与现网设备已验证的组合严格一致，且 deploy 不需要网络。若要更换版本，
应先确认 `Il2CppInterop.Common` 的 `Microsoft.Extensions.Logging.Abstractions` 引用
（见 `src/csharp/il2cppinterop/Il2CppInterop.Common/Il2CppInterop.Common.csproj`）仍然兼容，
再从对应版本的 BepInEx.Android.zip 里解出同名 DLL 替换本目录。

## 消费方

`src/csharp/tools/pylon.ps1` 的 `Command-Deploy`（glob：`vendor/ms-extensions/*.dll`
→ `src/app/src/debug/assets/pylon-interop/`）。
