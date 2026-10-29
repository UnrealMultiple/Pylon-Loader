# Il2CppInterop（仓库内副本）

本目录是 **BepInEx/Il2CppInterop** 的源码副本，不再作为 git 子模块使用。

## 来源

| 项 | 值 |
|---|---|
| 上游 | https://github.com/BepInEx/Il2CppInterop |
| 提交 | `81a6f78c8b653e0da4a3420ac4cd00819e8b6292`（与 BepInEx-Android 的 `provenance/il2cppinterop.env` 一致） |
| 补丁 | `external/BepInEx-Android/references/il2cppinterop-arm64-disarm.patch`（已应用） |
| 许可 | LGPL-3.0-only（见本目录 `LICENSE`） |

补丁内容（ARM64 / Android 必需，官方源码直接编译不可用）：

- `Common/XrefScans`：x86 的 Iced 扫描改为 ARM64 的 Disarm 扫描；
- `InjectorHelpers`：模块匹配 `libil2cpp.so`、`NativeLibrary.Load("libil2cpp.so")`；
- `IDetourProvider.Create(..., specialReturnBuffer)` 三参签名；
- `Hook.ApplyHook` 找不到目标时告警而不抛；
- 所有 `XrefScannerLowLevel` 调用点改为三参 `JumpTargets`；
- Runtime / HarmonySupport retarget 到 `net10.0`。

## 本仓库在上游之外做的改动

1. **移除 HarmonySupport 工程**：hook 只有 `Il2CppInterop.Runtime.Hooking` 一套。
2. **Hook 引擎 = `Il2CppInterop.RuntimeDetour` 工程**：
   - `Hook`：MonoMod 形状的自由 hook（任意 `MethodBase` 或原生地址）；
   - `DetourBackend`：原生 detour 抽象类，宿主（Pylon）实现它接内核 Dobby；
   - `HookEndpoint`：生成的 `On.*` 事件的运行时端点；
   - `HookPlan/HookStub/MirrorTypes`：签名映射，含 `ref/out`、真值类型按值传、
     **装箱值类型（`Il2CppSystem.Nullable<T>` 这类 class 代理但原生按值传）的镜像 blob + `il2cpp_value_box/unbox`**。
   - `Il2CppInterop.RuntimeDetour.HookGen`：用 Cecil 把 `On.*` 事件直接发 IL 写进代理程序集
     （不生成 C# 源码、不经过 Roslyn；`On.__PylonHookGen` 标记保证可重复执行）。
3. **TFM 统一为 `net10.0`**（Common / Generator / StructGenerator / CLI），因为本机只有 .NET 10 SDK，
   且 `Il2CppInterop.Common` 要引用随运行时提供的 `Disarm.dll`（net6 元数据，netstandard2.0 工程会 CS1705）。
4. **离线 NuGet**：`src/csharp/il2cppinterop/local-feed/Disarm.2022.1.0-master.99.nupkg` 由
   `runtime/bepinex-android/bepinex/core/Disarm.dll` 重建（见该目录 README）。
5. 删除 `global.json`（上游把 SDK 钉在 8.x；本仓库统一用根目录的 .NET 10 SDK）。
6. **生成物 corlib 升级到 .NET 10**：`Generator/Utils/CorlibReferences` 的 `TargetCorlib` 从
   `KnownCorLibs.SystemRuntime_v6_0_0_0` 改为 `SystemRuntime_v10_0_0_0`（TargetFrameworkAttribute 同步为
   ".NET 10.0"），与仓库 net10.0 的 Runtime/Common/HookGen 一致，避免生成物带 System.Runtime 6.x
   的陈旧引用。


## 构建

```powershell
dotnet build src/csharp/il2cppinterop/Il2CppInterop.Runtime/Il2CppInterop.Runtime.csproj -c Release
dotnet build src/csharp/il2cppinterop/Il2CppInterop.CLI/Il2CppInterop.CLI.csproj -c Release
```

沙箱/多进程环境下 MSBuild 会静默失败（命名管道不可用），加：
`-m:1 -nodeReuse:false -p:MSBUILDNOINPROCNODE=1 -p:UseSharedCompilation=false`。