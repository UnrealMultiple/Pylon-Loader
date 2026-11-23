# MonoMod（随仓库的 Android 修补源码树）

本目录是 **官方 MonoMod/MonoMod** 在 pin 提交上打好 Android 补丁后的**完整源码树**
（与 `src/csharp/il2cppinterop` 同样的收编形态：补丁已应用、可直接编译）。

## 出处

| 项 | 值 |
|---|---|
| 上游 | https://github.com/MonoMod/MonoMod（`reorganize` 分支） |
| 基提交 | `d2f2fd91f0daababe41b81239b3d53a4be77cd43`（官方提交，2026-08-05） |
| 补丁 | Android 支持（原 BepInEx-Android 的 `references/monomod-android.patch`，约 447 行的 `AndroidSystem` + `Interop/Android.cs` + W^X/mprotect 处理；补丁本身未随树保留，树的差分即对上游的全部增量） |
| 等价产物 | 逐字节等于公开 fork XtraCube/MonoMod @ `d41aaf9fd9a552ac5bc16c58701c6ca8c975019d` 的 `src/`（收编时已核对，仅 Windows 检出产生的行尾差异） |
| 包版本 | 25.3.7-android.1（`-android.1` 后缀是有意的：让 NuGet 缓存不可能被官方同名包顶替） |
| 官方不改会怎样 | `PlatformTriple.CreateCurrentSystem` 对 `OSKind.Android` 直接 `throw NotImplementedException`，Harmony 挂钩在设备上全灭 |

## 剪枝说明

`external/iced`（MonoMod.Core 以 ProjectReference 导入的上游子模块，缺它 MSB4019）
只保留了构建需要的 `external/iced/src/csharp/Intel/Iced/`（14MB 生成指令表为主体）
与 `Directory.Build.props` / `Iced.snk` / `logo.png` / `LICENSE.txt`；
iced 的 `UnitTests` / `Generator` / `IcedFuzzer` / `java` / `rust`（约 60MB）与
MonoMod 的 fork 断言脚本被剪掉——它们只服务 iced 自己的开发与再生成，
不在 MonoMod 构建路径上。iced pin：`c50f29b7bc305696895c075f3fc7719751426b12`。

## 消费方与现状

- **Pylon 的内核/托管层当前不引用 MonoMod**：hook 走 `Il2CppInterop.RuntimeDetour`
  （宿主 DetourBackend 接内核 ShadowHook）。此树保留的是 BepInEx 栈侧的
  Harmony 挂钩能力（BepInEx.Unity.IL2CPP → HarmonyX → MonoMod）的源码。
- 重建包（若需要）：`dotnet pack src/MonoMod.Core|Utils|RuntimeDetour` 各工程，
  `-p:Version=25.3.7-android.1`。
- **对上游的一处偏离**：global.json 的 SDK `rollForward` 从 `latestPatch`
  放宽为 `latestFeature`——上游钉死 10.0.100 的补丁段（10.0.1xx），换任何
  没有 10.0.100 精确补丁带的机器都会先于编译死在 SDK 解析上。本树已随
  `PylonLoader.slnx` 一起构建，必须任何 10.x SDK 都能解析，故放宽（原
  BepInEx-Android 管线每次构建也做同样的临时覆盖，此处改为落盘固化）。
  工程在 `src/csharp/PylonLoader.slnx` 的 `/MonoMod/` 文件夹内。

## 同步上游

上游 `reorganize` 分支移动时：在新 pin 上重放 Android 增量（与 fork 的差异可用
`git diff d2f2fd9 d41aaf9 -- src/` 重新生成），整树替换本目录，并更新本文件
的基提交与等价产物记录。
