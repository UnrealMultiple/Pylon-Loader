# 内容管线：墙壁（Wall）

## 1. 职责

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `WorldGen.KillWall` | `KillWall`（`fail` 可写：true = 只出尘土、墙还在）与 `NumDust`（Kill 内出几粒尘土，原版预算 fail ? 1 : 3） |
| `WorldGen.KillWall_MakeWallDust` | `CreateDust`（每粒尘土，可换类型或不出） |
| `WorldGen.KillWall_PlaySounds` | `KillSound`（返回 false = 不播；`HitSound < 0` = 静音） |
| `WorldGen.KillWall_DropItems` | `Drop`（返回 false = 什么都不掉，`type` 可换物品） |
| `Framing.WallFrame` | `WallFrame`（返回 false = 保持原边框，`style`/`frameNumber` 可换） |
| `Main.DoUpdate_AnimateWalls` | `AnimateWall`（整个类型共用的帧推进） |
| `Lighting.LightTiles` | `ModifyLight` 的收集段（见 2.5） |
| `Main.DoUpdateInWorld` | `ModifyLight` 的落光段（见 2.5） |
| 方块随机更新 | `RandomUpdate`（方块管线逐格随机更新时顺带分派墙分支，见 6 节） |
| 安装期建表 | 位集 + 掉落反查表 + 逐类型 `SetStaticDefaults` |

**不接**：`VanillaFallbackOnModDeletion`（`ModWall` 上声明了默认值 0，尚未实现，见 `docs/mods/09-blocos.md`）；`AddMapEntry` 的坐标式重载（直接抛 `NotSupportedException`，理由见 3 节）；地图项写入本身已接——`AddMapEntry` 收集的条目由地图图例（tile 侧的 `ModTileMap`）读走。

## 2. 我们的设计

### 2.1 两个文件

```
Pipelines/Wall/
  WallPipeline.cs   模块：各游戏入口的分派、发光的两阶段收集与落光
  WallTable.cs      每类型位集与掉落（密集 struct 数组）、安装期建表
```

### 2.2 每类型数据（密集 struct 数组，对应性能要求）

```csharp
private struct WallMeta
{
    public HookMask Masks;
    public int DropItem;
}
```

* `WallMeta[]` 按下标 = wall type 密集存放；判定路径 `ref WallMeta meta = ref _meta[type]`；
* 墙壁只有"整个类型一个默认掉落"，没有方块那种"按样式掉落"，所以**不需要字典**——整条管线没有一处哈希查找；
* `ModWall?[]` 与 `WallMeta[]` 同长度同步扩长。

### 2.3 掉落：显式优先、反查兜底

1. `RegisterItemDrop(itemType)`（作者显式登记）；
2. 放置这面墙的 `ModItem`（安装期从 `ContentSamples.ItemsByType` 的 `createWall` 反查，先到先得）。

第 2 条与方块管线是同一个思路：**放下去什么，挖回来就该是什么**，而这需要跨管线的信息，所以在安装期建一次表。

### 2.4 分派

* `KillWall`：先调作者（`fail` 可写），再把改过的 `fail` 交给原版——这样"只出尘土、墙不掉"是作者一句话的事，不必另开一个钩子。原版默认预算（只出尘土 1 粒、真挖掉 3 粒）之后可被作者的 `NumDust` 改写，尘土逐粒扣预算。
* `DropItems`：先问 `Drop`；`false` 就整段不发物品；`type` 被改过就用作者的 ID；作者没给 ID 时用表里的默认掉落。
* `WallFrame`：`resetFrame` 可写；返回 false 直接跳过原版重算。
* `AnimateWall` / `RandomUpdate`：覆写了才进各自的分派名单，每帧逐类型 / 逐格调用。

判定一律走位集：门级用 `HookGuard.Wants<ModWall>(HookId.X)`（有没有人覆写），格级用 `wall.HookEnabled(HookId.X)`。热路径上没有字典、没有字符串。

### 2.5 发光：收集与落光两阶段

`ModifyLight` 是唯一挂在光照循环上的墙钩子，因为 `LightTiles` 是引擎正在算这一帧的光的当口，**不能边扫边加**——所以拆成两段：

1. **收集段**（`On_Lighting.LightTiles`，orig 之后）：读 `Main.tile` 背后的墙数组裸指针（`TileData` 静态槽位直读，与存档管线同一访问面，只读不写），扫描「`LightTiles` 参数矩形每边外扩 28 格」（右/下按开区间再进一格），并按世界边界收口。逐格先看墙类型在不在发光位集里（`bool[]`，下标 = wall type），不命中直接下一格；命中才把 `(i, j, type)` 三元组写进缓冲。
2. **分派**：扫完统一逐格调 `ModWall.ModifyLight(i, j, ref r, ref g, ref b)`——起点 0，写多少存多少，可为负；不做任何钳制，原样进缓冲。
3. **落光段**（`On_Main.DoUpdateInWorld`，orig 之后）：逐格核对「这格现在的墙类型还等于收集时的类型」（被拆掉或被换成别的墙就丢弃），非零颜色统一 `Lighting.AddLight(i, j, r, g, b)`（逐格整型坐标重载）。主菜单时整段清空。

缓冲：`(i, j, type)` 的 `int[]` 起步 4096 格，rgb 三条 `float[]` 同长；超容量按倍翻新，稳定态零分配。缓冲每帧被收集段整段覆盖，落光段只消费不清理——两段之间隔一次绘制，所以作者拿到的是上一帧那格的光。

发光门（`Light`）的安装条件是「有 `ModWall` 覆写了 `ModifyLight`」：没人覆写就一门不装、两个钩子都不订阅，光照循环上零开销。类型位集在首次进世界时从 `WallTable` 建一次（安装期先于进世界，数据那时已齐）。

## 3. 取舍理由

* **为什么要自己建"墙 → 物品"的反查表？** 游戏的墙掉落逻辑对 mod 类型是空的：`ItemDrop` 不填，挖掉就没有任何东西。反查表让"默认不掉"变成"默认掉你放下去的那个"，这对作者是零成本的正确默认值。
* **为什么掉落用密集数组而不是 `Dictionary<int,int>`？** 一个类型只有一个默认掉落，下标就是类型号；字典在这里没有任何好处，只是多一次哈希与一条链。
* **为什么 `KillWall` 的 `fail` 要回传给原版？** 原版用同一个 `fail` 决定"墙是否消失"；回传是唯一能让作者影响"墙还在不在"的位置，另开钩子只会得到两个真相。
* **为什么发光要拆两段而不是在 `LightTiles` 里直接 `AddLight`？** 光照传播是迭代式的，引擎算这一帧的过程中插进来的光要么被本次传播流程吃掉、要么算进错的一轮；收集与落光分开后，加光只发生在帧与帧的间隙。代价是作者写的是上一帧那格的光——一格的时延换整个循环的正确性。
* **为什么扫描范围要外扩 28 格？** `LightTiles` 的参数只是屏幕那一片，而光会从屏幕外传播进来；只扫参数矩形的话，贴着屏幕边缘的发光墙照不亮视野里的格子。
* **为什么用裸指针扫墙数组？** 这是每帧上万格量级的读访问，逐格走 il2cpp 代理（每次访问都是一次互操作调用）撑不住。存档管线已经证明托管层可以直读 `TileData` 的墙数组槽位（类指针 + 静态块偏移），这里复用同一访问面，且只读不写。
* **为什么发光门是"类型覆写"而不是"哪些格能发光"的格子级门（方块的 `tileLighted` 那种）？** 原版光照对墙没有逐格登记这回事——墙能不能发光只由类型决定；逐格再查一次属性只会让每格的成本翻倍。
* **为什么 `AddMapEntry` 的坐标式重载直接抛？** 手机版的地图图例没有"按坐标算名字"的回调入口，只收静态的「颜色 + 名字」条目；依赖坐标的条目没有可落的位置，静默吞掉比抛错更坑。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| `KillWall` 抛错 | 记一条，用进来的 `fail` 继续 |
| `NumDust` 抛错 | 记一条，用原版预算 |
| `CreateDust` 抛错 | 用原版尘土 |
| `Drop` 抛错 | 退回表里的默认掉落（不让挖了什么都不掉） |
| `KillSound` 抛错 | 记一条，按返回 true 处理（照常播 `HitSound`） |
| `WallFrame` 抛错 | 按 true，让游戏自己重算 |
| `ModifyLight` 抛错 | 记一条（同类型同钩子只报一次），这一格按不发光处理，其他格照常 |
| 收集段自身异常 | 记一条（一次性），当帧缓冲清空、不落光，下一帧重扫 |
| 落光段自身异常 | 记一条（一次性），当帧缓冲清空 |
| `AnimateWall` / `RandomUpdate` 抛错 | 记一条，该类型该帧跳过 |
| `SetStaticDefaults` 抛错 | 由内容注册表兜；墙仍可用 |
| 反查表里没有对应物品 | 掉落为 0（原版行为），不猜 |

## 5. 扩展方式

新加一个墙钩子：`HookId` 末尾追加 → `ModWall` 加同名 `virtual` → 在 `WallPipeline` 对应入口分派；要按覆写分装的（每帧入口、全类型跑很贵的那类），照 `Light` 门的样式加一个门。`WallMeta` 不用动。

## 6. 与其它管线的边界

* **方块管线**：墙与方块共用 `Tile.liquid` / 打击反馈，但类型空间与入口完全不同；`Framing` 的两套 `WallFrame` 只接 `Framing` 那个（另一个是地图生成工具）。墙的 `RandomUpdate` 挂在方块管线的逐格随机更新里（同一个入口顺带分派墙分支），墙管线自己不开第二个逐格循环。
* **方块的光照**：方块的 `ModifyLight` 在方块管线（tile 侧自己挂 `Lighting.LightTiles`、自己缓冲）；墙这边只认墙数组，两边各管各的类型空间，同一帧里先后各自跑，结构上各写各的、互不引用。
* **存档管线**：墙数组裸指针的访问面与存档管线的全图扫描一致（那边读写、这边只读）；墙的存/读与侧车归存档管线。
* **物品管线**：`createWall` 是物品字段，本管线只读取它建掉落表。
* **网络管线**：墙同步（`MessageID.TileChange` 的墙变体）归网络管线。
* **安装管线**：贴图与类型号归安装管线，本管线只建内容级表。
