# Mod 开发

Mod 是一个 .NET 10 类库，运行时被加载进游戏进程，与游戏共享同一个 IL2CPP 运行时。本文覆盖工程配置、生命周期、能写什么以及调试回路。

## 1. 工程

| 引用 | 用途 |
|---|---|
| `Pylon.Mod.Abstractions` | 最小契约：`IPylonMod` / `IModContext` / `IModImGuiTab` |
| `Pylon.ModLoader` | 内容框架：`Mod` 基类、内容类型、配置、UI、效果 |
| `interop/generated/*.dll` | 游戏程序集的强类型代理（`Assembly-CSharp`、`Il2Cppmscorlib` 等） |
| `Pylon.NaviteAPI` | 内核导出面的 C# 绑定；自绘界面用它的 `Pylon.NaviteAPI.ImGui`（多数场景用 `IModImGuiTab` 就够） |

```xml
<TargetFramework>net10.0</TargetFramework>
<Nullable>enable</Nullable>
<AllowUnsafeBlocks>true</AllowUnsafeBlocks>
```

代理程序集由**作者本机的游戏文件**生成，不能分发；这也决定了 Mod 与游戏版本绑定（见第 8 节）。

## 2. 两个入口

最小入口，只需要实现契约：

```csharp
public sealed class MinimalMod : Pylon.Mod.IPylonMod
{
    public void Load(IModContext context) => context.Logger.Info("hello");
}
```

实际开发几乎都派生框架基类，它替你处理日志、路径、内容注册：

```csharp
public sealed class MyMod : Pylon.ModLoader.Mod
{
    public override void Load()
    {
        Logger.Info($"loading {Name} {Version}");
    }

    public override void SetupContent()
    {
        // 游戏内容表已就绪，这里挂依赖游戏状态的钩子
    }
}
```

## 3. 生命周期

| 回调 | 时机 | 适合做的事 |
|---|---|---|
| `Load()` | 早阶段，内容已占号、游戏未就绪 | 注册内容、读配置 |
| `OnModLoad()` | 早阶段末尾 | 依赖其他 Mod 的初始化 |
| `OnLocalizationsLoaded()` | 本地化表就绪 | 注册文本 |
| `ResizeArrays()` | 内容表扩号 | 需要按号段处理时 |
| `OnContentReady(ctx)` | 游戏内容就绪（晚阶段） | 安装依赖游戏状态的钩子 |
| `SetupContent()` | 同上，框架内顺序执行 | 贴图、配方引用、UI 注册 |
| `AddRecipeGroups()` / `AddRecipes()` | 配方阶段 | 配方与配方组 |
| `OnModUnload()` | 正常退出或被关停（被系统强杀不跑） | 释放外部资源 |

早阶段只保证"内容能占号"，任何依赖游戏运行状态的东西都放到晚阶段，这是最常见的踩坑点。

## 4. 内容类型

框架提供 tModLoader 习惯的基类，内容 ID 由框架统一分配：

```text
ModItem  ModProjectile  ModNPC  ModBuff  ModMount  ModPrefix  ModRarity
ModRecipe  ModTile  ModWall  ModPlayer  ModConfig  GlobalItem / GlobalNPC / GlobalProjectile …  ModType
```

一个典型的物品：

```csharp
public sealed class Fireball : ModItem
{
    public override void SetDefaults(Item item)
    {
        item.damage = 42;
        item.width = item.height = 32;
        item.shoot = ProjectileID.Fireball;
        item.shootSpeed = 14f;
        SetDefaultWeaponStyle(useTime: 12, autoReuse: true);
    }

    public override bool Shoot(Item item, Player player, Vector2 position, Vector2 velocity,
                              int type, int damage, float knockBack)
    {
        Projectile.NewProjectile(player.GetProjectileSource_Item(item), position, velocity,
                                 ModContent.ProjectileType<TestFireball>(), damage, knockBack, player.whoAmI);
        return false; // 不发射原版那发
    }
}
```

代理类型是强类型的：字段直接读写、方法直接调用，有 IDE 补全与编译期检查，不需要手写反射字符串。

## 5. 改原版行为

订阅代理程序集里生成的 `On.*` 事件即可，不需要 Harmony 或额外框架：

```csharp
public override void SetupContent()
{
    On.Terraria.Main.Update += (orig, self, time) =>
    {
        orig(self, time);
    };
}
```

同一方法的多个订阅者按订阅顺序串成链，`orig` 是链上的下一环。钩子异常在框架边界内被吞掉并记日志，不会打断游戏。

## 6. 资源与界面

```csharp
Texture2D icon = LoadTexture("assets/icon.png");     // 走 GPU 抓取，不要求纹理可读
Asset<Texture2D> asset = LoadAsset("assets/icon.png");

RegisterImGuiTab(new MyTab());                        // 或者实现 IModImGuiTab 后注册
```

- 贴图路径相对 Mod 目录，只能在游戏线程调用。
- ImGui 页出现在 `Pylon Mod` 窗口的 Tab 里，画在游戏自己的 GLES 上下文，不需要悬浮窗权限。
- 屏幕效果见 `Pylon.ModLoader.Effects`：`ScreenShader` / `ScreenEffect` / `ScreenGraphics`，可选世界层或屏幕层。

## 7. 配置

`ModConfig` 由框架统一读写：字段即配置项，界面在游戏的设置菜单里，存档在 Mod 数据目录。配置变更不会自动重载内容，需要时自己订阅变更。

## 8. 打包与部署

三种形态，运行时等价：

| 形态 | 结构 |
|---|---|
| 文件夹 Mod | `Mods/<id>/mod.dll`（或与 id 同名的 dll）+ 可选 `manifest.json`、`assets/`、`lib/`、`icon.png` |
| 单文件包 | `Mods/<id>.pmod`：同结构的 zip |
| 单个 dll | 直接放 `Mods/xxx.dll`（无资源时最省事） |

- 设备路径：`/storage/emulated/0/Android/data/dev.pylon.loader/Mods/`，应用专属外部目录，无需存储权限。
- 可写数据目录：`Mods/_data/<id>/`，升级 Mod 不会清掉，适合存档与缓存。
- 启停状态：`Mods/.pylon/selection.json`，由启动器的"Mod 管理"页维护。
- 图标：`icon.png` 或 `assets/icon.png`；打包时缺失会自动生成占位图标。

## 9. 调试回路

| 场景 | 命令 |
|---|---|
| 改了 Mod 源码 | `pwsh -File src/csharp/tools/pylon.ps1 push -Samples <名字>` |
| 改了托管层 / 框架 | `pwsh -File src/csharp/tools/pylon.ps1 managed` |
| 看日志 | `adb logcat -s PylonLoader` |

两者都不需要重装 APK：框架用资产指纹判断是否需要重新解包，Mod 就是普通文件。

## 10. 约定

- **每帧/每实体路径上不分配**：不闭包、不字符串拼接、不 `new List<T>()`。
- **先判有没有人覆写**：为空钩子付出的分派成本也要避免。
- **已发布 API 不改成员形态**：属性与字段互换会让老 Mod 抛 `MissingFieldException`。
- **不要引入第二套 detour**：钩子只有 `Il2CppInterop.RuntimeDetour` 一条链。
- **版本绑定**：换游戏版本必须用新的 `interop/generated` 重编 Mod。
