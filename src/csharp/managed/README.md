# 托管层（managed）代码规范与重构说明

本目录是 Pylon 的 C# 托管层，包含三个工程：

| 工程 | 职责 | 状态 |
| --- | --- | --- |
| `Pylon.Mod.Abstractions` | 给 Mod 作者的最小契约（`IPylonMod` / `IModContext` / `IModImGuiTab`） | ✅ 已重构 |
| `Pylon.Managed` | CoreCLR 入口：原生解析、hook 后端、Il2CppInterop 启动、Mod 发现与加载 | ✅ 已重构 |
| `Pylon.ModLoader` | Mod 内容框架（tModLoader 风格的 API、内容管线、UI） | ✅ 已重构（含 `Loaders/`；全工程 0 warning，已开启 `TreatWarningsAsErrors`） |

`Pylon.ImGui` 是机器生成的绑定，不参与本规范。

## 1. 代码规范

- **一个文件一个类型**。嵌套的私有实现类型（如 `ModCatalog.VisitState`）可以留在宿主类里，
  但不再出现「一个文件塞五个公开类型」。
- **文件级命名空间**（`namespace Pylon.ModLoader;`），不再用大括号包住整个文件。
- **可空引用类型开启**。新代码必须写对 `?` 与 `!`，不要用 `#nullable disable` 绕过。
- **现代 C#**：集合表达式 `[]`、模式匹配、`switch` 表达式、主构造函数、`using` 声明、
  `FrozenSet`/`FrozenDictionary`、`LibraryImport`（宿主侧 P/Invoke）。
- **命名**：类型/公开成员 PascalCase；私有字段 `_camelCase`；局部变量/参数 camelCase；
  接口以 `I` 开头。成员访问不写 `this.`。
- **不写无意义的分支**：例如 `if (result) return true;` 一律合并为 `return result || ...`。
- **已发布 API 不改字段/属性形态**：Mod 是按成员形态编译的，属性改字段（或反过来）会让老 Mod
  运行时抛 `MissingFieldException` / `MissingMethodException`。重构只允许改可空标注与实现，
  例如 `SoundStyle.Options` 必须保持公共字段（真机踩过）。
- **每个工程一个 csproj 职责**：不把宿主逻辑塞进框架，也不让框架反向依赖宿主。

## 2. 注释规范

只写「为什么」，不写「是什么」：

- 删除文件头的「层：……」说明和逐行翻译式注释；
- 保留：时序约束（例如「必须在任何 On.* 订阅之前设置」）、
  平台坑（il2cpp / Android / Dobby）、
  非直觉的取舍（例如「跳板必须保活」）；
- 公开 API 用一行 `<summary>` 说明用途即可。

## 3. 错误处理模型

从外到内只有四层边界，业务代码不要逐调用写 try/catch：

1. `Pylon.Managed.CrashGuard`：`AppDomain.UnhandledException` 与未观察的 Task，
   记录并去重（只能记录，无法阻止进程终止）。
2. `Entrypoint`：`Start` / `StartMods` 各包一层，启动失败不会静默。
3. hook 引擎：每个 `On.*` 调用由 stub 包住，handler 异常记日志并返回默认值，游戏不被打断。
4. `Pylon.ModLoader.Safe`：同一个 hook 内多个 Mod 的隔离，一次失败不影响其余 Mod。

除此之外的 try/catch 只在两种情况下保留：**必须还原状态**（配合 `finally`）或
**跨进程/原生边界**。捕获范围要收窄（`FormatException` 之类），不要 `catch (Exception)` 一把抓。

## 4. 目录结构

```text
Pylon.Mod.Abstractions/   契约（每个接口一个文件）
Pylon.Managed/
  Entrypoint.cs           内核解析的入口（签名不可改）
  Hosting/                启动编排、日志、全局错误边界、就绪探测
  Interop/                原生库解析、Dobby 后端、Il2CppInterop 启动
  Mods/                   清单解析、目录扫描、依赖排序、加载管线
Pylon.ModLoader/
  Mod.cs                  作者基类
  Core/                   日志、异常边界、ID 分配、就绪队列、注册表
  Content/                内容基类（ModItem / ModNpc / ...）
  Contracts/              值类型契约（NetReader、Condition、...）
  Globals/                Global* 基类
  Loaders/                各内容管线（ILoaderModule）+ 触屏按钮
                           LoaderBootstrap 反射发现并按 Order 安装
  Patch/                  原版方法补丁
  Textures/ Tooltips/ Effects/ UI/ ImGui/
```

## 5. 内容管线模块（ILoaderModule）

每个内容管线（Item/Npc/Tile/...）都是一个实现 `ILoaderModule` 的类，由
`LoaderBootstrap` **反射发现并用首个 Mod 的日志构造**，按 `Order` 升序安装：

```csharp
internal sealed class ItemLoader : ILoaderModule
{
    public ItemLoader(ModLogger logger) => _logger = logger;   // 日志只在这一处注入

    public string Name => "item";
    public int Order => 100;

    public bool Install()            // false = 内容还没出现，下次 Mod 注册再试
    {
        On.Terraria.Player.ItemCheck_CheckCanUse_Inner += CanUse;
        return true;
    }
}
```

约定：

- **安装状态只在 `LoaderBootstrap`**（`Installed` 集合）。Loader 里不要再写 `_installed` 标志。
- **不要写转发方法**（`void ILoaderModule.Install(...) => Install(...)`）；接口方法直接就是唯一的入口。
- 需要每次 Mod 注册后刷新缓存的模块额外实现 `ILoaderModuleRefresh.Refresh()`
  （目前只有 `NpcLoader`：要重算"有没有 Global 覆写这套钩子"）。
- 新增管线 = 写一个实现类，`LoaderBootstrap` 不用改。
- 只有 `Order` 决定顺序，反射返回顺序不参与；SetDefaults 补丁必须早于对应的类型分派 Loader。

## 6. 性能约定

- 每帧/每实体路径上不分配闭包与字符串；错误标签按「出事时才拼」。
- 分派前先判定「有没有人覆写」（`OverridePlan` / `AnyWith`），没有就整段跳过。
- 热路径复用静态空列表，不每帧 `new List<T>()`。
- 表扩容只在内容就绪时做一次，不在帧循环里查长度。

## 7. 构建

本机沙箱下 MSBuild 的多进程节点不可用，必须单节点构建：

```powershell
dotnet build src/csharp/managed/Pylon.ModLoader/Pylon.ModLoader.csproj -c Release -m:1 -nodeReuse:false
```

三个工程都开了 `TreatWarningsAsErrors`（零警告基线）。离线时 NuGet 漏洞审计的 `NU1900`
不算错误（见 `Directory.Build.props`），在线时照常显示。

真机冒烟（托管 DLL 热替换，不重编 APK）：

```powershell
pwsh -File src/csharp/tools/push-managed.ps1        # 推 4 个 DLL
adb shell am force-stop dev.pylon.loader
adb logcat -c; adb shell am start -n dev.pylon.loader/.MainActivity -a dev.pylon.loader.action.IMPORT --ez launch true --ez coreclr true
```
