# Il2CppInterop（仓库内副本）

本目录是 [BepInEx/Il2CppInterop](https://github.com/BepInEx/Il2CppInterop) 的源码副本，用来在本地生成游戏程序集的代理与 `On.*` 钩子，并随运行时一起编译。

- 来源、提交、补丁与许可：[VENDOR.md](VENDOR.md)
- 离线编译所需的本地 NuGet 源：[local-feed](local-feed/README.md)
- 生成代理的完整流程：[构建](../../../docs/build.md) 第 2.3 节

## 我们改了什么

只保留生成与运行所需的部分，并为 ARM64 / Android 打了上游补丁（`il2cppinterop-arm64-disarm.patch`）。除补丁外不修改上游逻辑；升级上游时按 VENDOR.md 的流程整份替换。

## 常用命令

```powershell
dotnet build src/csharp/il2cppinterop/Il2CppInterop.CLI/Il2CppInterop.CLI.csproj -c Release -m:1
dotnet src/csharp/il2cppinterop/bin/Il2CppInterop.CLI/net10.0/Il2CppInterop.CLI.dll generate --input <DummyDll> --output interop/generated
```

日常不需要手敲这些命令：`pwsh -File src/csharp/tools/pylon.ps1 interop -DummyDir <DummyDll>` 已经封装了编译、生成与 hookgen。
