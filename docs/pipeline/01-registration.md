# 管线一：内容注册（Registration）

## 职责

把"一个 Mod 程序集里写了哪些内容"变成"游戏里每个内容拿到哪个 ID、由谁持有"。

输入：已实例化的 `Mod` 列表（按加载顺序）。
输出：每个 `ModType` 拿到确定的 `Type`（游戏 ID），并进入可按 ID / 按类型 / 按全名反查的表。

不做的事：碰游戏类型表、上传贴图、调用任何 `SetStaticDefaults`。注册只负责"占号 + 建索引"，全部在游戏线程之外不成立的时机也安全。

## 原理

Terraria 的内容 ID 是数组下标：原版内容占 `[0, Count)`，自定义内容只能接在后面。因此"发号"= 在一个种类内分配一段连续整数，并且**必须跨设备一致**——存档、网络包、配方都用这个整数做引用，两台设备分配出不同的号就会互相读错。

保证一致的两条规则：

1. **排序键固定**：`(Mod 加载顺序, 种类, 类型全名序数)`。三者都与运行时环境无关。
2. **不许并发**：注册在单线程上按上面的顺序串行发号，号段按种类独立。

有了这两条，号段分配是纯函数：给定一组 Mod 及其类型集合，分配结果唯一。

## 设计

### 数据形状

```
ContentRegistry                       （实例，生命周期同 LoaderHost）
├── ContentSlotTable[]  _tables       按 ContentKind 索引，每个是一段稠密槽位
│     └── ModType?[]   _slots         下标 = Type - KindBase
├── ContentDescriptor[] _descriptors  下标 = 全局序号，存 (Kind, Type, ModIndex, Flags)
├── Dictionary<string,int> _byName    全名（mod/名字）→ 全局序号
└── Dictionary<Type,int>   _byClass   类型 → 全局序号
```

* `ContentDescriptor` 是 `readonly struct`：`Kind`(byte)、`Flags`(byte)、`Type`(int)、`ModIndex`(int)、`Index`(int)。查询 `ref readonly` 返回，不装箱、不分配。
* 槽表按 `Type - Base` 直接索引 → `ref ModType?`。热路径（每个实体、每帧）只做一次减法与一次数组访问。
* 名字与类型两份字典只服务加载期与 UI，不在每帧路径上。

### 发号流程

1. 收集：反射扫程序集，取 `ModType` 派生类，按全名序数排序；`ModPlayer` 单独收集（不占号）。
2. 校验：无参构造、是否 abstract、是否 `IsLoadingEnabled`；不合格只跳过该类型并记日志，不中断整批。
3. 实例化并注入 `Mod`。
4. 扫描覆写：`HookScan` 生成 `HookMask`（见管线三）。
5. 发号：按排序键逐类型取 `KindBase + cursor[kind]++`，写描述符、写槽表、写索引。
6. 广播：`Global*` 进各自的注册表（`GlobalItemRegistry` 等），不占号。

### 与游戏基表的关系

每种内容的起始号来自游戏当次运行的原版数量（`VanillaCounts` / `MountLoader.BaseCount()` / `PrefixLoader.BaseCount()`）。基表长度不属于注册管线的职责，注册只问"现在起点是多少"。

## 扩展方式

新增一种内容：

1. 在 `ContentKind` 里加一项（枚举顺序即发号顺序，决定跨 Mod 的 ID 布局）；
2. 在 `ContentKindTable` 里给它登记基表起点与槽表类型；
3. 写对应的 `ModType` 派生基类；
4. 管线四里给它加安装步骤。

注册管线本身不需要改动——它对种类只认"顺序 + 基表长度"两件事。

## 失败语义

* 单个类型构造失败：记录 `(Mod, 类型全名, 异常摘要)`，跳过；同批其他类型照常占号。
* 号段越界（`Type > short.MaxValue`）：停用该类型，日志给出种类与当前游标。
* 同一全名重复注册：后者不覆盖前者，记一条冲突日志。
