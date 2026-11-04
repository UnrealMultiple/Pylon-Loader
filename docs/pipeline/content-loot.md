# 内容管线：掉落（Loot）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（`Mods` 阶段，所有内容装完之后） | 每个 `GlobalNpc.ModifyGlobalLoot(ItemDropDatabase)` 跑一次——给**所有** NPC 加规则 |
| 同一时机 | 每个 `ModItem` 的 `ModifyItemLoot` 预跑一次（规则先建出来，作者 `ItemLoot.For` 查得到） |
| NPC 掉落（游戏自己的 `ItemDropsDB`） | mod NPC 的 `ModNpc.ModifyNPCLoot`（这条已经由 NPC 静态默认值那一步接了） |
| 按实体的全局掉落 | 每种 NPC 一次 `GlobalNpc.ModifyNPCLoot(npc, ItemDropDatabase)`（样表来自 `ContentSamples.NpcsByNetId`） |

**不接**：开 Boss 袋时的内容（`ItemLoot` 的规则不会自动掷骰）、多人同步。图鉴掉落本来就会显示（见 7 节），只是运行期改掉落后不即时刷新。

## 2. 我们的设计

### 2.1 两个文件

`Pipelines/Loot/LootTable.cs`（跑全局规则 + 预建物品规则 + 记数）、`Pipelines/Loot/LootPipeline.cs`（安装期一步）。

### 2.2 只接"能确定语义"的那一半

掉落这件事在本版分三段看：

1. **NPC 掉落**：游戏有 `ItemDropsDB`，`NPCLoot.Add` 直接调 `RegisterToNPC`——这条已经在用（NPC 静态默认值那一步会调 `ModNpc.ModifyNPCLoot`）。
2. **全局掉落**：`GlobalNpc.ModifyGlobalLoot` 是这套表里**唯一**"对全部 NPC 生效"的入口，`RegisterToGlobal` 也是现成的。本版补上这一步。
3. **物品掉落（Boss 袋 / 宝箱）**：**接不了**，原因见 2.3。

### 2.2b 开袋与右键（本轮接上）

```
右键 mod 物品
  → Player.ItemCheck_ManageRightClickFeatures（原版跑完之后）
  → ModContent 查得到 ModItem 且 CanRightClick(item) == true
  → 作者的 RightClick(item, player)
  → ItemLoot.For(modItem) 有规则就轮一遍
```

轮规则用的是游戏自己的 `IItemDropRule.TryDroppingItem(DropAttemptInfo)`：`DropAttemptInfo` 填 `player` / `rng = Main.rand` / `IsExpertMode` / `IsMasterMode`；失败再看 `ChainedRules`，用 `CanChainIntoRule(父结果)` 决定接不接。`Player.OpenBossBag` 也挂了前缀：mod 袋直接走同一条轮法，不落回原版那个大 switch。

### 2.3 为什么开袋这条路是本版明确不做的

- 游戏的开袋内容是 `Player.OpenBossBag(type)` 里的**硬编码分支**（按 itemId 写死每个袋子吐什么），`ItemDropDatabase` 里没有"按物品查规则"的方法（`GetRulesForNPCID` 只认 NPC）。
- 即使把 mod 袋的规则塞进 `ItemDropsDB`，也没有一条路径会去读它。
- 要自己掷骰就得重写掉落引擎（`IItemDropRule` 的链式求值、条件、连锁规则都在游戏内部实现里）。

所以这一版：**`ModifyItemLoot` 照跑、规则照存**（作者 `ItemLoot.For(this)` 拿得到），但**本版不代作者开袋**。这不是"漏了"，是把"做不到"写在明处，避免作者以为写了 `ModifyItemLoot` 袋子就自动有内容。

### 2.4 时机

全局规则必须在**所有 mod 的内容都装完之后**跑：作者的 `ModifyGlobalLoot` 里常见的是 `this.Add(ItemDropRule.Common(某 mod 物品, 1, 1, 1))`，那会去查物品编号。

所以这一步挂在安装计划的 `Mods` 阶段末尾，而不是 `Static`。

### 2.5 失败边界

`ModifyGlobalLoot` 是**纯粹的声明**：抛异常就说明作者写错了（编号不存在、规则链断了），安装期直接抛给安装管线，不吞。

相对的，`ItemLoot` 的预建如果抛，也只影响"作者拿不到规则表"，所以那一段单独记一次、继续处理下一个物品——预建不是游戏能否启动的必要条件。

## 3. 取舍理由

* **为什么全局规则只跑一次，不按 NPC 跑？** `ModifyGlobalLoot` 的语义就是"对全部 NPC"；按 NPC 跑会得到 N 倍重复的规则。
* **为什么现在接得了 `GlobalNpc.ModifyNPCLoot`？** 它的签名要一个 `NPC` 实例（`(NPC npc, ItemDropDatabase db)`）。以前以为要为每种 NPC 造一份样表——其实 `ContentSamples.NpcsByNetId` 里原版与 mod 的样例都在（NPC 样例那一步已经填好），直接拿来用，代价只是遍历一遍；跑在 `ModNpc.ModifyNPCLoot` 之后，全局规则加在作者规则之上。**早先版本说这条不接**，理由是"造样例的代价与 `ContentSamples` 同级"——那个前提是错的（样例现成）。
* **为什么 `ItemLoot` 还是预跑？** 它让"规则存在"这件事可查询（作者可以拿 `ItemLoot.For` 自查/互相追加），代价只是一次遍历；同时也为将来接开袋留好数据。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| `ModifyGlobalLoot` 抛异常 | 安装期抛给安装管线（安装失败，指名作者） |
| 全局规则里引用了不存在的物品 | 同上（`ItemDropRule` 构造时就会炸） |
| `ModifyItemLoot` 抛异常 | 记一次，该物品没有规则；安装继续 |
| mod NPC 的掉落 | 走游戏 `ItemDropsDB`，与原版 NPC 完全一致 |
| mod 袋被打开 | 只有原版分支的内容（见 2.3）——本版 mod 袋不吐作者写的东西 |

## 5. 扩展方式

接开袋：需要 (1) 一张"物品 → 规则"的表能被游戏读到，或 (2) 自己掷骰的实现（`IItemDropRule` 的求值、条件、连锁）。前者要改游戏的开袋分支（本仓库没有 IL 修补设施），后者等于重写掉落引擎。两条都不是加一个钩子能完成的。

## 6. 与其它管线的边界

* **NPC 管线**：mod NPC 的 `ModNpc.ModifyNPCLoot` 由 NPC 静态默认值那一步调；本管线的 `ApplyNpcGlobals` 只管 `GlobalNpc.ModifyNPCLoot`，两者不重复。
* **物品管线**：`ItemLoot` 的规则表按物品类型存在物品侧，本管线只触发预建。
* **图鉴**：全局规则与 mod 规则都会进图鉴的掉落展示——规则进 `Main.ItemDropsDB` 早于世界载入时的 `BestiaryDatabase.Merge`；`NPCLoot.Changed` 记着"表动过"，运行期改掉落要重进世界才刷新（要当场刷新只能整库重 `Merge`，会叠项，不做）。
* **条件/成就**：规则里的条件用游戏自己的 `IItemDropRuleCondition`，与本仓库的 `Condition` 无关。

### 这一版没接

1. ~~**`GlobalNpc.ModifyNPCLoot`**（按实体跑的那条）~~：**已接**（见 3 节表与 4 节；样表来自 `ContentSamples.NpcsByNetId`，跑在 `ModNpc.ModifyNPCLoot` 之后）。
2. **`GlobalLoot` 类**：它是普通类（不是 `ModType`），当前没有办法被自动发现，本版不把它当入口；等价能力用 `GlobalNpc.ModifyGlobalLoot`。
3. ~~**图鉴掉落刷新的通知**~~：**查过了，不需要挂**。mod 规则在 mod 装载期直接进 `Main.ItemDropsDB`（`NPCLoot.Add` → `RegisterToNPC`），而图鉴是在世界载入时 `BestiaryDatabase.Merge(ItemDropsDB)` 按当时的库合并——所以 mod 掉落本来就会出现在图鉴里。运行期再改掉落（`PostSetupContent` 之后还 `Add`）不会立刻刷新，重进世界就对了；要做到"当场刷"只能再跑一次 `Merge`，而那是整库重合并、会重复叠掉落展示项，不值得。
5. **多人同步**：规则在各端各自构建，不加通道。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 本版生效 | `GlobalNpc.ModifyGlobalLoot`（对全部 NPC 加规则）、`ModNpc.ModifyNPCLoot`（已接） |
| 新增 | `ModItem.ModifyItemLoot`：开袋时轮一遍（`Player.OpenBossBag` 认出 mod 物品就接管） |
| 新增 | `ModItem.CanRightClick` / `RightClick`：右键 mod 物品时派发（`Player.ItemCheck_ManageRightClickFeatures`） |
| 本版生效 | `GlobalNpc.ModifyNPCLoot`（每种 NPC 一次，样表来自 `ContentSamples.NpcsByNetId`） |
