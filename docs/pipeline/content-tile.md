# 内容管线：方块（Tile）

## 1. 职责

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `WorldGen.CanKillTile` / `CanKillTile_1` | `CanKillTile`（镐子挖不挖得动） |
| `WorldGen.KillTile` | `KillTile`（可否决、可只播效果、可不掉东西）、`KillMultiTile` |
| `WorldGen.KillTile_GetTileDustAmount` | `NumDust` |
| `WorldGen.KillTile_MakeTileDust` | `CreateDust`（每粒尘土，可换类型/不出） |
| `WorldGen.KillTile_DropItems` | `CanDrop` + `GetItemDrops` + `ItemDrop`／`RegisterItemDrop` 的掉落表 |
| `WorldGen.TileFrame` | `TileFrame`（边框重算，可否决） |
| `Wiring.HitWireSingle` | `HitWire` |
| `Player.AdjTiles` | `AdjTiles`（这块也算制作站） |
| `Player.TileInteractionsUse` | `RightClick`（返回 true = 已处理）、`HasSmartInteract` |
| `Tile_PC.slope_1` | `Slope`（锤子敲，false = 不变） |

**不接**（本版没有逐格的游戏入口，见第 2 节）：`PlaceInWorld`、`ModifyLight`、`AnimateTile`／`AnimateIndividualTile`、`SetDrawPositions`／`SetSpriteEffects`／`PreDraw`／`PostDraw`／`DrawEffects`／`SpecialDraw`／`EmitParticles`、`NearbyEffects`、`RandomUpdate`、`MouseOver`／`MouseOverFar`、`KillSound`、`CanExplode`、`GrowSapling`、`ModifySittingTargetInfo`／`ModifySleepingTargetInfo`、箱子四个钩子。

## 2. 我们的设计

### 2.1 两个文件

```
Pipelines/Tile/
  TilePipeline.cs   模块：十个游戏入口的分派
  TileTable.cs      每类型的位集与掉落数据（密集 struct 数组）、安装期建表
```

### 2.2 每类型数据：密集 struct 数组（对应性能要求）

方块是最"按类型"的内容：所有判定都是"先拿到 tile type，再问它有没有覆写"。所以数据布局照 type 直接索引：

```csharp
private struct TileMeta
{
    public HookMask Masks;      // 这块覆写了哪些钩子
    public int DropItem;        // 默认掉落（0 = 用放置它的那个物品）
    public int DropStyle;       // 默认掉落的样式（-1 = 全都算）
}
```

* `TileMeta[]` 按 type 密集存放，下标就是 `ushort` 的类型号；`ref TileMeta meta = ref Meta[type]` 取用，不复制、不装箱；
* 只有"指定样式掉落"这种少数情况才进 `Dictionary<(int, int), int>`；
* 位集是 `HookMask`（本来就是个 4×ulong 的 struct）——**和参考实现的 `uint` 掩码同一个思路，但用现成的类型**，不必自己数位。

**为什么不再自带一套 tag 表？** 位集已经把"有没有覆写"编码进 struct 里；再存一份 tag 表就是两份真相。

### 2.3 掉落：一张反查表 + 两条来源

优先级从高到低：

1. `ModTile.GetItemDrops`（作者按格子自己算，返回 null 表示不接管）；
2. `ModTile.ItemDrops`（`RegisterItemDrop` 登记的，按样式匹配，`-1` 通配）；
3. `ModTile.ItemDrop`（单个 ID）；
4. 放置这块的那个 `ModItem`（建表时从 `createTile` 反查）。

第 4 条是关键的兜底：玩家放下去的东西挖回来还是它自己，这是最符合直觉的默认行为，而它需要**跨管线**的信息（物品的 `createTile`），所以这张表在安装期建一次。

### 2.4 分派顺序与语义

* `KillTile`：先问 `CanKillTile`（挖不动就整段跳过）→ 调作者 `KillTile`（可以设 `fail`／`effectOnly`／`noItem`）→ 多格家具的左上角由游戏自己算，作者只收一次 `KillMultiTile`。
* `RightClick`：`HasSmartInteract` 先答"这块是不是智能交互目标"，再调 `RightClick`；返回 true 表示"已处理"，游戏不再走默认交互。
* `Slope`：返回 false 就是"保持原样"，游戏不写 slope 字节。
* `TileFrame`：`resetFrame`／`noBreak` 都是可写引用；返回 false 让游戏整格跳过重算。

### 2.5 安装期建表

`Build` 做两件事：位集（`HookScan.Of(类型, typeof(ModTile))`）与掉落反查表。`SetStaticDefaults` / `PostSetDefaults` 由内容注册表按 `ModType` 生命周期调用，这里不再调一遍（重复调会让 `AddMapEntry` 记两次）。

`Maintain` 是低频补写：`Main.tileGlowMask` 的 mod 类型填 `-1`、玩家 `adjTile` 按类型数扩长、`TileObjectData._data` 空槽补 `Style1x1`。最后这条不是装饰：原版放方块时直接解引用 `GetTileData` 的返回值，`null` 在真机上是 SIGSEGV。

## 3. 取舍理由

* **为什么不像参考实现那样自己写原生 trampoline？** 我们的 detour 入口统一走 `On.` 代理。自建 trampoline 的好处是"一个入口一个 `kind`"，代价是所有参数都要和内核约定指针对齐；在这个项目里，那等于把最容易出错的一段代码放在没有编译期检查的位置。
* **为什么接受"绘制类钩子不接"？** 因为本版没有逐格的绘制入口：`TileDrawing.DrawTilesThreaded` 是整屏批量绘制的线程函数，挂它只能拿到批次，拿不到"第 (i,j) 格"。在这种入口上假装支持 `PreDraw`／`SetDrawPositions`，结果是作者改了参数而屏幕上什么都不会变——比不提供更容易误导。API 保留（删了会让已有 Mod 编译不过），启动日志与本文档逐条列出。
* **为什么掉落表要在安装期建、而不是每次挖的时候算？** 挖方块是高频动作且要即时响应；建表只做一次，之后是"一次数组索引 + 一次字典查找（仅指定样式时）"。
* **为什么 `StyleOf` 要自己算？** 游戏没有"这块是第几个样式"的公开方法；`TileObjectData` 里那套帧坐标与 `StyleHorizontal/WrapLimit/Multiplier` 的换算就是样式的定义，照算一遍是最省的做法。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| `CanKillTile` 抛错 | 按"挖得动"处理（否则方块会变成永不可销毁） |
| `KillTile` 抛错 | 记一条，照常挖掉 |
| `NumDust` / `CreateDust` 抛错 | 用原版尘土数量与类型 |
| `GetItemDrops` 抛错 | 退回登记表／默认掉落（不让挖了什么都不掉） |
| `TileFrame` 抛错 | 按 true（让游戏自己重算） |
| `Slope` 抛错 | 按 true（允许改坡度） |
| `RightClick` 抛错 | 视为未处理，游戏走默认交互 |
| `SetStaticDefaults` 抛错 | 由内容注册表兜；该方块仍可用，只是缺静态标记 |
| `Main.tileGlowMask` / `TileObjectData._data` 长度不足 | `Maintain` 每次按类型数门控重试，不静默跳过 |

**已知缺口（相对旧实现）**：`TileData.TileLists` 是内核侧的一块指针表，旧实现在 `TileData.Allocate` 之后用 `il2cpp_field_static_set_value` 整块重分配。本版没有搬这一段，影响的是"mod 方块与哪些方块互相融合/连接"的合并表，不影响挖掘、掉落与放置。补齐它需要在内核层分配内存并写静态字段，属于"要么完整做对、要么不做"的一类工作，单独作为后续项。

## 5. 扩展方式

现在新加一个方块钩子：`HookId` 末尾追加 → `ModTile` 加同名 `virtual` → 在 `TilePipeline` 对应入口分派。等哪天真有了逐格绘制/光照入口，把第 2 节列的那批一次性接上即可，`TileMeta` 不用改结构（`HookMask` 自动扩）。

## 6. 与其它管线的边界

* **墙壁管线**：`WallID` 是另一套类型空间，独立一条管线；地板与墙共用 `Tile` 的坐标但分派入口不同。
* **物品管线**：`createTile`／`placeStyle` 是物品的字段；本管线只读它们来建掉落表。
* **网络管线**：方块同步（`MessageID.TileChange`）归网络管线。
* **安装管线**：贴图上传、表扩容归安装管线；本管线只建"内容级"的表。
