# Pylon Loader

**非官方的 Terraria 移动版 Mod 加载器**（Android / arm64-v8a，for Terraria Mobile）。

Mod 是普通的 .NET 10 C# 类库，运行时被加载进游戏进程：内容 API 参照 tModLoader 的使用习惯，游戏类型由 Il2CppInterop 映射成强类型代理，改原版行为直接订阅 `On.*` 事件。

> 与 Re-Logic、505 Games、tModLoader 均无关联，也未获其背书。游戏文件由用户自己的正版提供，本仓库与发布产物都不含游戏二进制。

## 特性

- **强类型**：`Terraria.Main`、`Item`、`Projectile`、`NPC`、`Player` 都是编译期可检查的代理，字段直接读写，方法直接调用。
- **tModLoader 式路径**：`ModItem` / `ModProjectile` / `ModNPC` / `ModBuff` / `ModConfig` / `Global*`，内容 ID 由框架分配，贴图、配方、配置统一注册。
- **只有一条钩子链**：`Il2CppInterop.RuntimeDetour` + 内核 Dobby，不需要 Harmony 或第二套 detour。
- **进程内直调**：没有 IPC、没有脚本层；框架先判"有没有人覆写"再分派，为空钩子不付成本。
- **不改游戏 APK、不需要 root**：宿主把自己的进程变成游戏进程，全程只读用户已安装的那份游戏。
- **UI 不依赖悬浮窗权限**：配置屏挂在游戏菜单里，自定义界面用 ImGui 覆盖层，画在游戏自己的 GLES 上下文。
- **失败隔离**：内核引用校验失败只停用 Mod；单个 Mod 出错不影响其余 Mod，游戏照常运行。

## 架构

```text
:app 启动器 (Kotlin)      选择游戏 → 导入运行时并校验 → 拉起宿主
  └─ :game 宿主 (Kotlin)  注入游戏 dex → 装载游戏 .so → 挂载 assets → UnityPlayer
       └─ libpylon.so     绑定 il2cpp_* → 等待引擎稳定 → 校验引用 → Dobby → CoreCLR → ImGui
            └─ 托管层       Pylon.Managed → Pylon.ModLoader → 你的 Mod
```

四层职责与启动时序见 [架构](docs/architecture.md)。

## 快速开始

前置：Windows + PowerShell 7、.NET SDK 10、JDK 17、Android SDK 与 NDK，以及一台 arm64 设备（可选）。

```powershell
git clone --recurse-submodules https://github.com/Controllerdestiny/Pylon-Loader.git
cd Pylon-Loader
pwsh -File src/csharp/tools/pylon.ps1 check
```

`check` 会列出缺什么、该怎么补。补齐三类不进仓库的文件（游戏 dump、运行时、代理程序集）后：

```powershell
pwsh -File src/csharp/tools/pylon.ps1 all -BepInExApk D:\dl\app-release-aligned.apk -DummyDir refs\DummyDll
```

日常改动的两条捷径：

```powershell
pwsh -File src/csharp/tools/pylon.ps1 build      # 内核/宿主/资源有变动时重打 APK
pwsh -File src/csharp/tools/pylon.ps1 managed    # 只改托管件：编 + 推进设备，不重装
```

完整的构建说明、命令参考与 CI artifact 见 [构建](docs/build.md)。

## 写第一个 Mod

```csharp
public sealed class MyMod : Pylon.ModLoader.Mod
{
    public override void Load() => Logger.Info("hello");

    public override void SetupContent()
    {
        On.Terraria.Main.Update += (orig, self, time) => orig(self, time);
    }
}
```

把编译产物放进 `/storage/emulated/0/Android/data/dev.pylon.loader/Mods/<id>/`（或直接在启动器的"Mod 管理"页导入 `.dll` / `.zip` / `.pmod`），重启游戏生效。

API、生命周期、内容类型、打包与调试回路见 [Mod 开发](docs/modding.md)；完整示例在 [src/csharp/samples](src/csharp/samples)。

## 现状

| 项 | 状态 |
|---|---|
| 宿主链路 / 运行时导入 / 内核 / 托管层 | 已跑通 |
| 内容管线（物品、弹幕、NPC、Buff、配方、方块、玩家与世界、网络、音乐、配置、UI） | 已接入，逐项见 [路线](docs/roadmap.md) |
| warm 启动 | 宿主 `onCreate` +26ms，CoreCLR +142ms，引用校验 +219ms |
| 未完成 | release 变体与签名、Tile 自定义绘制、地图颜色、战斗命中管线 |
| 不支持 | Google Play 的 PairIP 构建、中文版、32 位、root 相关注入方案 |

## 目录

```text
src/app/     启动器（外部 UI）：导入运行时、Mod 管理、拉起 :game
src/game/    宿主（:game）：dex 注入、运行时装载、UnityPlayer、崩溃日志
src/common/  契约：运行时目录布局、清单格式、哈希
src/core/    C++ 内核 libpylon.so（boot / il2cpp / hook / dotnet / mod / graphics / imgui / jni）
src/csharp/  托管层与示例：Pylon.Managed / Pylon.ModLoader / Pylon.Mod.Abstractions / Pylon.NaviteAPI
interop/     Il2CppInterop 代理程序集（由你的游戏文件生成，不进 git）
external/    外部依赖（git 子模块，只有源码）
refs/        本地游戏 dump（不进 git）
runtime/     BepInEx-Android 运行时（不进 git）
docs/        文档
```

## 文档

| 文档 | 内容 |
|---|---|
| [架构](docs/architecture.md) | 分层、启动时序、内核模块、线程模型、图形管线 |
| [构建](docs/build.md) | 前置、三类派生文件、`pylon.ps1` 参考、CI artifact、缺件对照 |
| [运行时导入](docs/runtime-import.md) | 目录布局、清单格式、版本门禁、PairIP 拒绝 |
| [Mod 开发](docs/modding.md) | 工程配置、生命周期、内容类型、钩子、打包、调试 |
| [路线](docs/roadmap.md) | 已支持范围、未实现项、已知限制、优先级 |
| [排障](docs/troubleshooting.md) | 日志层次、崩溃定位、常见症状 |
| [贡献](CONTRIBUTING.md) | 开发回路、代码与文档规范、提交约定 |
| [内核](src/core/README.md) | 内核目录、编译契约、硬约束（改内核前必读） |

## 边界

- **不提供游戏文件**：仓库、Release 与 CI artifact 都不含游戏二进制或由其派生的代理程序集。
- **必须使用自己的正版**：导入与生成以设备上已安装的 `1.4.5` 系列为准。
- **不支持 PairIP 构建**：`libunity.so` 依赖 `libpairipcore.so` 的 Google Play 版会在导入阶段被拒绝，请用无校验构建。
- **不绕过任何校验**，也不分发被解密的资源。
- **不复制上游代码**：[Bunny Loader](https://github.com/JonataOliveiraa/Bunny-Loader) 采用 PolyForm Noncommercial，与本项目不兼容，只借鉴技术路线与实测结论。
- **名称与商标**：产品名不含 "Terraria"，只在描述里写 "for Terraria Mobile"；不使用游戏美术。

## 许可

[Pylon Loader 非商业许可 —— GNU GPL v3 修改版](LICENSE)：官方 GPL-3.0 全文 + 附加条款。

- **禁止商用**：不得将本项目代码或其衍生作品用于任何商业目的，除非获得版权所有者书面许可。
- **使用即开源**：任何使用、包含或修改本项目代码的项目，无论以何种形式发布，都必须开源并保留本许可条款与署名。

附加条款使本许可属于 GPL-3.0 的修改版，与标准 GPL/MIT/Apache 代码不自动兼容；分发时请随附完整 [LICENSE](LICENSE)。
