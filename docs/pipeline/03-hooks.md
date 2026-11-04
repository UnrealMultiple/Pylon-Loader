# 管线三：钩子分派（Hooks）

## 职责

把游戏事件分发给"确实需要它的" Mod / 内容，并保证分派本身在每帧、每实体路径上几乎不花钱。

覆盖两类需要：

* **Mod 级**：`Mod` 上的生命周期方法（`PreUpdateWorld`、`PostAI` 一类，约 90 个）。
* **内容级**：`ModItem` / `GlobalItem` / `ModNPC` 一类内容类型上的钩子（约 60 个）。

## 原理

现状用 `FrozenSet<string>` 判"这个类覆写了哪些方法"：每次判定是一次字符串哈希。问题不在哈希慢，而在**判定的次数**——每帧 × 每 Mod / 每实体 × 每个钩子点。

把这个判定换成**编译期常量索引**：

* 所有钩子编号进 `HookId` 枚举；
* 一个类的覆写集合是位集 `HookMask`（`ulong × N`）；
* 分派 = 迭代"订阅者稠密数组"，数组为空就直接返回。

于是热路径上：无字符串、无字典、无枚举器、无分配。

## 设计

### HookId

```csharp
internal enum HookId { PreUpdateWorld, PostUpdateWorld, PreAI, ... }
```

编号只在本进程内使用，不落盘、不过网，所以可以自由增删；**但不要重排已存在的项**，否则 `HookMask` 的位含义会漂移。新增一律追加在末尾。

### HookMask

```csharp
internal readonly struct HookMask
{
    private readonly ulong _lo, _hi;      // 128 位 = 128 个钩子
    public bool Has(HookId id) => ...;    // 纯位运算，可内联
}
```

* `readonly struct`：按值传递，无堆分配，无继承。
* 扫描只做一次：`HookScan.Of(type, baseType)` 比较 `MethodInfo.DeclaringType != baseType`，与现状语义一致（继承来的空实现不算覆写），结果以位集保存。

### 订阅表

```csharp
internal sealed class HookBus
{
    private Mod[][] _channels;            // 下标 = (int)HookId
    public void Rebuild(ReadOnlySpan<Mod> mods);   // 注册结束时构建一次
    public ReadOnlySpan<Mod> Channel(HookId id);   // 空表就是 Empty
}
```

* 每个 `HookId` 一条通道，只装覆写了它的 Mod；通道按 Mod 加载顺序排列，保证不同 Mod 之间的相对顺序稳定。
* 通道数组重建只发生在"有新 Mod 注册"时（加载期），分派期只读。
* 分派点的写法固定为：先取 span，空则返回；否则 `for` 遍历。**不做 foreach 装箱、不用 LINQ**。

### 内容级分派

`Global*` 注册表沿用"模板 + 方法索引"，但索引键从字符串改成 `HookId`：

```
EntityGlobalRegistry<TGlobal, TEntity>
  ├── TGlobal[] _templates
  ├── MethodSlot[] _byHook        // 下标 = (int)HookId → 模板下标数组
  └── per-entity 缓存（仅当有 InstancePerEntity / AppliesToEntity）
```

`For(entity, HookId.PreAI)` = 一次数组索引；实体级过滤结果按实体缓存，不在每帧重建。

### 与旧语义的对齐

| 旧 | 新 |
|---|---|
| `mod.Plan.Has("PreAI")` | `mod.Plan.Has(HookId.PreAI)` |
| `GlobalItemRegistry.For(item, "UseStyle")` | `GlobalItemRegistry.For(item, HookId.UseStyle)` |
| `AnyWith("PreAI","AI","PostAI")` | `AnyWith(HookId.PreAI, HookId.AI, HookId.PostAI)` |

语义不变，只换索引形式。作者 Mod 侧不受影响：作者覆写方法，不写字符串。

## 失败语义

* 单个订阅者抛异常：记一条（同标签去重），继续下一个订阅者；游戏不受影响。
* 空通道：直接返回，不进入 try 块。

## 扩展方式

1. 在 `HookId` 末尾追加一项；
2. 在 `Mod`（或对应内容基类）上加 `virtual` 方法；
3. `HookScan` 自动把覆写计入位集；
4. 在分派点用 `bus.Channel(HookId.Xxx)` 取订阅者。

不需要注册、不需要特性、不需要改枚举以外的任何表。
