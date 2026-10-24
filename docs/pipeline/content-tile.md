# 内容管线：方块（Tile）

## 1. 职责

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `WorldGen.CanKillTile`（三个重载全接） | `CanKillTile`（镐子挖不挖得动，`blockDamaged` 可写） |
| `WorldGen.KillTile` | `KillTile`（`fail`／`effectOnly`／`noItem` 可写） |
| `WorldGen.KillTile_PlaySounds` | `KillSound`（返回 false = 不播；`HitSound < 0` = 静音） |
| `WorldGen.KillTile_MakeTileDust` | `NumDust`（定预算）+ `CreateDust`（每粒扣 1，可换类型/不出） |
| `WorldGen.KillTile_DropItems` / `KillTile_GetItemDrops` | `CanDrop` + `GetItemDrops`（生成与 out 变体两个入口）+ `Drop` |
| `WorldGen.TileFrame` | `TileFrame`（边框重算，可否决） |
| `WorldGen.TileFrameImportant` | 多格完整性检查与整件毁坏（`TileObjectCheck`：残件触发整件 `KillTile` + `KillMultiTile`） |
| `WorldGen.UpdateWorld_OvergroundTile` / `...UndergroundTile` | `RandomUpdate`（over/underground 两入口；同一格顺带分派墙分支，见 6 节） |
| `Wiring.HitWireSingle` | `PreHitWire`（false = 信号不到这格）→ 原版信号 → `HitWire`（作用域释放后在原版之后跑；同点重入不重复派发） |
| `Player.AdjTiles` | `AdjTiles`（这块也算制作站） |
| `Player.TileInteractionsUse` / `...MouseOver` / `...CheckLongDistance` | `RightClick`（返回 true = 已处理）/ `MouseOver` / `MouseOverFar` |
| `TileSmartInteractCandidateProvider.FillPotentialTargetTiles` | `HasSmartInteract`（智能光标候选逐格问，`JoinValue` 去重进 targets） |
| `Player.PlaceThing_...` / `Projectile.CanExplodeTile` / `WorldGen.ReplaceTile` | `CanPlace` / `CanExplode` / `CanReplace` |
| `TileDrawing.IsTileDangerous` / `Main.IsTileSpelunkable` | `IsTileDangerous` / `IsTileSpelunkable`（格子坐标来自绘制钩的顺带记录，不做大扫描） |
| `SceneMetrics.Scan` | `NearbyEffects`（buff 区域 closer=false + 视觉区域 closer=true，ModTile 先、GlobalTile 后） |
| `WorldGen.SlopeTile` / `PoundTile` | `Slope`（false = 保持原样） |
| `Chest.IsLocked` / `Unlock` / `Lock` | `IsLockedChest` / `UnlockChest` / `LockChest` |
| `PlayerSittingHelper` / `PlayerSleepingHelper.GetSittingTargetInfo` | `ModifySittingTargetInfo` / `ModifySleepingTargetInfo` |
| `TileDrawing.GetTileDrawData`（两个重载）+ `Main.AnimateTiles` | `SetDrawPositions` / `SetSpriteEffects` / `AnimateIndividualTile` / `AnimateTile`（形状/动画修正 + 4096 槽绘制缓存） |
| `TileDrawing.PostDrawTiles` | `PreDraw` / `DrawEffects` / `PostDraw` / `SpecialDraw`（原版画完后的逐格重画通道，见 6 节 tiledraw） |
| `Lighting.LightTiles` | `ModifyLight` 的收集段（见 2.5） |
| `Main.DoUpdateInWorld` | `ModifyLight` 的落光段（见 2.5） |
| `WorldFile` 存读档 | 存档守卫：mod 静态块换遮蔽副本、全图扫描写侧车、读档按行恢复（归存档管线，见 6 节） |
| 安装期建表 | 位集 + 掉落反查 + 逐类型 `SetStaticDefaults` |

**不接**：`VanillaFallbackOnModDeletion`（`ModTile` 上声明了默认值 0，尚未实现，见 `docs/mods/09-blocos.md`）；`AddMapEntry` 的坐标式重载（直接抛 `NotSupportedException`，理由见 3 节）；安装期的自动耦合（`tileLavaDeath → tileObsidianKill`、`tileSolid → tileNoSunLight`、`RoomNeeds` 的 bool 标记同步进 `*Types` 名单、`HighlightTexture` 描边贴图加载——属性都在，补装链路未接，作者写什么是什么）。

## 2. 我们的设计

### 2.1 文件

```
Pipelines/Tile/
  TilePipeline.cs          模块：安装门（Gates）与挖/尘土/掉落/边框/通电/制作站/交互/危险格/随机更新的分派
  TileTable.cs             每类型位集与掉落数据（密集 struct 数组）、安装期建表
  TileLightHooks.cs        发光的收集与落光两阶段（见 2.5）
  TileEffectsHooks.cs      NearbyEffects 逐格分派
  TileInteractionHooks.cs  低频交互：箱子锁、坐卧目标、锤坡、智能光标、出生点（TileID.Sets.IsValidSpawnPoint 的 mod 方块在 Player.CheckSpawn 里临时换装成床）
  TileDrawHooks.cs         GetTileDrawData 形状/动画修正、动画帧推进、绘制缓存
  TileDrawPipeline.cs      （独立模块 tiledraw）PostDrawTiles 之后的逐格重画通道
  TileObjectCheck.cs       多格完整性检查与整件毁坏
  TileMergeFix.cs          tileMerge 行独立 + SetupTileMerge 快照回放
  TileLists.cs             TileData.TileLists 换表
  TileSavePipeline.cs      存档守卫（mod 静态块遮蔽 + 侧车存读）
  ModTileMap.cs            地图项侧车
```

### 2.2 安装门：按覆写分装

每个（组）游戏入口一个门（`TilePipeline.Gates`），条件与订阅同居一门。`Wanted()` 问"有没有内容覆写了这个钩子"（`ModType.Plan` 位集 + `GlobalTileRegistry.AnyWith`），有人覆写才挂对应的 `On.` 钩子——没有任何 mod 覆写时，原版路径上一个多余判断都没有。`GateHub` 反射发现所有门、每轮重评条件，未就绪的保持 pending 下轮再装。

### 2.3 每类型数据：密集 struct 数组

方块是最"按类型"的内容：所有判定都是"先拿到 tile type，再问它有没有覆写"。所以数据布局照 type 直接索引：

```csharp
internal struct TileMeta
{
    public HookMask Masks;      // 这块覆写了哪些钩子
    public int DropItem;        // 默认掉落（0 = 用放置它的那个物品）
    public int MinPick;         // 低于这个镐力挖不动
    public float MineResist;    // 挖掘抗性（每击伤害除以它）
}
```

* `TileMeta[]` 按 type 密集存放，下标就是 `ushort` 的类型号；`ref TileMeta meta = ref Meta[type]` 取用，不复制、不装箱；
* 只有"指定样式掉落"这种少数情况才进 `Dictionary<(int, int), int>`；
* 位集是 `HookMask`（8×ulong 的 struct，容量 512 位），枚举追加即自动扩；
* `ModTile?[]` 与 `TileMeta[]` 同长度同步扩长；`TileObjectData` 的形状快照与绘制形状另有平行数组，同样按下标取。

### 2.4 分派顺序与语义

* **ModTile 先、GlobalTile 后**，所有逐格钩子都是这个顺序。
* `KillTile`：先问 `CanKillTile`（挖不动就整段跳过）→ 调作者 `KillTile`（三个可写引用交给原版）→ 多格家具的整件毁坏由 `TileObjectCheck` 负责（任一格被重框就查整件：残件触发整件 `KillTile` + 掉落 + 作者只收一次 `KillMultiTile`，随后重框邻域）。
* `RightClick`：按（帧号、松键、目标格）去重连点——同一格的连按爆发只算一次新按下，换目标格立即算新；返回 true 表示"已处理"，游戏不再走默认交互。
* `HitWire`：同一点重入直接放行不重复派发；信号点记入作用域，释放时（原版信号处理之后）才派发 ModTile 与 GlobalTile 的 `HitWire`。
* `IsTileDangerous` / `IsTileSpelunkable`：格子坐标由 `GetTileDrawData` 钩顺带记录，原版问到哪个格就答哪个格——不在判定路径上自己开大扫描。
* 判定一律走位集：门级用 `HookGuard.Wants<ModTile>(HookId.X)` / `GlobalTileRegistry.AnyWith(HookId.X)`（有没有人覆写），格级用 `tile.HookEnabled(HookId.X)`。热路径上没有字典、没有字符串。

### 2.5 发光：收集与落光两阶段

`ModifyLight` 是唯一挂在光照循环上的方块钩子，而 `LightTiles` 是引擎正在算这一帧的光的当口，**不能边扫边加**——所以拆成两段：

1. **收集段**（`On_Lighting.LightTiles`，orig 之后）：读 `Main.tile` 背后的 tile 数组裸指针（`TileData` 静态槽位直读：`TileLookup → definition → TileType / TileSHeader`，与存档管线同一访问面，只读不写），扫描「`LightTiles` 参数矩形每边外扩 28 格」并按世界边界收口。逐格先看 active 位，再看类型在不在发光位集里（`bool[]`，下标 = tile type，首次进世界时从 `TileTable` 建一次）；有 `GlobalTile` 覆写 `ModifyLight` 时，判据放宽为"全部活动格"。命中的格子把 `(i, j, type)` 三元组写进缓冲。扫描拿不到裸指针（类未初始化、世界未建）就当帧不收集，不影响游戏自身光照。
2. **分派**：扫完统一逐格调作者钩子——`ModTile.ModifyLight(i, j, ref r, ref g, ref b)` 的门是 `Main.tileLighted[type]`（作者在 `SetStaticDefaults` 里置 true 才算发光类型，与原版方块的语义一致），`GlobalTile.ModifyLight(i, j, type, ...)` 无门跟在后面。起点 0，写多少存多少，可为负、可大于 1；不做任何钳制，原样进缓冲。
3. **落光段**（`On_Main.DoUpdateInWorld`，orig 之后）：逐格核对「这格还 active、类型还等于收集时的类型」（被拆掉或换型就丢弃），非零颜色统一 `Lighting.AddLight(i, j, r, g, b)`（逐格整型坐标重载）。主菜单时整段清空。

缓冲：`(i, j, type)` 的 `int[]` 起步 4096 格，rgb 三条 `float[]` 同长；超容量按倍翻新，稳定态零分配。缓冲每帧被收集段整段覆盖，落光段只消费不清理——两段之间隔一次绘制，所以作者拿到的是上一帧那格的光。

发光门（`Light`）的安装条件是「有 `ModTile` 或 `GlobalTile` 覆写了 `ModifyLight`」：没人覆写就一门不装、两个钩子都不订阅，光照循环上零开销。

### 2.6 掉落：一张反查表 + 两条来源

优先级从高到低：

1. `ModTile.GetItemDrops`（作者按格子自己算，返回 null 表示不接管）；
2. `ModTile.ItemDrops`（`RegisterItemDrop` 登记的，按样式匹配，`-1` 通配）；
3. 放置这块的那个 `ModItem`（建表时从 `ContentSamples.ItemsByType` 的 `createTile` 反查，先到先得）。

第 3 条是关键的兜底：玩家放下去的东西挖回来还是它自己，这是最符合直觉的默认行为，而它需要**跨管线**的信息（物品的 `createTile`），所以这张表在安装期建一次。

### 2.7 安装期建表

`Build` 做两件事：位集（`HookScan.Of(类型, typeof(ModTile))`）与掉落反查表；`SetStaticDefaults` 由内容注册表按 `ModType` 生命周期调用，这里不再调一遍（重复调会让 `AddMapEntry` 记两次）。建表期间临时解锁 `TileObjectData.readOnlyData`，随后快照 mod 形状。

`Maintain` 是低频补写：`Main.tileGlowMask` 的 mod 类型填 `-1`、玩家 `adjTile` 按类型数扩长、`TileObjectData._data` 重建后回放 mod 形状。最后这条不是装饰：原版放方块时直接解引用 `GetTileData` 的返回值，`null` 在真机上是 SIGSEGV。

## 3. 取舍理由

* **为什么 detour 入口统一走 `On.` 代理？** 自建原生 trampoline 的好处是"一个入口一个 kind"，代价是所有参数都要和内核约定指针对齐；在这个项目里，那等于把最容易出错的一段代码放在没有编译期检查的位置。`On.` 钩子有编译期签名检查，作者钩子的分派留在托管层做。
* **为什么发光要拆两段而不是在 `LightTiles` 里直接 `AddLight`？** 光照传播是迭代式的，引擎算这一帧的过程中插进来的光要么被本次传播流程吃掉、要么算进错的一轮；收集与落光分开后，加光只发生在帧与帧的间隙。代价是作者写的是上一帧那格的光——一格的时延换整个循环的正确性。
* **为什么扫描范围要外扩 28 格？** `LightTiles` 的参数只是屏幕那一片，而光会从屏幕外传播进来；只扫参数矩形的话，贴着屏幕边缘的发光方块照不亮视野里的格子。
* **为什么用裸指针扫 tile 数组、连 lookup 间接层也直读？** 这是每帧上万格量级的读访问，逐格走 il2cpp 代理（每次访问都是一次互操作调用）撑不住。tile 数组是 `TileLookup → definition → type/sHeader` 的间接布局，与墙的直接数组不同，但访问面相同：类指针 + 静态块槽位偏移。存档管线已经验证这条链路在真机上成立，这里复用同一访问面，且只读不写。
* **为什么有全局钩时收集判据放宽到"全部活动格"？** `GlobalTile.ModifyLight` 是按格咨询的横切钩子，作者在钩子里按 `type` 自己挑关心的格子；只扫 mod 类型的话，全局钩在原版方块上就永远收不到。mod 自身的钩子仍按类型位集收，不扩大。
* **为什么 `ModTile.ModifyLight` 的门是 `Main.tileLighted`？** 原版光照对"哪类方块发光"是逐类型登记的：不登记的类型按普通方块处理，引擎不会为它多算一轮。作者忘写时静默不发光，与原版行为一致，而不是替作者猜一个默认亮。
* **为什么掉落表要在安装期建、而不是每次挖的时候算？** 挖方块是高频动作且要即时响应；建表只做一次，之后是"一次数组索引 + 一次字典查找（仅指定样式时）"。
* **为什么 `StyleOf` 要自己算？** 游戏没有"这块是第几个样式"的公开方法；`TileObjectData` 里那套帧坐标与 `StyleHorizontal/WrapLimit/Multiplier` 的换算就是样式的定义，照算一遍是最省的做法。
* **为什么 `AddMapEntry` 的坐标式重载直接抛？** 手机版的地图图例没有"按坐标算名字"的回调入口，只收静态的「颜色 + 名字」条目；依赖坐标的条目没有可落的位置，静默吞掉比抛错更坑。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| `CanKillTile` 抛错 | 按"挖得动"处理（否则方块会变成永不可销毁） |
| `KillTile` 抛错 | 记一条，照常挖掉 |
| `NumDust` / `CreateDust` 抛错 | 用原版尘土数量与类型 |
| `KillSound` 抛错 | 记一条，按返回 true 处理（照常播 `HitSound`） |
| `GetItemDrops` 抛错 | 退回登记表／默认掉落（不让挖了什么都不掉） |
| `TileFrame` 抛错 | 按 true（让游戏自己重算） |
| `Slope` 抛错 | 按 true（允许改坡度） |
| `RightClick` 抛错 | 视为未处理，游戏走默认交互 |
| `NearbyEffects` 抛错 | 记一条，跳过这一格 |
| `ModifyLight` 抛错 | 记一条（同类型同钩子只报一次），这一格少这一份光；同格的其余钩子、其他格子照常，下一帧照常再问 |
| 发光收集段自身异常 / 拿不到裸指针 | 记一条（一次性），当帧缓冲清空、不落光，下一帧重扫；游戏自身光照不受影响 |
| 发光落光段自身异常 | 记一条（一次性），当帧缓冲清空 |
| `SetStaticDefaults` 抛错 | 由内容注册表兜；该方块仍可用，只是缺静态标记 |
| `Main.tileGlowMask` / `TileObjectData._data` 长度不足 | `Maintain` 每次按类型数门控重试，不静默跳过 |

## 5. 扩展方式

新加一个方块钩子：`HookId` 末尾追加 → `ModTile`/`GlobalTile` 加同名 `virtual` → 门 `Wanted()` 用 `HookGuard.Wants` / `GlobalTileRegistry.AnyWith` 判覆写、按需订阅 → 分派点照 2.4 的顺序调。`HookMask` 容量 512 位，枚举追加即自动扩，`TileMeta` 不用改结构。要逐格扫描的钩子（如发光）照 2.5 的两阶段形状接：LightTiles 收集、世界更新收尾落光，扫描直读裸指针、按类型位集收口。

## 6. 与其它管线的边界

* **墙壁管线**：`WallID` 是另一套类型空间，独立一条管线；地板与墙共用 `Tile` 的坐标但分派入口不同。墙的 `RandomUpdate` 挂在本管线的逐格随机更新里（同一个入口顺带分派墙分支），墙管线自己不开第二个逐格循环。
* **墙的光照**：墙的 `ModifyLight` 由墙管线在同两个游戏钩子上独立做两阶段——两边各管各的类型空间（tile 侧只认 tile 数组、墙侧只认墙数组），同一帧里先后各自收集、各自落光，结构上各写各的、互不引用。
* **tiledraw（`TileDrawPipeline`）**：`PreDraw` / `DrawEffects` / `PostDraw` / `SpecialDraw` 走 `PostDrawTiles` 之后的逐格重画通道，是独立模块；本管线的 `GetTileDrawData` 修正与绘制缓存为它供形状与动画帧。
* **物品管线**：`createTile`／`placeStyle` 是物品的字段；本管线只读它们来建掉落表。
* **TileEntity 管线**：`PlaceInWorld`（玩家放成功后）由它代分派。
* **Tree 管线**：`GrowSapling`（树苗长大）由它分派。
* **存档管线**：mod 方块的遮蔽、侧车与恢复归 `TileSavePipeline`；裸指针访问面与它的全图扫描一致（那边读写、这边只读）。
* **网络管线**：方块同步（`MessageID.TileChange`）归网络管线。
* **安装管线**：贴图上传、表扩容归安装管线；本管线只建"内容级"的表。
