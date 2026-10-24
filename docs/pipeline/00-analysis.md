# 现状分析与问题清单

本文只评估 `Pylon.ModLoader` 这一层（C# Mod 管线）。`Pylon.Managed`（程序集装载）与内核不在改造范围内。

## 1. 现有管线的实际形状

```
Mod.Load()                        ← 实例方法
  ├── DiscoverTypes()             ← 反射扫描程序集，实例化 ModType
  ├── ModContent.Register()       ← static：按"种类 + 类型全名"排序，边注册边自增发号
  ├── ModLoader.Register()        ← static：Mod 表
  ├── LoaderBootstrap.Install()   ← static：反射发现 ILoaderModule，Activator 构造，按 Order 排序
  ├── ModLifecycle.Register()     ← static：装 On.* 钩子 + 把就绪任务排进 Ready
  └── Load() / OnModLoad()        ← 作者代码

OnContentReady()                  ← 实例方法
  ├── ContentInstaller.InstallTextures()  ← static：登记 Mod，武装每帧 tick
  ├── TooltipLoader.Install()
  └── 之后由 ContentInstaller.Tick 每帧一步推进安装
```

也就是说：**没有管线，只有一处一处接起来的钩子**。

## 2. 问题

### 2.1 静态与实例混用

* `Mod` 是实例，但它的 `Load()` 里做全局副作用（`LoaderBootstrap.Install`、`ModLifecycle.Register`、`ModConfigLoader.ModConfigLog = Logger`）。
* 管线状态全部放在 `static` 可变字段里：`ContentInstaller`（11 个 static 字段）、`Ready`（3 个）、`ModContent`（4 个容器）、`LoaderBootstrap`（2 个）、`ModLifecycle`（3 个）。
* 谁在什么阶段可以改这些状态，没有任何地方能表达；只能靠读完整条调用链推断。
* `ItemLoader` 这类模块把构造参数写进 `static` 字段（`private static IModLog _logger`），实例构造与静态状态互相污染。

### 2.2 分派走字符串

* `OverridePlan` 是 `FrozenSet<string>`：每次 `plan.Has("PreAI")` 都是一次字符串哈希 + 集合查找。
* 热路径上反复出现：`ItemLoader.UseStyle` 里一次调用要做 3 次 `mod.Plan.Has(...)` + 3 次 `GlobalItemRegistry.For(item, "...")`；`NpcLoader`、`ProjectileLoader` 的 `PreAI/PostAI` 每帧每实体同样如此。
* `EntityGlobalRegistry.For(entity, method)` 是 `Dictionary<string, MethodEntry>` 查询，外加 `ConditionalWeakTable` 缓存 —— 每帧每实体至少一次字典查找。
* `ModLifecycle` 的每个钩子点都要对所有 Mod 走一遍 `PlanOf(mod).Has("PreUpdateWorld")`：字典（Type→Plan）+ 字符串集合双重开销。

### 2.3 安装调度是闭包 + 每帧一步 + 自我插入

`ContentInstaller.EnsureSteps` 用 `List<Action>` + 并行 `List<string>` 描述步骤；"扩表分帧"靠把闭包 `_steps.Insert(_step, self)` 插回队列。后果：

* 步骤顺序只能靠阅读 `Add(...)` 的书写次序；
* 步骤的"可续"语义藏在闭包里，无法复用、无法单测；
* `Timed` 靠闭包捕获 + `Stopwatch`，失败只记一行日志；
* 620 行一个类型里同时做：就绪判定、扩表、贴图、名字、样例、静态默认值、装表维护。

### 2.4 注册表职责过载

`ModContent` 一个 static 类里同时是：ID 分配器、反查表（按类 / 按名 / 按 ID）、贴图缓存、数据目录规则、`ContentKind` 判定、游戏基表数量表。任何一处想改都要动这一个类。

### 2.5 Loader 装配靠反射 + 魔法数字

`LoaderBootstrap` 反射扫描 `ILoaderModule`、`Activator` 构造、按 `Order`（100/200/300...）排序，撞号只 warning。安装顺序是隐式契约：改一个 Order 就改变全流程语义，但编译期不会有任何提示。

### 2.6 就绪阶段的契约分散

`Ready`（8 个阶段）与 `ContentInstaller` 的步骤列表是两套并行机制，同一件事（"内容就绪后依次做什么"）分成两处表达；`ModLifecycle` 又在加载时往 `Ready` 里塞任务。三处协作，没有单一事实来源。

## 3. 要做成什么

| 现状 | 目标 |
|---|---|
| static 状态散落 | 单一实例 `LoaderHost` 持有全部状态；静态只做门面转发 |
| 字符串钩子判定 | `HookId` 枚举 + `HookMask` 位集；热路径无哈希 |
| 闭包步骤队列 | 显式 `IInstallStep` 列表 + 预算执行器，可续步骤有类型 |
| 反射装配 | 显式 `LoaderRegistry` 数组，顺序即位置 |
| 两套就绪机制 | 一条安装管线，阶段是线性的 |
| 注册表职责过载 | `ContentRegistry`（发号 + 槽表）/ `ModContent`（公开门面）/ `ContentTables`（基表长度）分离 |

## 4. 明确的取舍

* `ModType` / `Mod` 仍是**类**：它们是作者继承并覆写的 API 面，改成 struct 会让作者代码变形。
* **大量数据（内容描述符、槽表、订阅表）用 `struct` + `ref` 访问**：`ContentDescriptor`、`HookMask`、`HookBus` 的通道数组，全部按 ID 直接索引返回 `ref`，不装箱、不字典。
* 公开 API（`Mod`、`ModType`、`ModContent.*`、`ILoaderModule`）保持形态，作者 Mod 不需要改代码。
