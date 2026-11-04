# 架构

Pylon 让 Mod 跑在游戏自己的进程里：启动器负责选择与校验，宿主持有游戏，内核在进程内接上 IL2CPP、CoreCLR 与图形层。本文按"从冷启动到一帧画面"的顺序说明这四层各自做什么、边界在哪。

## 1. 分层

```
┌──────────────────────────────────────────────────────────────┐
│ :app  启动器（Kotlin，独立进程）                              │
│   选择已安装游戏 → 导入运行时（解包 + sha256 清单）→ 拉起宿主  │
└───────────────────────────┬──────────────────────────────────┘
                            │ startActivity(:game)
┌───────────────────────────▼──────────────────────────────────┐
│ :game  宿主（Kotlin，游戏所在进程）                            │
│   注入游戏 dex → 装载游戏 .so → 挂载游戏 assets → UnityPlayer  │
└───────────────────────────┬──────────────────────────────────┘
                            │ JNI
┌───────────────────────────▼──────────────────────────────────┐
│ libpylon.so  内核（C++20）                                     │
│   绑定 il2cpp_* → 等引擎稳定 → 校验引用 → Dobby detour         │
│   → 起 CoreCLR → ImGui / 屏幕效果                              │
└───────────────────────────┬──────────────────────────────────┘
                            │ P/Invoke + 代理程序集
┌───────────────────────────▼──────────────────────────────────┐
│ 托管层（.NET 10，进程内 CoreCLR）                              │
│   Pylon.Managed（入口）→ Pylon.ModLoader（内容框架）→ Mod dll   │
└──────────────────────────────────────────────────────────────┘
```

四层的职责边界是硬性的：

| 层 | 可以做 | 不可以做 |
|---|---|---|
| 启动器 | 读写自己的私有目录、读已安装游戏的 APK、启动宿主 | 加载游戏库、跑游戏代码 |
| 宿主 | 建立并持有 Unity 的 `Activity`、装载 .so、拼 AssetManager | 解析 IL2CPP、执行托管代码 |
| 内核 | 绑定 `il2cpp_*`、hook、跑 CoreCLR、画覆盖层 | 拥有 UI 生命周期、写游戏存档 |
| 托管层 | 声明内容、订阅 `On.*`、调游戏 API | 碰原生句柄以外的内存布局 |

## 2. 启动时序

实测数据来自开发基线（小米 2509FPN0BC，Android 17，arm64，warm start），`stage()` 打印的偏移是相对宿主进程创建：

| 时刻 | 事件 | 说明 |
|---|---|---|
| +26ms | 宿主 `onCreate` | 起线程做 dex 注入与运行时装载 |
| +71~107ms | CoreCLR 预热开始 | 与引擎初始化并行，不占游戏线程 |
| +142ms | `coreclr_initialize` 完成 | 委托解析、TPA 扫描已就绪 |
| ~+195ms | `il2cpp_init` 返回 | 此时才允许调 IL2CPP API |
| +219ms | 引用校验通过 | 逐条按名解析，失败则停用 Mod |
| +230ms | `UnityPlayer` 起来 | 游戏主循环开始 |
| 41~73ms | 托管入口执行 | CoreCLR 跑 `Pylon.Managed.Entrypoint` |
| ~+14s | Mod 内容安装完成 | 等游戏线程与内容表就绪，不是框架开销 |

关键点：**CoreCLR 的准备工作与游戏初始化并行**。`Prepare` 阶段只做 TPA 扫描、`coreclr_initialize` 与委托解析，完全不碰 IL2CPP；等引用校验通过后，`RunEntry` 才在独立大栈线程上执行托管入口。

## 3. 内核（`libpylon.so`）

| 目录 | 职责 |
|---|---|
| `src/boot` | 阶段状态机（`KernelLoaded` → `EngineReady` → `RefsValidated` / `Failed`）、就绪探测、引用校验与报告 |
| `src/il2cpp` | `il2cpp_*` 符号表、域与线程票据、按名解析类/字段/方法、`runtime_invoke` 调用 |
| `src/hook` | Dobby 的 `DobbyHook` / `DobbyDestroy` 出口，供托管侧 detour provider 调用 |
| `src/dotnet` | CoreCLR 托管（`Prepare` / `RunEntry` / `CallManagedEntry`） |
| `src/mod` | `PylonMod_*` 导出 ABI：解析、读写字段、调方法、hook |
| `src/graphics` | GLES/EGL 入口表、GL 状态镜像、帧编排（世界层 / 屏幕层 / 覆盖层）、屏幕效果 |
| `src/imgui` | ImGui 源码与 GLES3 后端、触摸输入桥、跨线程快照与上屏 |
| `src/jni` | 唯一的 JNI 边界（`PylonCore` 的 native 方法） |

### 3.1 引用校验

内核不认识任何具体游戏版本：所有类、字段、方法都按名字解析（`Assembly-CSharp` 优先，缺失时扫全部程序集）。任一条**必需**引用解析失败，内核进入 `Failed`，只停用 Mod 并写清楚原因，游戏照常运行。这是"Mod 可以没有，游戏必须能跑"的落地方式。

### 3.2 线程模型

| 场景 | 规则 |
|---|---|
| `il2cpp_init` 执行期间 | 禁止调用任何 IL2CPP API，否则 SIGSEGV |
| 外来线程首次访问 | 必须先 `il2cpp_thread_attach()` |
| 托管入口 | 跑在 64 MB 栈的独立线程，不占游戏线程 |
| IL2CPP 对象与方法 | 只在 UnityMain 上碰；跨线程调用会被拒绝并记日志 |
| detour 回调 | 由 Dobby 跳板进入，异常在托管侧边界内消化 |

## 4. 托管层

| 程序集 | 职责 |
|---|---|
| `Pylon.Mod.Abstractions` | 最小契约：`IPylonMod` / `IModContext` / `IModImGuiTab` / `ImGuiTabRegistry` |
| `Pylon.Managed` | CoreCLR 入口：原生解析、Dobby 后端、Il2CppInterop 启动、Mod 发现与加载 |
| `Pylon.ModLoader` | 内容框架：`Mod` 基类、五条管线（见 [pipeline/](pipeline/README.md)）、`Global*`、配置、UI、效果 |
| `Pylon.NaviteAPI` | 内核导出面的唯一 C# 绑定：ImGui（`ig*` / `PylonImgui_*`，机器生成）、屏幕特效（`PylonFx_*`）、宿主（`PylonMod_*`）。全工程只有这里声明 `LibraryImport` |

### 4.1 管线

`Pylon.ModLoader` 的内容流程由一条实例化的宿主（`LoaderHost`）串起四段：注册（发现类型 + 确定发号）、安装（阶段 + 预算 + 可续步骤）、钩子分派（`HookId` 位集 + 稠密订阅表）、模块装配（显式数组）。设计取舍见 [pipeline/](pipeline/README.md)。

### 4.2 钩子链

只有一条链：

```
On.Terraria.Main.Update += handler
        │  代理程序集里的静态事件（Il2CppInterop.RuntimeDetour 生成）
        ▼
Il2CppInterop 的 detour 引擎
        ▼
Pylon.Managed 的 DetourBackend（P/Invoke）
        ▼
内核 src/hook/HookApi.cpp → DobbyHook
```

因此：不要引入 Harmony、MonoMod 或第二套 detour；`On.*` 由 `interop/generated` 生成，随游戏版本走。

## 5. 图形与 UI

游戏线程在 `Publish` 时交出一帧快照，渲染线程按三个层次绘制：

| 层 | 内容 | 接入点 |
|---|---|---|
| 世界层 | 屏幕效果（画在世界之上、界面之下） | 世界 Blit 之后 |
| 屏幕层 | 屏幕效果（盖住一切） | 帧末、`eglSwapBuffers` 之前 |
| 覆盖层 | ImGui（`Pylon Mod` 窗口与 Mod Tab） | 同上，默认 framebuffer |

不 patch Unity 的 `SpriteBatch.Draw` / `Main.EntitySpriteDraw`（手机版会渲染错乱），也不需要第二块 Surface 或 `SYSTEM_ALERT_WINDOW`：ImGui 直接画在游戏自己的 GLES 上下文里。屏幕效果着色器用 GLSL ES 3.00，编译延迟到渲染线程首次绘制，报错带 Mod 文件行号。

## 6. 失败隔离

按边界从外到内四层：

1. `Pylon.Managed.CrashGuard`：`AppDomain.UnhandledException` 与未观察的 Task，记录并去重。
2. `Entrypoint`：`Start` / `StartMods` 各包一层，启动失败不静默。
3. hook 引擎：每个 `On.*` 调用由 stub 包住，handler 异常记日志并返回默认值。
4. `Pylon.ModLoader.Safe`：同一 hook 内多个 Mod 之间的隔离。

内核侧对应的是引用校验：缺符号只降级，不崩进程。

## 7. 相关文档

- [构建](build.md) ｜ [运行时导入](runtime-import.md) ｜ [Mod 开发](modding.md) ｜ [排障](troubleshooting.md) ｜ [路线](roadmap.md)
- 内核目录、编译契约与硬约束：[src/core/README.md](../src/core/README.md)
