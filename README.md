# Pylon Loader

**非官方的 Terraria 移动版 Mod 加载器**（for Terraria Mobile，Android / arm64-v8a）。

外部 Kotlin 启动器 + 独立的 `:game` 宿主进程 + C++ 内核 `libpylon.so` + 进程内 CoreCLR。
Mod 是普通 .NET 10 C# 类库，按一套参照 tModLoader 使用习惯的 API 声明内容。
游戏运行时由用户从**自己已安装的正版**提供；仓库与发布产物不含游戏二进制。debug 变体默认也不再预置 `refs/` 里的游戏 `.so` / `assets/bin/Data`，启动时从启动器导入目录与已安装游戏 APK 取。

> 本项目与 Re-Logic、505 Games、tModLoader 均无关联，也未获其背书。
> 请使用自己的正版游戏；本项目不提供、也不代为下载任何游戏文件。

## 现状

- **宿主链路**：启动器选择已安装游戏并导入校验 → `:game` 进程 → 注入游戏 dex、从导入目录加载游戏 `.so`、把所选游戏 APK 的 assets 挂到 AssetManager → `UnityPlayer` → 等 IL2CPP 稳定。已跑通，APK 不含游戏二进制。
- **内核**：`libpylon.so` 绑定 `il2cpp_*`、按名字解析、引用校验、Dobby detour、起 CoreCLR、ImGui 覆盖层。已跑通。
- **Mod 框架**：`Pylon.ModLoader` 提供 `Mod` / `ModItem` / `ModProjectile` / `ModNPC` / `ModBuff` / `ModMount` / `ModPrefix` / `ModRarity` / `ModRecipe` / `ModTile` / `ModWall` / `ModPlayer` / `ModConfig` / `Global*` 等，已接入物品、弹幕、NPC、Buff、坐骑、前缀、稀有度、配方、方块、墙壁、玩家生命周期与侧车存档、世界生命周期与侧车存档、网络包与实体附加数据、城镇 NPC/商店、音乐与音效、配置、工具提示与触屏按钮；世界/每帧时机直接写在 `Mod` 的覆写上（不再有单独的 `ModSystem`）。
- **UI**：游戏原生配置屏（暂停菜单 / 主菜单设置）、ImGui 覆盖层（`Pylon Mod` 窗口，Mod 可注册 Tab）、原生 SpriteBatch 面板；外部启动器是单页深色界面，含已安装游戏选择、运行时状态、导入进度、逐文件校验详情、崩溃日志入口与一键启动；`Mod 管理` 是独立页面，支持导入 `.dll` / `.zip` / `.pmod` / 文件夹、单文件打包、启停与删除，并显示 Mod 自己的图标。
- **屏幕效果**：屏幕效果着色器管线已迁移进内核（与 Bunny Loader 同源：dlsym GLES3 + GLSL ES 3.00，片元着色器可 `#include <bl/screen|math|noise|color|shapes|mask>`，编译延迟到渲染线程首次绘制、报错带 Mod 文件行号）。C# 侧 `Pylon.ModLoader.Effects` 暴露 `ScreenShader`（FromSource/FromFile/Reload）、`ScreenTexture`、`ScreenEffect`、`ScreenFrame`、`ScreenGraphics`（Shader/Texture/Submit/SaveNeutralLut/Stats）；效果可选画在世界层（界面之下）或屏幕层（盖一切），支持离屏目标、History（上一帧世界）、自定义 uniform 与 sampler2D。ImGui 覆盖层保持独立 Vulkan 渲染，两者互不影响（评估见 [`docs/评估-ImGui后端-GLES化.md`](docs/评估-ImGui后端-GLES化.md)）。
- **未接**：Tile 的 `PreDraw/PostDraw/SpecialDraw` 自定义绘制 pass、地图颜色/图例、门/坐睡/锁箱；战斗命中/受伤管线、自然生成、菜单主题等，逐项进度见 [`docs/migration-map.md`](docs/migration-map.md)。

## 优势

**最直接的一条：写 Mod 的体验接近在游戏自己的 C# 代码里加东西。**

Il2CppInterop 把游戏的 `Assembly-CSharp`、`Il2Cppmscorlib` 等程序集映射成强类型代理，
`Terraria.Main`、`Item`、`Projectile`、`NPC`、`Player` 都是强类型的 C# 代理：字段直接读写、
方法直接调用，有 IDE 补全和编译期检查，不需要手写反射字符串。

```csharp
// 直接读写游戏的 Item 字段，和游戏里的写法一样
public override void SetDefaults(Item item)
{
    item.damage = 42;
    item.width = item.height = 32;
    item.shoot = ProjectileID.Fireball;
    item.shootSpeed = 14f;
    SetDefaultWeaponStyle(useTime: 12, autoReuse: true);
}

// 直接调游戏的发射 API（返回 false = 不发射游戏原本那发）
public override bool Shoot(Item item, Player player, Vector2 position, Vector2 velocity,
                           int type, int damage, float knockBack)
{
    Projectile.NewProjectile(player.GetProjectileSource_Item(item), position, velocity,
                             ModContent.ProjectileType<TestFireball>(), damage, knockBack, player.whoAmI);
    return false;
}
```

其余几条：

- **C# + .NET 10**：Mod 就是普通类库，语言能力与工具链都是现成的；不需要为 Mod 学一门专用脚本。
- **tModLoader 式的上手路径**：`ModItem` / `ModProjectile` / `ModNPC` / `ModBuff` / `ModMount` /
  `ModPrefix` / `ModRarity` / `ModRecipe` / `ModConfig` / `Global*`；内容 ID 自动分配，
  贴图、配方、配置由框架统一注册。
- **Harmony 直接打补丁**：需要改原版行为时用 `[HarmonyPatch]` 挂游戏方法，和 C# Mod 生态一致；
  框架自己的内容钩子也走这套。
- **进程内直调，没有中间层**：不是 IPC、不是 JS 解释器；调用直接落到 IL2CPP，热路径开销小，
  框架按“先判覆写再分派”避免为空钩子付出分派成本。
- **不改游戏 APK、不需要 root**：启动器只读你已安装的那份游戏，宿主把自己的进程变成游戏进程；
  没有 Xposed / 虚拟空间 / 重签 APK 这些前置条件。
- **UI 不依赖 Android 悬浮窗权限**：配置屏挂在游戏自己的暂停菜单 / 主菜单里；自定义界面用 ImGui 覆盖层注册 Tab；
  触屏按钮走游戏自己的 Controls 编辑器。不需要 `SYSTEM_ALERT_WINDOW`，也不是 Android View 盖在游戏上。
- **失败隔离**：一个 Mod 加载失败只停用它自己；钩子里的异常按 key 去重记录，不刷屏；
  内核引用校验失败会停用 Mod，而不是让进程随机崩。
- **改完就试**：把 dll 丢进 `Mods/`，重启游戏即可；Mod 作者不需要重新打包 APK。

> 注："像源码"指的是 API 的**类型与调用形态**，不是拿到了游戏的源代码；代理程序集仍然由你本机的
> 游戏文件生成，且受游戏版本约束（见下方边界）。

有两处边界需要先说明：

1. **启动器的边界**：它负责选择已安装游戏、导入运行时、校验、启动与排障，以及 Mod 的导入/启停/删除；不提供 Mod 市场、在线更新或依赖解析。
2. **发行形态（M5）的主链路已跑通，但 release 打包未完成**。debug APK 已能做到不含游戏 `.so` / `assets/bin/Data`，由宿主从导入目录 `System.load`、把目录追加进 classloader 的 native 搜索路径，并用 `AssetManager.addAssetPath` 挂载所选游戏的 `base.apk`。仍未完成的是：release 变体把 CoreCLR/BCL/代理程序集也纳入、正式签名，以及 Android 未来版本对“从可写目录执行 .so”和隐藏 `addAssetPath` 的限制（当前目标设备实测可用）。
   本地没有导入、想退回“APK 内置 refs”的调试方式时，构建加 `-PpylonStageRefs=true`。

## 技术路线

1. **启动器**（`:app`，Kotlin）：从已安装游戏导入运行时并逐文件校验，然后拉起宿主进程。
2. **宿主**（`:game`）：`GameActivity` 是真正的 `Activity`（Unity 的 `instanceof Activity` 与游戏 C# 的 JNI 名字查找都依赖它）；注入游戏的 `classes.dex`，从导入目录加载游戏库（并把目录追加进 classloader 的 native 搜索路径），挂载所选游戏 APK 的 assets，创建 `UnityPlayer` 并对齐生命周期。
3. **内核**（`libpylon.so`，C++20）：等引擎稳定后才碰 IL2CPP；按名字解析类/字段/方法并逐条校验，任一必需引用失败就停用 Mod（游戏照常跑）；Dobby 提供原生 detour，CoreCLR 跑托管层，ImGui 走 Vulkan 覆盖层。
4. **Mod 层**（C#，net10.0）：CoreCLR 由内核启动并调用托管入口；`Pylon.Managed` 装 Harmony、按依赖加载 Mod；`Pylon.ModLoader` 是内容框架；Il2CppInterop 提供游戏类型的托管代理。

## 目录

```text
app/       启动器（外部 UI）：深色状态页，导入运行时、拉起 :game
game/      宿主层（:game）：dex 注入、运行时装载、UnityPlayer、崩溃日志
common/    契约层：导入目录布局、清单格式、哈希
core/      C++ 内核 libpylon.so（boot / il2cpp / hook / dotnet / mod / imgui），用 core/build.ps1 编
managed/   C# 托管层：Pylon.Managed（宿主入口）、Pylon.ModLoader（Mod 框架）、
           Pylon.Mod.Abstractions（Mod 契约）、Pylon.ImGui（ImGui 绑定）
samples/   示例 Mod（FrameMod）
interop/   Il2CppInterop 代理与自编的 Harmony 支持层（harmonysupport）
tools/     环境自检、运行时部署、一条龙构建、真机日志
external/  外部依赖（git 子模块，只钉源码）
refs/      本地游戏 dump（不进 git；仅 refs/README.md 进 git）
runtime/   BepInEx-Android 运行时（不进 git）
docs/      文档
```

## 快速开始

前置：Windows + PowerShell 7、.NET SDK 10、JDK 17、Android SDK + NDK、一台 arm64 设备（可选 adb）。

```powershell
git clone --recurse-submodules https://github.com/Controllerdestiny/Pylon-Loader.git
pwsh -File tools/check-env.ps1
pwsh -File tools/pipeline.ps1 -BepInExApk D:\dl\app-release-aligned.apk -DummyDir refs\DummyDll
```

`pipeline.ps1` 依次补齐子模块、`runtime/`、`interop/generated/`、内核、托管件与 APK；每一阶段幂等，缺件时会说明要传哪个参数。也可以手工分步（或只重打 APK）：

```powershell
pwsh -File tools/deploy-runtime.ps1     # 运行时 native → jniLibs；BCL/托管件 → assets
pwsh -File core/build.ps1               # 内核 libpylon.so
gradlew :app:assembleDebug              # 打包（会先编 interop/harmonysupport）
```

仓库**不含**三类内容：`refs/`（游戏 dump）、`runtime/`（CoreCLR/BCL/BepInEx 托管栈）、`interop/generated/`（由游戏程序集派生的代理）。它们的来源、补齐方式与 CI artifact 见 [`docs/构建-从零开始.md`](docs/构建-从零开始.md)。

安装 APK 后：打开启动器 → 选择设备上已安装的游戏 → 导入运行时 → 启动。debug 构建默认不把 `refs/` 里的游戏文件打进 APK；只有加 `-PpylonStageRefs=true` 才会预置本地回退副本。

## 写一个 Mod

Mod 是一个 .NET 10 类库。最小入口实现 `Pylon.Mod.IPylonMod`；使用框架时直接派生 `Pylon.ModLoader.Mod`：

```csharp
public sealed class MyMod : Pylon.ModLoader.Mod
{
    public override void Load() => Logger.Info("hello");
}
```

物品等内容的写法与 tModLoader 的使用习惯一致（`ModItem` 的 `SetDefaults` / `Shoot` / `UseItem`，`ModProjectile` 的 `AI` / `OnKill` 等）。完整示例在 [`samples/FrameMod`](samples/FrameMod)，包含物品、弹幕、NPC、Buff、坐骑、前缀、稀有度、配方、配置、触屏按钮与 ImGui Tab。

安装：把编译产物放进设备的
`/storage/emulated/0/Android/data/dev.pylon.loader/Mods/<id>/`（`mod.dll` + 可选 `manifest.json` / `assets/` / `lib/`），或直接把单个 dll 丢进 `Mods/`。该目录是应用专属外部目录，不需要额外存储权限；改完重启游戏生效。
也可以直接在启动器的「Mod 管理」页导入 `.dll` / `.zip` / `.pmod` / 整个 Mod 文件夹，或删除已有 Mod。开关写入 `Mods/.pylon/selection.json`，托管侧加载时优先读它（不覆盖各 Mod 自己的 `manifest.json`）。

### 单文件包与图标

- **`.pmod`** 是 Mod 目录的 zip：根目录 `manifest.json` + 主程序集（`mod.dll` / 同名 dll / 唯一 dll）+ 可选 `icon.png`、`assets/`、`lib/`。
- 在 Mod 管理页点「打包为 .pmod」，选一个包含主程序集的文件夹，输出到 `Mods/<id>.pmod`；宿主启动时把它解到内部缓存，再交给现有加载器，和文件夹 Mod 走同一套选择逻辑。
- Mod 图标：Mod 根目录放 `icon.png`（或 `assets/icon.png`，或 manifest 的 `icon` 指路）就会显示在管理页；打包时没有图标会自动生成一个首字母占位图标，文件夹 Mod 没有图标时显示文件夹占位图。

> 编译 Mod 需要 `interop/generated/` 里的游戏代理程序集（`Assembly-CSharp`、`Il2Cppmscorlib` 等）；它们由**你自己的游戏文件**生成、不能分发。生成方式见构建文档第 4 节。

## 文档

- [`docs/构建-从零开始.md`](docs/构建-从零开始.md)：新 clone 怎么补齐三类二进制、工具链、报错对照、CI artifact。
- [`docs/migration-map.md`](docs/migration-map.md)：内容管线与上游参考的逐项对照、当前完成度、后续路线。
- [`core/README.md`](core/README.md)：内核的目录、构建契约与硬约束（改内核前先读）。
- [`refs/README.md`](refs/README.md)：游戏 dump 的来源、版本纪律与用途边界。
- [`PROMPT.md`](PROMPT.md)：新会话 / 交接用的项目现状与提示词。

代码注释里的 `docs/NN` 是早期设计笔记的编号；那批笔记没有随仓库发布，现行文档以上面几份为准。

## 纪律与边界

- **不提供游戏文件**：仓库、Release、CI artifact 都不含游戏二进制或由游戏派生的代理；`refs/`、`runtime/`、`interop/generated/` 被 `.gitignore` 排除。
- **必须使用自己的正版**：导入与生成都以你设备上已安装的 `com.and.games505.Terraria` 1.4.5.8.6 为准；本项目不绕过任何购买或校验流程。
- **名称与商标**：产品名不含 "Terraria"；只在描述里写 "for Terraria Mobile"。图标不使用游戏美术。
- **不复制上游代码**：[Bunny Loader](https://github.com/JonataOliveiraa/Bunny-Loader.git) 采用 PolyForm Noncommercial，与本项目不兼容；只借鉴其技术路线与实测结论，代码为独立实现。
- **按名字解析**：不硬编码游戏偏移；解析失败就停用 Mod 并写日志，不要静默。
- **Harmony 支持层用仓库里这份**（`interop/harmonysupport`，带值类型参数修复），不要替换成上游原包。
- 目标平台：`com.and.games505.Terraria` 1.4.5.8.6（Unity 2021.3.56f2，arm64-v8a）。

## 许可证

本项目采用 [Pylon Loader 非商业许可 —— GNU GPL v3 修改版](LICENSE)：官方 GPL-3.0 全文 + 附加条款，核心要求：

- **禁止商用**：不得将本项目代码或其衍生作品用于任何商业目的（销售、收费服务、商业项目集成等），除非获得版权所有者书面许可。
- **使用即开源**：任何使用、包含或修改本项目代码的项目，无论以何种形式发布，都必须开源（Copyleft），并保留本许可全部条款与署名。

> 注意：附加条款使本许可属于 GPL-3.0 的修改版（非官方 GPL-3.0），与标准 GPL/MIT/Apache 代码不自动兼容；分发时请随附完整 [LICENSE](LICENSE)。如有疑问，请先与维护者确认。
