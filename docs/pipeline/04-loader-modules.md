# 管线四：模块装配（Loader Modules）

## 职责

把"某一类内容需要挂哪些游戏钩子"从主流程里拆出去，成为独立的、可增删的模块。

一个模块 = 一类内容的接线（物品、弹幕、NPC、方块、网络、音效……）。

## 原理

现状靠反射发现 `ILoaderModule`，用 `Order`（100 / 200 / 300…）决定安装顺序，撞号只记一条警告。这有两个后果：

* **顺序是隐式契约**：`SetDefaults` 补丁必须先于按类型分派的 Loader，这条规则只存在于数字大小的记忆里；
* **类型集合在运行时才确定**：反射失败（缺代理程序集）会让整张安装表静默缩水。

改为**显式数组**：

```csharp
internal static class LoaderRegistry
{
    private static readonly ILoaderModule[] Modules =
    [
        new ItemLoader(),        // 1. SetDefaults 补丁与物品使用链
        new ProjectileLoader(),  // 2. 弹幕
        ...
    ];
    public static ReadOnlySpan<ILoaderModule> All => Modules;
}
```

数组顺序就是安装顺序，读代码即可看到；漏写编译不过。

## 设计

### 模块契约

```csharp
internal interface ILoaderModule
{
    string Name { get; }        // 日志里的短名
    bool Install();             // 挂钩子；幂等；false = 条件未满足，下次再试
}
```

* 不再有 `Order`：位置即顺序。
* 不再有"构造注入日志"：模块用统一的日志出口（`Safe.Info` / `Safe.Warn`），不持有状态。
* 不再反射构造：没有 `Activator`、没有构造签名约束。

### 需要刷新的模块

`NpcLoader` 之类要按"有没有 Global 覆写这套钩子"重算门控。保留第二个接口：

```csharp
internal interface ILoaderModuleRefresh
{
    void Refresh();
}
```

装配器在每次 Mod 注册后调用它；是否实现由模块自己决定，不做强制。

### 装配时机

```
LoaderHost.Attach(mod)
  ├── 首次：LoaderRegistry 全部 Install()（按数组顺序）
  ├── 每个 Mod 后：所有 ILoaderModuleRefresh.Refresh()
  └── 全部完成后：HookBus.Rebuild()
```

`Install()` 返回 false 的模块留在"未安装"集合里，下次 `Attach` 再试——这是"内容还没出现"的唯一失败语义。

### 模块之间不许互相调用

模块只依赖三样东西：游戏 `On.*`、`ModContent` 门面、`HookBus`。跨模块协作通过注册表（`GlobalItemRegistry` 一类）而不是直接引用另一个 Loader 的静态方法。

## 扩展方式

新增一类内容：

1. 实现 `ILoaderModule`（`Install` 里挂 `On.*`）；
2. 在 `LoaderRegistry.Modules` 里按正确位置加一行；
3. 需要内容类型的安装步骤 → 在 `InstallPlan.Build()` 对应阶段加步骤。

不需要改 `LoaderHost`、不需要改枚举、不需要改文档以外的任何注册表。
