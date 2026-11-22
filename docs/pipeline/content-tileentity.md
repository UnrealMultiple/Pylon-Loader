# 内容管线：方块实体（TileEntity）

## 1. 职责

把 `ModTileEntity` 的模板与实例管起来：注册建表、每帧更新、实体建摘（作者自理）、多人同步、侧车存档。

| 游戏时机 | 做什么 |
|---|---|
| `TileEntity.PerformUpdates` | `PreGlobalUpdate`（全部实例）→ 原版更新 → `Update`（只要覆写过）→ `PostGlobalUpdate`（全部实例） |
| `WorldGen.clearWorld` | 清空实例表（不调作者代码） |
| `WorldFile.LoadWorld` | 清表后逐条读侧车：类型键找不到模板 → 丢弃；`IsTileValidForEntity` 不成立 → 丢弃；建立实例抛错 → 报一条并丢弃；其余 `LoadData` |
| `WorldFile.InternalSaveWorld` | 逐条 `SaveData` 写入侧车存档 `<世界>.wld.tileentities.json`（云存档时跳过） |
| `NetMessage.SendSection` | 给某客户端发区块时，把落在这一段里的实体单独补发给他 |
| Mod 通道的子通道 | 四个动作：实体广播 / 摘除广播 / 放置请求 / 摘除请求（见 2.7） |
| 作者调 `Place` / `Kill` | 单人：本地放 / 摘；服务器：放 / 摘后广播；客户端：只发请求，等服务器广播。放 / 拆方块后实体建不建、摘不摘，完全由作者在 `ModTile.PlaceInWorld` / `KillTile` / `KillMultiTile` 里自己调（见 2.6） |

**已接**：`ModTileEntity.ByID` / `ByPosition`（管线内部表的只读视图）与 `TileEntityTml.TryGet`（见 2.9）。

**不接**：`TileEntity.Place` / `Kill` / `OnPlaced` / `OnRemoved` 的静态入口、`TileEntitiesManager` 注册、`NetMessage.SendData` 86 号（TileEntitySharing）按 ID 重发——本构建的 `SendData` 没有 86 号分支（`dump.cs` 里无对应语句），收实体已由子通道的 `ActionEntity` 覆盖（见「这一版没接」）。

## 2. 我们的设计

### 2.1 三个文件

`Pipelines/TileEntity/` 下三个文件，一个职责一个文件：`TileEntityPipeline.cs`（钩子、每帧 / 网络分派、读档存档）、`TileEntityTable.cs`（模板按类型号密集索引 + 每类型位集）、`TileEntityBinding.cs`（ID / 位置 → 实例、ID 发号）。

### 2.2 类型表：数组下标就是类型号

```csharp
internal struct TileEntityMeta
{
    public HookMask Masks;      // 这个类型覆写了哪些方块实体钩子
    public bool NeedsUpdate;    // 有没有覆写 Update
}
```

`ModTileEntity?[]` 与 `TileEntityMeta[]` 并列，下标 = `ModTileEntity.Type`（注册序，0 起）。发号在 `Refresh` 里做：每次 Mod 注册完，把新出现、还没发号的模板依次接到表尾。

**位集必须重算**：`RegistrationPipeline` 给每个内容算的是相对 `ModType` 的位集，那样"继承来的 `Update` 空实现"也会被算成覆写。建表时用 `HookScan.Of(类型, typeof(ModTileEntity))` 重新算一遍并写回 `Plan`，`RequiresUpdates` 就跟着正确了。

### 2.3 实例绑定：构造式建实例

放进世界的新实例用 `Activator.CreateInstance(类型, true)` 新造：构造器与字段初始化器照常执行，再显式补 `Mod` / `Type` / `ID` / `Position`。每类型首次调用做一次构造器解析并缓存创建委托，之后每次约百纳秒级。

构造式让"每个实体一份状态"从根上成立：每实例一个可变引用字段（`private readonly List<...> items = new();`）直接可用，实例之间、实例与模板之间不共享任何引用字段；新实体拿到的是字段默认值，不是模板当前值的快照。只写了带参构造的类会在首次建立实例时抛 `MissingMethodException`——明错，按第 4 节的失败语义上报。

实例表是两张字典：`Dictionary<int, ModTileEntity>`（ID → 实例）与 `Dictionary<Point16, ModTileEntity>`（左上角格 → 实例）。位置键直接用 `Point16`——游戏自己的位置类型，哈希现成，作者拿到的键和自己传出去的类型一致。ID 从 `0x40000000` 起，与游戏自己的 ID 空间不重叠。

另存一份 `List<ModTileEntity>` 作为更新顺序（字典的枚举顺序在热路径上不可靠，也不允许每帧 `ToArray`）。

### 2.4 每帧分派

`PerformUpdates` 的钩子里按"列表下标 + 存活判定"遍历：

* 前置：全部实例 `PreGlobalUpdate`；
* 原版：`orig()`（原版实体的更新一次不少）；
* 主体：`NeedsUpdate` 的实例 `Update`，但调用前先确认它还在表里（作者可能在 `Update` 里把自己摘了）；
* 收尾：全部实例 `PostGlobalUpdate`。

遍历不做快照：第 `n` 个实例把自己摘掉会让后面的元素前移一格，当帧最多漏调一个实例的后续回调，下一帧自然恢复。这条写进第 2 节，不装成"绝对不会漏"。

### 2.5 存档

侧车存档 `<世界路径>.wld.tileentities.json`，形如 `{"ents":[{"t":"Mod/类名","x":12,"y":34,"d":{...}}]}`。读档时 `t` 找不到模板 → 丢弃；`IsTileValidForEntity` 返回 false 或抛错 → 丢弃；建立实例抛错（构造器缺失、初始化器炸了）→ 报一条、丢弃该条继续；`LoadData` 抛错 → 记一条，实例保留（与"档里记的东西还在、只是作者的初始化没跑完"一致）。

**为什么另开文件**：`ModLifecycle` 的 `<世界>.wld.bl.json` 是按 Mod 分键的 `SaveWorldData`，方块实体是**跨 Mod 的位置数据**；混在一个文件里，删掉某个 mod 后留下的孤儿条目会和"某 mod 的世界数据"互相牵连。独立文件把实体数据的格式、校验与清理从 per-mod 世界数据里解耦，读档对"键找不到模板"的条目按丢弃处理，不让孤儿条目牵连其它 mod 的数据。

### 2.6 实体建摘：作者自理

放下 / 拆掉方块后，实体建不建、摘不摘完全由作者决定——在自己的 `ModTile` 钩子里调 `ModTileEntity.Place` / `Kill`。管线不在 `WorldGen.PlaceTile` / `KillTile` 后替作者建摘任何实体。

* 放置：1×1 简单方块在 `PlaceInWorld` 里调 `Place(i, j)`；多格家具的放置只认左上角——按 `frameX % CoordinateFullWidth == 0 && frameY == 0` 判定，或按 `data.Origin` 折回后再调；
* 摘除：1×1 在 `KillTile` 里让 `fail` / `effectOnly` 早退、只在真拆时调 `Kill(i, j)`；多格家具用 `KillMultiTile`（整件毁坏才调一次，坐标即左上角）；
* mod 代码直接调 `WorldGen.PlaceTile`（非玩家放置）不派发 `PlaceInWorld`——要建实体就显式调 `Place`；
* 幂等：同位已有实体 `Place` 返回 -1，实体已不在 `Kill` 是无操作——单机 / 服务器 / 客户端各自把联动跑一遍也不会双建双摘（服务器先建则客户端请求落空，先处理请求则服务器自己的联动落空；摘除侧同构）；
* 作者"放完之后还要做的事"写在 `PlaceInWorld` 里 `Place()` 之后；
* `IsTileValidForEntity` 仍必写：读档丢弃与服务器放置复核都靠它。

1×1 简单方块的完整形态：

```csharp
public override void PlaceInWorld(int i, int j, Item item) =>
    ModContent.GetInstance<MyEntity>().Place(i, j);

public override void KillTile(int i, int j, ref bool fail, ref bool effectOnly, ref bool noItem)
{
    if (fail || effectOnly) return;
    ModContent.GetInstance<MyEntity>().Kill(i, j);
}
```

多格家具的摘除：

```csharp
public override void KillMultiTile(int i, int j, int frameX, int frameY) =>
    ModContent.GetInstance<MyEntity>().Kill(i, j);
```

**从"随方块自动建摘"迁移**：只实现 `IsTileValidForEntity`、依赖管线自动建 / 自动摘的写法不再受支持——升级后这类 mod 的实体不再随放块 / 拆块出现或消失（拆方块后实体残留）。按上面的形态把联动搬进 `PlaceInWorld` / `KillTile` / `KillMultiTile` 即可；已经在自己钩子里调 `Place` / `Kill` 的 mod 无需任何改动。

### 2.7 网络：借 Mod 通道的子通道，权威在服务器

**通道**：`NetPipeline` 已占用消息号 `0xB17E`，1–6 号种类字节留给内核（Mod 包 / 世界 / NPC / 弹幕 / 物品附属 / 元数据）。本管线向它 `OpenChannel` 分得其后第一个可用字节——分配顺序 = 模块安装顺序，跨设备一致；收包由 `NetPipeline` 的默认分支回调回来。通道本身仍只有一条消息号，载荷靠 `NetCodec` 的值序列化。

**四个动作**（第一字节）：

| 动作 | 方向 | 载荷 | 处理 |
|---|---|---|---|
| `ActionEntity` | 服务器 → 客户端 | ID、左上角、类型键、作者的 `NetSend` 值 | 同 ID 已存在就只补数据，没有就按服务器给的 ID 建实例 |
| `ActionKilled` | 服务器 → 客户端 | ID | 从表里除名（不调 `OnKill`，那是服务器侧语义） |
| `ActionRequestPlace` | 客户端 → 服务器 | 类型键、坐标 | 服务器用 `IsTileValidForEntity` 复核后放置、调 `OnNetPlace`、广播 |
| `ActionRequestKill` | 客户端 → 服务器 | 类型键、坐标 | 服务器复核位置上的类型后摘除、广播 |

**广播点**：实体出现或消失的每一条路径都以广播收尾——作者调 `Place` / `Kill`（服务器侧）与客户端请求的服务器处理（`ServerPlace` / `ServerKill`）。单机不广播，客户端不出广播包。

**权威**：客户端调 `Place` 只发请求（返回 `-1`），因为"这一格还是不是这个实体"的判定必须由服务器做；否则一个伪造包就能在任意格子凭空生成实体。服务器侧 `Place` 成功后才广播，客户端永远是被动接受服务器分配的 ID。

**补发**：`SendSection` 的 postfix 按区块（200×150 格）把落在这一段的实体单独发给这个客户端——否则新玩家要等到实体下次发数据才知道世界上有它。载荷上限由 `NetPipeline.End` 统一卡在 65000 字节。

### 2.8 状态归属

* `TileEntityTable` / `TileEntityBinding`：静态表——它们按游戏侧编号（类型号、实体 ID、格子坐标）索引，生命周期与进程一致，和 `WallTable` / `TileTable` 同一个约定；
* `TileEntityPipeline` 实例：分派去重集合（`_reported`）、子通道种类字节（`_kind`）与通道对象（`_net`）。

### 2.9 只读视图与 TryGet

两张内部表（ID → 实体、左上角格 → 实体）随放置 / 摘除 / 读档 / 网络一起维护，作者侧以只读视图暴露：

```csharp
foreach (var (id, entity) in ModTileEntity.ByID) { }
if (ModTileEntity.ByPosition.TryGetValue(new Point16(x, y), out var here)) { }
```

按格取实体走 `TileEntityTml.TryGet<T>`（自动折算到物体左上角，类型不符取不到）。

**为什么给只读视图而不是可写字典**：表的一致性由管线负责（放置、拆除、读档、网络都要同步改两张表）；让作者直接增删会造出"表里有、世界里没有"的幽灵实体。

## 3. 取舍理由

* **为什么不用游戏的 `TileEntitiesManager`？** 它是"类型字节 + 工厂"闭集：`TileEntity` 是 abstract，`TileEntityType<T>` 是泛型闭类，`GenerateInstance` 只认 `RegisterAll` 里那几个原版类型；`type` 字段只有 `byte`。塞不进 mod 类型，硬塞就得改游戏静态字段，会连带影响原版实体的读写档。所以实体表自持，不注册进游戏闭集。
* **为什么实体建摘是作者自理？** "这格还该不该有这个实体"只有作者说得清——家具朝向、多格形状、放置中间态都是 mod 自己的规则；管线代劳只能对全部模板轮询猜测，还把"拆方块 = 实体必死"定成铁律。作者自理后，建收敛到 `Place` 系（自带广播）、摘收敛到 `Kill` 系（自带广播）+ 收包除名，管线不猜时机、不留兜底，也免掉了每次成功放块对全部模板各问一遍 `IsTileValidForEntity` 的开销。
* **为什么 `Refresh` 里发号？** `Install()` 在每个 Mod 注册后只跑一次（`LoaderRegistry` 的游标），而内容是在各 Mod 注册时陆续出现的；`Refresh` 是"每次注册之后必然跑一遍"的唯一位置。发号必须早于作者的 `SetupContent`，否则 `ModContent.GetInstance<T>().ID` 在安装期是 0。
* **为什么网络走 `NetPipeline` 的子通道，而不是自己再抢一个消息号？** 接收侧只有一个可靠的入口（`NetManager.Read`）。第二个消息号意味着第二份"这包是不是我的"的判定与第二套缓冲，出一次读半截就会连着别人的包一起错位。子通道只占 1 个字节，接收、长度上限、异常兜底全复用现成的那一套。
* **为什么客户端 `Place` 返回 `-1` 而不是"先本地放、再让服务器纠正"？** ID 的权威在服务器；先本地放会造出两个不同 ID 的同位实体，等服务器广播回来还得"按位置合并"，规则任何一个分支写错就是幽灵实体。
* **为什么客户端一律不调 `OnKill`？** `OnKill` 的语义是"被 `Kill` 摘掉之前"，作者会在这里掉东西、发数据包；在客户端再跑一次会让这些东西双份。服务器调一次，客户端只除名——`ActionKilled` 的收包处理遵守这条。
* **为什么造实例用 `Activator` 而不是克隆模板？** 构造器与字段初始化器照常执行，"每个实体一份干净状态"不需要作者做任何事；克隆模板会把模板当前字段值带进每个新实例，可变引用字段一 clone 就成了全体共享。反射成本按类型缓存创建委托后是百纳秒级，远低于一次放置的世界操作；这条反射是纯托管层，与 il2cpp 类型系统无关。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| `PreGlobalUpdate` / `Update` / `PostGlobalUpdate` 抛错 | 记一条（同类型同钩子只报一次），该实例当帧跳过，其它实例照常 |
| `OnKill` 抛错 | 记一条，实体照样从表里摘掉（否则表里留下"已经没了"的条目） |
| `SaveData` 抛错 | 记一条，该条目写进去但不带 `d` 键；下次读档 `LoadData` 收到空 `TagCompound` |
| `LoadData` 抛错 | 记一条，实例保留（位置与类型是对的） |
| `IsTileValidForEntity` 抛错 | 按 false 处理：读档与服务器放置复核丢弃该条 |
| 建立实例抛错（只写了带参构造、初始化器炸了） | 读档：报一条、丢弃该条继续，其它条目不受影响；放置（作者 API / 请求路径）：异常沿调用点的兜底上报，实体不建 |
| `NetSend` 抛错 | 记一条，这一包仍会发（作者什么都没写，客户端只拿到位置与类型）；`NetReceive` 抛错同理，实例照建、字段是默认值 |
| 客户端伪造放置请求 | 服务器用 `IsTileValidForEntity` 复核，不通过就静默丢弃 |
| 客户端收到未知类型键（没装那个 mod） | 跳过，不建实例 |
| 摘除广播到达前实体已被本地除名 | 幂等，按 ID 查不到就无操作 |
| 包超过 65000 字节 | `NetPipeline` 丢弃并记一次，连接不受影响 |
| `Update` 里把别的实例摘掉 | 当帧可能漏调一次，下一帧恢复（见 2.4） |
| 世界里没有 mod 实体 | `PerformUpdates` 的钩子直接 `orig()`，零额外开销 |
| 存档文件损坏 | `TagJson` 记一次，按"没有存档"处理，不阻塞进世界 |

## 5. 扩展方式

新增一个方块实体钩子：`HookId` 末尾追加 → `ModTileEntity` 加同名 `virtual` → 在 `TileEntityPipeline` 的对应入口分派（每帧类挂在 `Tick`，放置 / 摘除类挂在 `Place` / `Kill`，网络类挂在 `Receive` 的动作分支）。三类内容（模板、实例、位集）都不用改结构。

新增一条同步动作：在 `TileEntityPipeline` 常量里加一个动作字节 → `Receive` 加一个分支 → 服务器侧加发送方法。种类字节不用动。

新增"作者声明"类需求（例如"这个实体要不要进存档"）时，加 `protected virtual` 声明方法。

## 6. 与其它管线的边界

* **方块管线**：方块类型与掉落归 `TilePipeline`；本管线只认 `ModTileEntity` 的模板，不写 `Main.tile` 的任何字节。实体建摘所需的放置 / 摘除时机由方块管线的 `PlaceInWorld` / `KillTile` / `KillMultiTile` 派发提供，作者在那里面调本管线的 `Place` / `Kill`。
* **晶塔管线**：`TEModdedPylon` 是本管线的普通模板，放置与摘除由晶塔侧通过 `ModTileEntity.Place` / `Kill` 触发，两边不互相引用内部表。
* **网络管线**：消息号、种类字节分配、长度上限、收包入口归 `NetPipeline`；本管线只写自己的动作字节与载荷。**不新开消息号**。
* **安装管线**：`SetStaticDefaults` 与 `Load` 在 `InstallPlan` 的 `Static` 阶段各调一次（`TileEntityPipeline.Build`），表扩容与贴图上传仍归安装管线。

### 这一版没接

1. **`PlacementHook` 接线（`Hook_AfterPlacement` 的派发点）**：`ModTileEntity.Hook_AfterPlacement` 保留为放置入口契约——作者折算出左上角后自己调 `Place` 建实体——但本构建没有 PlacementHook 调用点（`dump.cs` 里 `HookPostPlaceMyPlayer` 只有属性声明），管线不派发它。实体随放置建立由 2.6 的作者自理承担；接线后它作为放置入口派发，建实体依旧不派发它（回调里再调 `Place` 会无限递归）。
2. **`TileEntity.Place` / `Kill` 的静态入口**：mod 实体不在 `TileEntitiesManager` 里，这两个入口收不到自己的事件；等价能力是作者自理的联动 + `ModTileEntity.Place` / `Kill`（见 2.6）。
3. **`NetMessage.SendData` 86 号（TileEntitySharing）按 ID 重发**：本构建的 `SendData` 没有 86 号分支，原版对未知分支静默；实体的即时同步由子通道广播与 `SendSection` 补发覆盖。
4. **按 Mod 卸载的粒度**：卸载世界时整表清空（Pylon 目前不在运行期卸载 Mod）。

### 作者 API 一览

| 成员 | 说明 |
|---|---|
| `int Place(int i, int j)` / `void Kill(int i, int j)` | 单人 / 服务器：放置（返回实体 ID）与摘除（先 `OnKill`）；客户端只发请求，`Place` 返回 -1；越界或格占用也返回 -1。建 / 摘的时机由作者在方块钩子里调（见 2.6） |
| `int Find(int i, int j)` / `static int CountInWorld()` | 按位置反查本类型的实体 ID（没有 = -1）与世界里的实体总数 |
| `static IReadOnlyDictionary<int, ModTileEntity> ByID` / `ByPosition` | 管线内部表的只读视图，随放置 / 摘除 / 读档 / 网络实时更新 |
| `TileEntityTml.TryGet<T>(i, j, out T)` | 按格取 mod 实体（自动折算左上角）；类型不符取不到 |
| `virtual void NetSend(NetWriter)` / `virtual void NetReceive(NetReader)` | 实体同步的载荷；只写会变的东西，位置与类型由管线带 |
| `virtual int Hook_AfterPlacement(i, j, type, style, direction, alternate)` | PlacementHook 的放置入口契约：作者折算左上角后自己调 `Place`；建实体不派发它，本构建尚未接线（见「这一版没接 1」） |
| `IsTileValidForEntity` / `Load` / `Update` / `PreGlobalUpdate` / `PostGlobalUpdate` / `OnNetPlace` / `OnKill` / `SaveData` / `LoadData` | 签名与语义见 `ModTileEntity` 的 XML 注释，管线按 1 / 2.4 / 2.7 的时机调用 |
