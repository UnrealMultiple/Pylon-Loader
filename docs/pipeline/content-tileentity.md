# 内容管线：方块实体（TileEntity）

## 1. 职责

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `TileEntity.PerformUpdates` | `PreGlobalUpdate`（全部实例）→ 原版更新 → `Update`（只要覆写过）→ `PostGlobalUpdate`（全部实例） |
| `WorldGen.clearWorld` | 清空实例表（不调作者代码） |
| `WorldFile.LoadWorld` | 逐条 `LoadData`；`IsTileValidForEntity` 不成立就丢弃该条 |
| `WorldFile.InternalSaveWorld` | 逐条 `SaveData` 写入侧车存档 `<世界>.wld.tileentities.json` |
| `NetMessage.SendSection` | 新玩家进到某一段世界时，把这一段里的实体补发给他 |
| Mod 通道的子通道（自己的种类字节） | 实体广播 / 摘除广播 / 放置请求 / 摘除请求 → `NetReceive`、`OnNetPlace`、`OnKill` |
| 作者调 `Place` / `Kill` | 单人：本地放/摘；服务器：放/摘后广播；客户端：只发请求，等服务器广播 |
| `WorldGen.PlaceTile` 之后 | `ModTile.TileEntity` 指了实体名就建那一个；否则按 `IsTileValidForEntity` 的老规则建（客户端不建，等广播） |
| 放置完成（原 `PlacementHook` 的时机） | `ModTile.Hook_AfterPlacement(i, j, type, style, direction, alternate)`，默认 -1 |

**已接**：静态 `ByID` / `ByPosition`（`ModTileEntity.ByID` / `ByPosition`，返回内部表的只读视图，见对应小节）。

**不接**（见文末「这一版没接」）：`TileEntity.Place` / `Kill` / `OnPlaced` / `OnRemoved` 的静态入口、`TileEntitiesManager` 注册。`PlacementHook` 的能力改以 `ModTile.TileEntity` 声明 + `Hook_AfterPlacement` 回调接上（见没接第 1 条）。

已核对（铁律 1）：存档（`LoadWorld` / `InternalSaveWorld` / `SendSection`）与网络（放置广播 / 摘除广播 / 放置请求 / 摘除请求）均与参考实现同集；参考里多出的那条「`NetMessage.SendData` 拦 `TileEntitySharing`（86）按 ID 重发实体」在本构建没有对应入口——`dump.cs` 里 `NetMessage.SendData` 的 86 号分支不存在，收实体的路径已经由子通道的 `ActionEntity` 覆盖。

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

### 2.3 实例绑定：模板克隆，不反射

`ModTileEntity` 加一个 `protected internal virtual ModTileEntity NewInstance()`，默认 `MemberwiseClone()`。放进世界的新实体就是模板的浅拷贝，再写 `ID` 与 `Position`。`MemberwiseClone` 是实例方法，不碰 `GetMethod` / `Activator`；作者有可变引用字段（列表、缓存）时覆写它给一份干净的。

实例表是两张字典：`Dictionary<int, ModTileEntity>`（ID → 实例）与位置键 → 实例。位置键按 `(x & 0xFFFF) | ((y & 0xFFFF) << 16)` 折成 `int`，与游戏的 `GetKey` 同构。ID 从 `0x40000000` 起，与游戏自己的 ID 空间不重叠。

另存一份 `List<ModTileEntity>` 作为更新顺序（字典的枚举顺序在热路径上不可靠，也不允许每帧 `ToArray`）。

### 2.4 每帧分派

`PerformUpdates` 的钩子里按"列表下标 + 存活判定"遍历：

* 前置：全部实例 `PreGlobalUpdate`；
* 原版：`orig()`（原版实体的更新一次不少）；
* 主体：`NeedsUpdate` 的实例 `Update`，但调用前先确认它还在表里（作者可能在 `Update` 里把自己摘了）；
* 收尾：全部实例 `PostGlobalUpdate`。

遍历不做快照：第 `n` 个实例把自己摘掉会让后面的元素前移一格，当帧最多漏调一个实例的后续回调，下一帧自然恢复。这条写进第 2 节，不装成"绝对不会漏"。

### 2.5 存档

侧车存档 `<世界路径>.wld.tileentities.json`，形如 `{"ents":[{"t":"Mod/类名","x":12,"y":34,"d":{...}}]}`。读档时 `t` 找不到模板 → 丢弃；`IsTileValidForEntity` 返回 false 或抛错 → 丢弃；`LoadData` 抛错 → 记一条，实例保留（与"档里记的东西还在、只是作者的初始化没跑完"一致）。

**为什么另开文件**：`ModLifecycle` 的 `<世界>.wld.bl.json` 是按 Mod 分键的 `SaveWorldData`，方块实体是**跨 Mod 的位置数据**；混在一个文件里，删掉某个 mod 后留下的孤儿条目会和"某 mod 的世界数据"互相牵连。

### 2.6 网络：借 Mod 通道的子通道，权威在服务器

**通道**：`NetPipeline` 已占用消息号 `0xB17E`，前 4 个种类字节是 Mod 包 / 世界 / NPC / 弹幕。本管线向它 `OpenChannel` 要一个种类字节（`5`），分配顺序 = `LoaderRegistry` 的模块顺序，跨设备一致；收包由 `NetPipeline` 的默认分支回调回来。通道本身仍只有一条消息号，载荷靠 `NetCodec` 的值序列化。

**四个动作**（第一字节）：

| 动作 | 方向 | 载荷 | 处理 |
|---|---|---|---|
| `ActionEntity` | 服务器 → 客户端 | ID、左上角、类型键、作者的 `NetSend` 值 | 同 ID 已存在就只补数据，没有就按服务器给的 ID 建实例 |
| `ActionKilled` | 服务器 → 客户端 | ID | 从表里除名（不调 `OnKill`，那是服务器侧语义） |
| `ActionRequestPlace` | 客户端 → 服务器 | 类型键、坐标 | 服务器用 `IsTileValidForEntity` 复核后放置、调 `OnNetPlace`、广播 |
| `ActionRequestKill` | 客户端 → 服务器 | 类型键、坐标 | 服务器复核位置上的类型后摘除、广播 |

**权威**：客户端调 `Place` 只发请求（返回 `-1`），因为"这一格还是不是这个实体"的判定必须由服务器做；否则一个伪造包就能在任意格子凭空生成实体。服务器侧 `Place` 成功后才广播，客户端永远是被动接受服务器分配的 ID。

**补发**：`SendSection` 的 postfix 按区块（200×150 格）把落在这一段的实体单独发给这个客户端——否则新玩家要等到实体下次发数据才知道世界上有它。载荷上限由 `NetPipeline.End` 统一卡在 65000 字节。

### 2.7 状态归属

* `TileEntityTable` / `TileEntityBinding`：静态表——它们按游戏侧编号（类型号、实体 ID、格子坐标）索引，生命周期与进程一致，和 `WallTable` / `TileTable` 同一个约定；
* `TileEntityPipeline` 实例：分派去重集合（`_reported`）、子通道种类字节（`_kind`）与通道对象（`_net`）。

### 2.6 跨实例查表

两张内部表（ID → 实例、左上角格 → 实例）随 `Place` / `Kill` / 读档一起维护，作者侧以只读视图暴露：

```csharp
foreach (var (id, entity) in ModTileEntity.ByID) { }
if (ModTileEntity.ByPosition.TryGetValue(new Point16(x, y), out var here)) { }
```

**位置键从打包 int 换成 `Point16`**：原先用 `(x & 0xFFFF) | (y & 0xFFFF) << 16` 打包，省一点内存但作者拿到手没法用；`Point16` 本身就是游戏的位置类型，哈希也现成。

**为什么给只读视图而不是可写字典**：表的一致性由管线负责（放置、拆除、读档、网络都要同步改两张表）；让作者直接增删会造出"表里有、世界里没有"的幽灵实体。

## 3. 取舍理由

* **为什么不用游戏的 `TileEntitiesManager`？** 它是"类型字节 + 工厂"闭集：`TileEntity` 是 abstract，`TileEntityType<T>` 是泛型闭类，`GenerateInstance` 只认 `RegisterAll` 里那几个原版类型；`type` 字段只有 `byte`。塞不进 mod 类型，硬塞就得改游戏静态字段，会连带影响原版实体的读写档。所以实体表按参考实现的思路自持，但**不用它的反射造实例**。
* **为什么用 `MemberwiseClone` 而不是 `Activator`？** 零反射是硬约束；`NewInstance` 是 `protected internal virtual`——内部可调、作者可覆写，两种需求一个方法解决，且跨程序集编译期就检查得到。作者的类如果没有无参构造也能用（不实例化新对象）。
* **为什么 `Refresh` 里发号？** `Install()` 在每个 Mod 注册后只跑一次（`LoaderRegistry` 的游标），而内容是在各 Mod 注册时陆续出现的；`Refresh` 是"每次注册之后必然跑一遍"的唯一位置。发号必须早于作者的 `SetupContent`，否则 `ModContent.GetInstance<T>().ID` 在安装期是 0。
* **为什么网络走 `NetPipeline` 的子通道，而不是自己再抢一个消息号？** 接收侧只有一个可靠的入口（`NetManager.Read`）。第二个消息号意味着第二份"这包是不是我的"的判定与第二套缓冲，出一次读半截就会连着别人的包一起错位。子通道只占 1 个字节，接收、长度上限、异常兜底全复用现成的那一套。
* **为什么客户端 `Place` 返回 `-1` 而不是"先本地放、再让服务器纠正"？** ID 的权威在服务器；先本地放会造出两个不同 ID 的同位实体，等服务器广播回来还得"按位置合并"，规则任何一个分支写错就是幽灵实体。
* **为什么 `ActionKilled` 在客户端不调 `OnKill`？** `OnKill` 的语义是"被 `Kill` 摘掉之前"，作者会在这里掉东西、发数据包；在客户端再跑一次会让这些东西双份。服务器调一次，客户端只除名。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| `PreGlobalUpdate` / `Update` / `PostGlobalUpdate` 抛错 | 记一条（同类型同钩子只报一次），该实例当帧跳过，其它实例照常 |
| `OnKill` 抛错 | 记一条，实体照样从表里摘掉（否则表里留下"已经没了"的条目） |
| `SaveData` 抛错 | 记一条，该条目写进去但不带 `d` 键；下次读档 `LoadData` 收到空 `TagCompound` |
| `LoadData` 抛错 | 记一条，实例保留（位置与类型是对的） |
| `IsTileValidForEntity` 抛错 | 按 false 处理，该条丢弃 |
| `NetSend` 抛错 | 记一条，这一包仍会发（作者什么都没写，客户端只拿到位置与类型）；`NetReceive` 抛错同理，实例照建、字段是默认值 |
| 客户端伪造放置请求 | 服务器用 `IsTileValidForEntity` 复核，不通过就静默丢弃 |
| 客户端收到未知类型键（没装那个 mod） | 跳过，不建实例 |
| 包超过 65000 字节 | `NetPipeline` 丢弃并记一次，连接不受影响 |
| `Update` 里把别的实例摘掉 | 当帧可能漏调一次，下一帧恢复（见 2.4） |
| `NetUpdateInterval` 设成负数 | 当成 0（不定时） |
| 定时同步的实体很多 | 按 `ID % 间隔` 错开在同一间隔内的不同帧发出，不会挤在一帧 |
| 世界里没有 mod 实体 | `PerformUpdates` 的钩子直接 `orig()`，零额外开销 |
| 存档文件损坏 | `TagJson` 记一次，按"没有存档"处理，不阻塞进世界 |

## 5. 扩展方式

新增一个方块实体钩子：`HookId` 末尾追加 → `ModTileEntity` 加同名 `virtual` → 在 `TileEntityPipeline` 的对应入口分派（每帧类挂在 `Tick`，放置 / 摘除类挂在 `Place` / `Kill`，网络类挂在 `Receive` 的动作分支）。三类内容（模板、实例、位集）都不用改结构。

新增一条同步动作：在 `TileEntityPipeline` 常量里加一个动作字节 → `Receive` 加一个分支 → 服务器侧加发送方法。种类字节不用动。

新增"作者声明"类需求（例如"这个实体要不要进存档"）时，加 `protected virtual` 声明方法，不引入反射。

## 6. 与其它管线的边界

* **方块管线**：方块类型与掉落归 `TilePipeline`；本管线只认 `ModTileEntity` 的模板，不写 `Main.tile` 的任何字节。
* **Pylon 管线**：`TEModdedPylon` 是本管线的普通模板，放置与摘除由 Pylon 管线通过 `ModTileEntity.Place` / `Kill` 触发，两边不互相引用内部表。
* **网络管线**：消息号、种类字节分配、长度上限、收包入口归 `NetPipeline`；本管线只写自己的动作字节与载荷。**不新开消息号**。
* **安装管线**：`SetStaticDefaults` 与 `Load` 在 `InstallPlan` 的 `Static` 阶段各调一次（`TileEntityPipeline.Build`），表扩容与贴图上传仍归安装管线。

### 这一版没接

1. ~~**`PlacementHook` / `Generic_HookPostPlaceMyPlayer`**~~：**已接（改走声明式）**。核对结论：`PlacementHook` 结构体本身存在（`refs/dump.cs:109216`，`TileObjectData` 的 `HookPostPlaceMyPlayer` 等四个属性也都在），但**没有任何被调用点**（全 `dump.cs` 只搜到属性与字段定义，找不到 `.hook` 被调用的语句）——这个构建里自动放置根本不走 `PlacementHook`，玩家放块走的是 `WorldGen.PlaceTile`。因此接的是 `ModTile.TileEntity`（声明 "放下这块自动建哪个实体"，写同类名）+ `ModTile.Hook_AfterPlacement`（放完回调，对应原签名 `(i, j, type, style, direction, alternate)`）：`PlaceTile` 后置先看声明、有就建那一个（客户端不建，等服务器广播）；没有声明的回落到 "`IsTileValidForEntity` 认了就建" 的老规则。通用放置的其余入口（`HookPlaceOverride` / `HookCheckIfCanPlace`）是原版方块自己的事，mod 方块不经它们。
2. ~~**静态 `ByID` / `ByPosition` / `TryGet`**~~：**已接**——`ModTileEntity.ByID`（`int → 实例`）与 `ModTileEntity.ByPosition`（`Point16 → 实例`）返回管线内部表的只读视图，随放置/拆除实时更新；`TryGet` 用 `ByID.TryGetValue(...)` 即可，不再单开。
3. **`TileEntity.Place` / `Kill` 的静态入口**：mod 实体不在 `TileEntitiesManager` 里，这两个入口收不到自己的事件；等价能力走 `WorldGen.PlaceTile` / `KillTile` 两处（放下就建实体、拆掉就摘实体，`IsTileValidForEntity` 为真的模板才建）。
4. ~~**每帧增量同步**~~：**已接（定时同步）**——`ModTileEntity.NetUpdateInterval`（帧；0 = 不定时）声明之后，服务器在 `PerformUpdates` 的每帧分派末尾按 `帧号 % 间隔 == 实体 ID % 间隔` 错开把该实体整个重发一遍（复用实体包：`NetSend` 载荷 + 位置/类型），客户端走同一条 `ActionEntity` 接收路径。不做的是**每帧**：间隔按帧给，作者要更快就调小，但每帧广播在实体多时会把带宽吃满，所以默认 0（关）。`SendExtraAI` 那种"只发 AI 字段、不带位置"的细分包没有单开——实体包本身就小，多一条协议不如复用一条。
5. **`Hook_AfterPlacement` 的通用放置流程 / 按 Mod 卸载的粒度**：前者仍然没有调用点（自动建实体走的是 `IsTileValidForEntity`，不是它）；后者在卸载世界时整表清空（Pylon 目前不在运行期卸载 Mod）。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 新增 `protected internal virtual ModTileEntity NewInstance()` | 放进世界时为每个实体做一份实例；默认 `MemberwiseClone`。有可变引用字段的作者应覆写它 |
| 新增 `int Place(int i, int j)` / `void Kill(int i, int j)` | 单人 / 服务器：放置（返回实体 ID）与摘除（先 `OnKill`）；**客户端只发请求，`Place` 返回 -1**；越界或格占用也返回 -1 |
| 新增 `int Find(int i, int j)` / `static int CountInWorld()` | 按位置反查本类型的实体 ID（没有 = -1）与世界里的实体总数 |
| 新增 `virtual void NetSend(NetWriter)` / `virtual void NetReceive(NetReader)` | 实体同步的载荷；只写会变的东西，位置与类型由管线带 |
| 新增 `virtual int NetUpdateInterval` | 服务器每隔多少帧重发一次这个实体（0 = 不定时，默认）；放在 `Install()` 之前读一次，改值不影响已装好的世界 |
| 新增（在 `ModTile` 上） | `virtual string? TileEntity`（放下这块自动建哪个实体，写同类名；null = 不建）与 `virtual int Hook_AfterPlacement(i, j, type, style, direction, alternate)`（放完回调，默认 -1） |
| 不变 | `IsTileValidForEntity` / `Load` / `Update` / `PreGlobalUpdate` / `PostGlobalUpdate` / `OnNetPlace` / `OnKill` / `SaveData` / `LoadData` / `Hook_AfterPlacement` 签名一律不动 |
