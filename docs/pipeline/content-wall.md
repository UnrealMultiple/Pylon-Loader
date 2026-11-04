# 内容管线：墙壁（Wall）

## 1. 职责

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `WorldGen.KillWall` | `KillWall`（`fail` 可写：true = 只出尘土、墙还在） |
| `WorldGen.KillWall_MakeWallDust` | `CreateDust`（每粒尘土，可换类型或不出） |
| `WorldGen.KillWall_DropItems` | `Drop`（返回 false = 什么都不掉，`type` 可换物品） |
| `Framing.WallFrame` | `WallFrame`（返回 false = 保持原边框，`style`/`frameNumber` 可换） |
| 安装期建表 | 位集 + 掉落反查表 |

**不接**：`NumDust`（本版没有"出几粒"的逐格入口）、`KillSound`、`ModifyLight`、`AnimateWall`、`RandomUpdate`、地图项写入（`AddMapEntry` 只收集、不写游戏侧）。

## 2. 我们的设计

### 2.1 两个文件

```
Pipelines/Wall/
  WallPipeline.cs   模块：四个游戏入口的分派
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

1. `ModWall.ItemDrop`（作者直接给 ID）；
2. 放置这面墙的 `ModItem`（安装期从 `ContentSamples.ItemsByType` 的 `createWall` 反查，先到先得）。

第 2 条与方块管线是同一个思路：**放下去什么，挖回来就该是什么**，而这需要跨管线的信息，所以在安装期建一次表。

### 2.4 分派

* `KillWall`：先调作者（`fail` 可写），再把改过的 `fail` 交给原版——这样"只出尘土、墙不掉"是作者一句话的事，不必另开一个钩子。
* `DropItems`：先问 `Drop`；`false` 就整段不发物品；`type` 被改过就用作者的 ID；作者没给 ID 时用表里的默认掉落。
* `WallFrame`：`resetFrame` 可写；返回 false 直接跳过原版重算。

## 3. 取舍理由

* **为什么要自己建"墙 → 物品"的反查表？** 游戏的墙掉落逻辑对 mod 类型是空的：`ItemDrop` 不填，挖掉就没有任何东西。反查表让"默认不掉"变成"默认掉你放下去的那个"，这对作者是零成本的正确默认值。
* **为什么掉落用密集数组而不是 `Dictionary<int,int>`？** 一个类型只有一个默认掉落，下标就是类型号；字典在这里没有任何好处，只是多一次哈希与一条链。
* **为什么 `KillWall` 的 `fail` 要回传给原版？** 原版用同一个 `fail` 决定"墙是否消失"；回传是唯一能让作者影响"墙还在不在"的位置，另开钩子只会得到两个真相。
* **为什么地图项不写进游戏？** 本版没有稳定的墙地图项写入入口（内核侧那套注册不在我们手里），只把数据收在 `ModWall.MapEntries` 里，等有入口时一次接上。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| `KillWall` 抛错 | 记一条，用进来的 `fail` 继续 |
| `CreateDust` 抛错 | 用原版尘土 |
| `Drop` 抛错 | 退回表里的默认掉落（不让挖了什么都不掉） |
| `WallFrame` 抛错 | 按 true，让游戏自己重算 |
| `SetStaticDefaults` 抛错 | 由内容注册表兜；墙仍可用 |
| 反查表里没有对应物品 | 掉落为 0（原版行为），不猜 |

## 5. 扩展方式

新加一个墙钩子：`HookId` 末尾追加 → `ModWall` 加同名 `virtual` → 在 `WallPipeline` 对应入口分派。哪天有了 `NumDust` / `KillSound` 的逐格入口，补上就是两个方法，`WallMeta` 不用动。

## 6. 与其它管线的边界

* **方块管线**：墙与方块共用 `Tile.liquid` / 打击反馈，但类型空间与入口完全不同；`Framing` 的两套 `WallFrame` 只接 `Framing` 那个（另一个是地图生成工具）。
* **物品管线**：`createWall` 是物品字段，本管线只读取它建掉落表。
* **网络管线**：墙同步（`MessageID.TileChange` 的墙变体）归网络管线。
* **安装管线**：贴图与类型号归安装管线，本管线只建内容级表。
