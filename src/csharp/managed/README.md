# 托管层

进程内 CoreCLR 上运行的 C# 代码。四个工程：

| 工程 | 职责 |
|---|---|
| `Pylon.Mod.Abstractions` | 给 Mod 作者的最小契约：`IPylonMod` / `IModContext` / `IModImGuiTab` / `ImGuiTabRegistry` |
| `Pylon.Managed` | CoreCLR 入口：原生解析、ShadowHook 后端、Il2CppInterop 启动、Mod 发现与加载 |
| `Pylon.ModLoader` | 内容框架：`Mod` 基类、内容管线、`Global*`、配置、UI、效果 |
| `Pylon.NaviteAPI` | 内核导出面的唯一 C# 绑定（ImGui / 特效 / 宿主）；全工程只有这里声明 `LibraryImport` |

## 目录

```text
Pylon.Mod.Abstractions/   契约（一个接口一个文件）
Pylon.Managed/
  Entrypoint.cs           内核解析的入口（签名不可改）
  Hosting/                启动编排、日志、全局错误边界、就绪探测
  Interop/                原生库解析、ShadowHook 后端、Il2CppInterop 启动
  Mods/                   清单解析、目录扫描、依赖排序、加载管线
Pylon.ModLoader/
  Mod.cs                  作者基类
  Core/                   日志、异常边界、ID 分配、就绪队列、注册表
  Content/                内容基类（ModItem / ModNpc / …）
  Contracts/              值类型契约
  Globals/                Global* 基类
  Loaders/                各内容管线（ILoaderModule）与触屏按钮
  Patch/  Textures/  Tooltips/  Effects/  UI/  ImGui/
Pylon.NaviteAPI/          内核导出面的唯一绑定（全工程只有这里写 P/Invoke）
  ImGui/                  namespace Pylon.NaviteAPI.ImGui：ig* / PylonImgui_*
    Generated/            机器生成（tools/imgui-gen），不手改
  Effects/                namespace Pylon.NaviteAPI.Effects：PylonFx_*
  Host/                   namespace Pylon.NaviteAPI.Host：PylonMod_*（日志 / hook / 线程 / 指令改写）
```

## 规范

- 一个文件一个公开类型；文件级命名空间；可空引用开启，`TreatWarningsAsErrors` 零警告基线。
- 注释只写"为什么"（时序约束、平台坑、非直觉取舍），公开 API 一行 `<summary>` 足够。
- **已发布 API 不改成员形态**：属性与字段互换会让老 Mod 抛 `MissingFieldException` / `MissingMethodException`。重构只改可空标注与实现。
- 工程职责不交叉：宿主逻辑不进框架，框架不反向依赖宿主。
- **P/Invoke 只许写在 `Pylon.NaviteAPI`**：内核导出面变了一处改；其他工程不再出现 `DllImport` / `LibraryImport`。
  绑定统一用源生成的 `LibraryImport`（运行期编组的 `DllImport` 不再新增）。

## 错误边界

由外到内四层，业务代码不逐调用写 `try/catch`：

1. `Pylon.Managed.CrashGuard`：`AppDomain.UnhandledException` 与未观察的 Task，记录并去重。
2. `Entrypoint`：`Start` / `StartMods` 各包一层，启动失败不静默。
3. hook 引擎：每个 `On.*` 调用由 stub 包住，handler 异常记日志并返回默认值。
4. `Pylon.ModLoader.Safe`：同一 hook 内多个 Mod 之间互相隔离。

其余 `try/catch` 只留给两种情况：必须还原状态（配合 `finally`），或跨原生/进程边界；捕获范围要收窄。

## 内容管线

每个内容管线实现 `ILoaderModule`，由 `LoaderBootstrap` 反射发现、用首个 Mod 的日志构造、按 `Order` 升序安装：

```csharp
internal sealed class ItemLoader : ILoaderModule
{
    public ItemLoader(ModLogger logger) => _logger = logger;

    public string Name => "item";
    public int Order => 100;

    public bool Install()          // false = 内容还没出现，下次 Mod 注册再试
    {
        On.Terraria.Player.ItemCheck_CheckCanUse_Inner += CanUse;
        return true;
    }
}
```

- 安装状态只记在 `LoaderBootstrap`，Loader 内不要再写 `_installed`。
- 不写转发方法；接口方法就是唯一入口。
- 需要每次 Mod 注册后刷新缓存的模块实现 `ILoaderModuleRefresh`。
- 新增管线 = 新增一个实现类，`LoaderBootstrap` 不用改。

## 性能

- 每帧 / 每实体路径不分配：不闭包、不拼字符串、不 `new List<T>()`。
- 分派前先判"有没有人覆写"（`OverridePlan` / `AnyWith`），没有就整段跳过。
- 表扩容只在内容就绪时做一次。

## 构建与调试

```powershell
dotnet build src/csharp/managed/Pylon.ModLoader/Pylon.ModLoader.csproj -c Release -m:1 -nodeReuse:false
pwsh -File src/csharp/tools/pylon.ps1 managed      # 编 + 推进设备，不重装 APK
pwsh -File src/csharp/tools/pylon.ps1 run          # 启动并抓日志
```

单节点构建是因为本机环境下 MSBuild 的多进程节点不可用；离线时 NuGet 漏洞审计的 `NU1900` 不计为错误（见 `Directory.Build.props`）。

Mod 作者视角的 API 与示例见 [Mod 开发](../../../docs/modding.md)。
