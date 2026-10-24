# 内容管线：树（Tree）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（`Static`） | 风格发号（32 起）+ `TreeTop` / `TreeBranch` / `Wood` 扩表 + 写作者贴图 + `GrowsOnTileId` 建映射 |
| 世界生成（`WorldGen.GetTreeType` / `IsTileTypeFitForTree`） | `CountsAsTreeType`：mod 地面算哪种原版树、能不能种树 |
| 树长成（`WorldGen.GrowTree` 后置、`ModTile.GrowSapling` 成功后） | 自动 `SetStyle`：地面是 mod 方块就把这一列换成 mod 外观 |
| 方块帧更新（`WorldGen.TileFrame` 后置） | 客户端收到别人的树时补 `SetStyle`（原生会把 mod 地面写成原版风格） |
| 摇树（`WorldGen.ShakeTree`） | `Shake(x, y, ref createLeaves)`——返回 true 再跑原版掉落 |
| 砍树掉落（`WorldGen.KillTile_GetItemDrops`） | `DropWood()`：这一列是 mod 树就把木头换成作者声明的 |

风格号是稳定的：同一个 mod 的同一棵树在每次启动都拿同一个号，因为它按注册序发。

**已接**：`TreeLeaf` 叶子样式（`WorldGen.GetTreeLeaf` 后置，作者给非负值就换 pass）、`CreateDust`（`KillTile_MakeTileDust`，作者给的号非默认就换；负数 = 不出尘）、`CanDropAcorn`（`KillTile_GetTreeDrops`，橡果掉不给由作者定，含原版没排到但框对了随机补一发的分支）、种苗长大（`AttemptToGrowTreeFromSapling`，苗是 mod 方块就交给 `ModTile.GrowSapling`）。

**已接（本轮）**：世界生成自动长 mod 树——`GetTreeType_1`/`IsTileTypeFitForTree` 让 mod 地面成为合法的种树地，`GrowTree` 后置按地面自动换外观，`TileFrame` 后置补客户端。

**不接**：背景树（`TreeTopsInfo`，见没接第 4 条的查证）。`SaplingGrowthType` 的**放置**侧已接（`TileObject.CanPlace` 前置换苗，见没接第 3 条）。

## 2. 我们的设计

### 2.1 三个文件

`Pipelines/Tree/TreeTable.cs`（风格号 + 地面映射）、`Pipelines/Tree/TreePipeline.cs`（扩表 + 写贴图 + `SetStyle`），作者基类 `Content/ModTree.cs` 本来就在。

### 2.2 风格号从 32 起

游戏的风格数是常量 `Main.numTreeStyles = 32`，所以 mod 从 32 开始发号。三张表都是按风格排的：

```
TreeTop[style * 3 + k]   k = 0..2   （树顶三张 80x80）
TreeBranch[style * 2 + k] k = 0..1   （树枝左右两列）
Wood[style]                          （树干）
```

扩表量按风格数算：树顶 +3/风格、树枝 +2/风格、树干 +1/风格。

### 2.3 地面映射是这张表的另一半

作者在 `GrowsOnTileId` 里写自己的方块号，管线建"方块 → 树"的反查。这样别处（比如种树物品、生成逻辑）问"这块地上该长哪种树"时能直接查到。一个方块只能对应一棵树，重复的后来者被忽略。

### 2.4 风格怎么落到世界里

三件事分开做，互不依赖：

* **判定按地面**：`TreeAt(i, j)` 先用 `WorldGen.GetTreeBottom` 找到地面方块，按 `GrowsOnTileId` 反查树。这样两端（服务器、客户端）对"这棵树是谁的"得同一个答案，不依赖 `Main.treeStyle`（那是原生写的本机视觉状态）。查不到地面的才退回 `Main.treeStyle`（作者手动 `SetStyle` 过的列）。
* **能不能长**：`IsTileTypeFitForTree` + `GetTreeType_1` 让 mod 地面返回作者声明的 `CountsAsTreeType`，原版的种树/生成判定就认这块地——**不重写生成算法**。
* **外观**：`WorldGen.GrowTree` 后置、`ModTile.GrowSapling` 返回 true 之后、以及 `WorldGen.TileFrame` 后置（客户端收树）各补一次 `SetStyle(x, tree)`。三个时机都是"树已经存在，只写风格号"。

`TreePipeline.SetStyle(x, tree)` 仍然是公开入口：作者自己摆树（不走 GrowTree）时手动调一次。

## 3. 取舍理由

* **为什么生成只"补外观"而不重写算法？** 原版 `GrowTree` 是一条按草地类型分支的生成路径（高度、树枝分布、边界检查都在里面）。重写它风险大、且每跟一次游戏更新都要再抄一遍；而"让 mod 地面通过原版的两条判定 + 长成后写风格号"能拿到同样的结果，代码只有几十行。
* **为什么三张表都要扩？** 少扩一张就会出现"树干是 mod 的、树顶还是原版"的半截树。
* **为什么 `SetStyle` 是公开静态方法？** 作者种树的地方各不相同（物品、生成、指令），给一个明确的入口比自己猜时机稳。
* **为什么种苗判据是"苗位置上是 mod 方块"？** 本构建没有 `TileID.Sets.TreeSapling`（那是 tML 加的集，`refs/dump.cs` 查无）；mod 苗本来就是 mod 方块，判据等价且零登记。多人客户端不替别人长树（服务器与世界生成负责）。

补一句代价：客户端收到别人长出来的树时，`Main.treeStyle` 会先被原生写成原版风格，再被 `TileFrame` 后置改正——同一帧内画的是对的，不存在闪烁；但如果某帧树方块没有触发 `TileFrame`（极少），外观会短暂停留成原版，逻辑判定不受影响（走地面）。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 作者没给树顶/树枝贴图 | 那几格留空，树顶/树枝画不出来（树干照常） |
| `GetTreeBottom` 在非树方块上被调 | 返回的位置越界或地面不是方块，判定按「不是 mod 树」走，不让钩子崩 |
| 客户端收树时 `TileFrame` 触发 `SetStyle` | 只写本机 `Main.treeStyle`，不发包、不改方块 |
| 世界生成调 `GrowTree` 但地面不是 mod 方块 | 不碰风格号，原版行为完全不变 |
| `GetTexture` 抛异常 | 记一次，这棵树不写贴图 |
| `SetStyle` 传的 x 越界 | 返回 false，不改任何东西 |
| `GrowsOnTileId` 重复 | 先注册的赢，后来者忽略 |
| 风格数超过表容量 | 不会：扩表按风格数算 |

## 5. 扩展方式

新增一种树的判定：`ModTree.GrowsOnTileId` 加地面方块号 → `TreeTable` 自动建映射，其余钩子不用动。
新增一个生成时机：`WorldGen.GrowTreeWithSettings`（宝石树那类设置式生成）还没挂，要支持时按 `ForGround` 判断后补一次 `SetStyle` 即可，不需要重写生成。
接摇树与掉落：`ModTree.Shake` / `CanDropAcorn` / `DropWood` 已有声明，挂对应入口后转发即可。

## 6. 与其它管线的边界

* **方块（21）**：`GrowsOnTileId` 指向 mod 方块，方块本身归方块管线。
* **物品**：种树物品与掉木归物品/掉落管线。
* **血腥粒子（19）**：`TreeLeaf` 是 gore，本管线只声明不生成。
* **背景**：背景树（`TreeTopsInfo`）归背景管线，和世界里的树无关。

### 这一版没接

1. ~~**世界生成时自动长出 mod 树**~~：**已接**（不重写生长：`GetTreeType_1`/`IsTileTypeFitForTree` 让 mod 地面成为合法种树地，`GrowTree`/`GrowSapling` 成功后补 `SetStyle`，`TileFrame` 后置补客户端）。
2. ~~**`TreeLeaf` 叶子粒子、`CreateDust` 打干尘土**~~：**已接**（`GetTreeLeaf` 后置 + `KillTile_MakeTileDust`）。
3. ~~**`CanDropAcorn` / `SaplingGrowthType` 的种苗链**~~：**已接全**——`CanDropAcorn`（砍树侧）、种苗长大（`ModTile.GrowSapling`）、`SaplingGrowthType` **放置**侧（`TileObject.CanPlace` 钩子生成后，橡果种在 mod 树的地面时换成作者声明的苗；基类默认实现返回原版苗、不替换，避免覆盖游戏按地形挑好的 style）。
4. **背景树**（`TreeTopsInfo`）：**核对为接不上**——`TreeTopsInfo.AreaId` 是 13 个固定的原版区域常量（`Forest1..Underworld`），`_variations` 是 `int[Count]`，风格由原生 `RandomizeTreeStyle(rand, areaId)` 按区域从原版风格里挑；托管侧没有任何"区域 ↔ 世界坐标"的映射，也没有作者可声明"我的树进背景"的入口。硬把 mod 风格号写进 `_variations` 只能是瞎猜区域，所以不做。世界里的树走 `Main.treeStyle`，与这条链路无关。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 生效 | `GetTexture` / `GetTopTextures` / `GetBranchTextures` 会被写进扩长后的三张表 |
| 生效 | `GrowsOnTileId` 会建"方块 → 树"映射（一个方块一棵树） |
| 新增 | `TreePipeline.SetStyle(x, tree)`：把某一列的世界树换成 mod 外观（自动生长与客户端补帧已经会调它） |
| 新增 | `virtual TreeTypes CountsAsTreeType`（默认 Forest）：mod 地面算哪种原版树，生成与「能不能种」判定都读它 |
| 变更 | 砍树/摇树/叶子/尘土的判定从「看 `Main.treeStyle`」改成「先按地面方块反查，查不到再看风格号」——两端判定一致，作者不调 `SetStyle` 也能被认出 |
| 生效 | `Shake` 摇树回调（`Main.treeStyle[x]` 是 mod 树时派给作者） |
| 生效 | `DropWood` 砍树掉木头 |
| 生效 | `TreeLeaf` / `CreateDust` / `CanDropAcorn` / `ModTile.GrowSapling`（种苗长大） |
| 本版不生效 | 背景树（`TreeTopsInfo`）；自动生长与 `SaplingGrowthType` 放置侧已接 |
