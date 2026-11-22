# 内容管线：掉落（Loot）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（`Mods` 阶段末尾，所有内容装完之后） | 每个 `GlobalNpc.ModifyGlobalLoot(GlobalLoot)` 跑一次——对全部 NPC 加规则 |
| 同一时机 | 每种 NPC 一次 `GlobalNpc.ModifyNPCLoot(NPC, NPCLoot)`（样表来自 `ContentSamples.NpcsByNetId`） |
| 同一时机 | 每个 `ModItem` 的 `ModifyItemLoot` 预跑一次（规则建出来，`ItemLoot.For` 查得到） |
| mod NPC 静态默认值（NPC 安装管线那一步） | `ModNpc.ModifyNPCLoot(NPCLoot)`，规则直接进游戏 `ItemDropsDB` |
| Boss 死亡（`NPC.DoDeathEvents_DropBossPotionsAndHearts`） | `ModNpc.BossLoot`：覆写才接管，药水类型/堆叠与红心数以 ref 交给作者（见 2.4） |
| `Player.OpenBossBag` | 只认 mod 物品的袋：按 `ItemLoot.For(type)` 掷一遍规则，掷完补一次开发者套件掷取（`ItemID.Sets.PreHardmodeLikeBossBag` 标了 true 的袋不掷）；原版袋放回原版分支 |
| `Player.ItemCheck_ManageRightClickFeatures`（原版之后） | mod 物品右键：`CanRightClick` → `RightClick` → 掷 `ItemLoot` → `ConsumeItem` → 扣堆叠 |
| `CommonCode.DropItemFromNPC`（NPC 掉落物生成） | mod 物品编号改走旧签名 `Item.NewItem` 直落；原版物品放回原版路径 |
| `NPC.NPCLoot`（指令常量补丁） | 掉落判定里的 NPC 数上限从原版数放宽到"原版 + mod"总数 |

**不接**：多人同步（规则各端各自构建）；mod 自己实现的 `IItemDropRule`/条件/链条对象（只收游戏原生规则对象，见 6 节）；运行期改掉落后的图鉴即时刷新；开袋的递归链求值与掉落率上报。

## 2. 我们的设计

### 2.1 文件拆分

```
Pipelines/Loot/
  LootTable.cs     安装期三步：全局规则 → 按实体全局 → 物品规则预建（记数与日志）
  LootPipeline.cs  安装门与运行期派发：开 Boss 袋 / 物品右键 / mod 物品掉落改道 / NPCLoot 上限补丁
Pipelines/Npc/
  NpcPipeline.cs   Boss 死亡药水/红心的 BossLoot 派发（与 PreKill/OnKill 同处，门同居 GateHub）
Contracts/
  ILoot / NPCLoot / GlobalLoot / ItemLoot   三张表的入口类型
```

### 2.2 NPC 掉落：直接用游戏的表

游戏有 `ItemDropsDB` 这张唯一权威表。`NPCLoot.Add/Remove` 直接调 `RegisterToNPCNetId` / `RemoveFromNPCNetId`（mod NPC 不在游戏内部那张 type→netID 映射表里，按 netID 注册才命中得到），`GlobalLoot.Add` 走 `RegisterToGlobal`。规则对象本身用游戏自己的 `IItemDropRule` 工厂造（`ItemDropRule.Common(...)` 等），求值、条件、链全部交给游戏——图鉴合并与掉落率展示也因此天然可见。规则不复制第二份，避免与游戏的表不一致。

### 2.3 物品（袋/箱）规则：托管字典 + 自己掷骰

游戏没有"按物品查掉落"的表（`ItemDropDatabase` 只认 NPC），所以 `ItemLoot` 把规则按物品类型存在托管字典里，`ModifyItemLoot` 预跑填充。运行期两个入口掷它：

- **mod 袋**：`Player.OpenBossBag` 前缀认出 mod 物品就接管，`ItemLoot.For(type)` 的规则滚一遍，不落回原版那个大 switch；掷完按 tML 语义补一次开发者套件掷取（袋子在 `ItemID.Sets.BossBag` 里且不在 `ItemID.Sets.PreHardmodeLikeBossBag` 才掷）；原版物品放回原版分支。
- **右键**：`Player.ItemCheck_ManageRightClickFeatures` 后缀——mod 物品（或任一 `GlobalItem.CanRightClick(item)` 为真）才继续：`RightClick` → 掷规则 → `ConsumeItem` → 扣一次堆叠。

掷法：逐条 `rule.TryDroppingItem(DropAttemptInfo)`，`info` 填 `player` / `rng = Main.rand` / `IsExpertMode = Main.expertMode` / `IsMasterMode` / `IsInSimulation = false`；本条出结果后再看 `ChainedRules`，`CanChainIntoRule(父结果)` 通过就接着掷链上的下一条。

### 2.4 BossLoot：Boss 死亡的药水与红心

原版 Boss 死亡时调私有方法 `NPC.DoDeathEvents_DropBossPotionsAndHearts()`（`boss` 死亡路径），方法体内算药水类型/堆叠、掉药水、掷红心数、循环掉红心，末尾还有"当日双 Boss 击杀标记 → 掉 BadgersHat"的彩蛋段。interop 签名无参（`private void()`），药水类型/堆叠、红心数都是方法体局部量——On 包装拿不到可写引用，托管层的可写语义只有整方法接管一条路。

所以 `NpcPipeline` 挂一个独立的 `Gates.BossLoot` 门（有 `ModNpc` 覆写 `BossLoot` 才装 `On.Terraria.NPC.DoDeathEvents_DropBossPotionsAndHearts`），派发时：

1. **未覆写的（含原版 Boss）**：直接 `orig(self)`，原版行为零变化。
2. **覆写了的**：不跑原版方法体，按原版公式重建——药水堆叠 `Main.rand.Next(5, 16)`；药水类型按 NPC type 查原版对照（SkeletronHead / WallofFlesh / Deerclops → `HealingPotion`，QueenBee → `BottledHoney`，MoonLordCore → `SuperHealingPotion`，Golem / Plantera / DukeFishron / MartianSaucerCore / CultistBoss / HallowBoss / QueenSlimeBoss 与 WallofFleshEye..Squid 区间 → `GreaterHealingPotion`，其余 → `LesserHealingPotion`）；红心数 `Main.rand.Next(5) + 5`。三个值以 ref 交给 `BossLoot(ref potionType, ref potionStack, ref heartStack)`——两参重载由三参默认实现转发，覆写任一个都会派发到三参槽。
3. **派发后的落点**：药水类型或堆叠 ≤ 0 就不掷药水；红心数 clamp 到 ≥ 0 后逐个 `Item.NewItem(..., Heart, 1)`；`type == EyeofCthulhu` / `WallofFlesh` 的当日标记置位，双标记齐时 `ResetBadgerHatTime` + 掉 `BadgersHat`。每次掉落都现取 `GetItemSource_Loot()` 并重读位置/尺寸，与原版逐次取值的习惯一致。

对照表是从原版本体逐类核对出来的；这套公式只作用于覆写了 `BossLoot` 的 mod NPC，游戏升级改动原版公式时这里要跟着对一遍。

### 2.5 时机

全局规则与按实体全局必须等所有 mod 内容装完（作者规则里要查物品/NPC 编号），挂安装计划 `Mods` 阶段末尾，顺序是 `ApplyGlobals` → `ApplyNpcGlobals` → `PrepareItems`：按实体全局在 mod NPC 自己的表建完之后跑（全局规则加在作者规则之上），物品预建为开袋/右键的掷骰备好数据。

### 2.6 失败边界

- `ModifyGlobalLoot` 是纯声明，抛异常就说明作者写错了（编号不存在、规则链断了），安装期直接抛给安装管线，不吞。
- `ModifyItemLoot` 预建、右键/扣物品/掷骰逐环节、`BossLoot` 派发：每钩独立 try/catch，记一条 `ContentErrors`，不影响其余内容。

## 3. 取舍理由

* **为什么 NPC 掉落不复制一份托管表？** 游戏求值、图鉴合并、掉落率展示全都认 `ItemDropsDB`；复制一份必然漂移。mod NPC 按 netID 注册是唯一的命中路径。
* **为什么开袋/右键自己掷骰而不是塞进游戏表？** `ItemDropDatabase` 没有按物品的入口，塞进去也没有任何路径会读；托管字典 + 两个入口接管是能让 mod 袋出东西的最短路径。代价是链求值只走一层、自定义规则对象进不去（见 6 节）。
* **为什么 `OpenBossBag` 只认 mod 袋？** 原版袋的开关与扣减在原版分支里已经做完，接管它要重做整套开袋语义；mod 袋原版本就不认识，直接自己掷，语义干净。
* **为什么 `BossLoot` 整方法接管而不是挂原版中间点？** 派发点要拿的三个值是方法体局部量，interop 无参可传；托管层没有指令位挂钩设施。整方法接管把"原版公式"搬进托管代码，作者拿到的是与原版相同的三份初值，引用语义照作者习惯。
* **为什么 `BossLoot` 门独立？** `PreKill` / `OnKill` 挂在 `NPC.NPCLoot` 上，`BossLoot` 挂在另一个方法上；只有覆写了 `BossLoot` 的 mod 才装这个 On 钩子，不牵连击杀门。

## 4. 失败语义

| 情况 | 表现 |
|---|---|
| `ModifyGlobalLoot` 抛异常 | 安装期抛给安装管线（安装失败，指名作者） |
| 全局规则里引用了不存在的物品 | 同上（`ItemDropRule` 构造时就会炸） |
| `ModifyItemLoot` 抛异常 | 记一条 `ContentErrors`，该物品没有规则；安装继续 |
| `ModNpc.BossLoot` 抛异常 | 记录并隔离，三个值保持派发前初值，药水/红心照常掉 |
| 右键 / 扣物品 / 掷骰 / 开袋链抛异常 | 逐环节记 `ContentErrors`，不影响其它物品 |
| mod NPC 掉落 mod 物品（编号 ≥ 原版上限） | 旧签名 `Item.NewItem` 直落（无 `IEntitySource`、无网络下发） |
| NPCLoot 上限补丁没生效 | 记 Warn，掉落判定回原版上限 |

## 5. 扩展方式

* **自定义规则/条件/链条**：要把作者自己实现的 `IItemDropRule` 家族送进求值，得有"托管规则 → 原生壳对象 + 求值拦截"的互操作层；现表三张路都只收游戏原生对象。
* **开袋递归求值**：把 2.3 的手动展开换成游戏解析器整棵递归（含链的链与嵌套规则），或把单层循环改成链到底；两者都要连带补伪 NPC 作用域（`DropAttemptInfo.npc` 现在不填）。
* **开袋进背包 / 多人门 / 专家位**：掷骰入口补 `Main.netMode == 2` 早退、`IsExpertMode` 按 `ItemID.Sets.BossBag[item.type]` 补位、拦截 `CommonCode.DropItemFromNPC` 改 `QuickSpawnItem`——都在现有掷骰与改道骨架上加。
* **原版袋增补**：在 `OpenBossBag`/右键入口区分"mod 袋"与"原版袋 + 全局增补"两条路，先掷增补规则再放行原版逻辑，扣减只做一次。

## 6. 与其它管线的边界

* **NPC 管线**：`ModNpc.ModifyNPCLoot` 由 NPC 静态默认值那一步调，`BossLoot` 派发与 `PreKill`/`OnKill` 同在 NpcPipeline（各自的门独立）；本管线的 `ApplyNpcGlobals` 只管 `GlobalNpc.ModifyNPCLoot`，两者不重复。
* **物品管线**：`ItemLoot` 的规则表按物品类型存在物品侧（类型共享一份 `List<IItemDropRule>`），本管线只在安装期触发预建、运行期触发掷骰。
* **图鉴**：NPC 规则与全局规则在装载期直接进 `Main.ItemDropsDB`，世界载入时 `BestiaryDatabase.Merge` 自然合并，图鉴掉落展示天然可见；物品表规则只存托管字典，任何 UI 看不到。运行期再改 NPC 表不会即时刷新（重进世界即对；当场刷新只能整库重 `Merge`，会叠项，不做），`NPCLoot.Changed` 记着"表动过"。
* **条件/成就**：规则里的条件用游戏自己的 `IItemDropRuleCondition`，与本仓库的 `Condition` 无关。

### 这一版没接

1. **自定义 C# 规则/条件/链条**：`NPCLoot.Add` / `GlobalLoot.Add` / `ItemLoot.Add` 只收游戏原生 `IItemDropRule` 实现；作者自己实现的规则对象进不了求值（NPC/全局侧注册不认，物品侧掷骰用的是同一批原生对象）。
2. **开袋链的深层递归与伪 NPC 作用域**：手动展开只走一层 `ChainedRules`，`DropAttemptInfo.npc` 不填；规则链上再挂链的深度 ≥ 2 部分会丢。
3. **开袋的服务端门与"袋即专家"位**：掷骰入口只看 `Main.myPlayer`，`IsExpertMode` 只看 `Main.expertMode`。
4. **原版可开袋的全局增补**：`OpenBossBag` 接管只认 mod 袋；右键路径对原版物品有双重扣减风险。
5. **mod 物品掉落的生成路径**：`DropItemFromNPC` 改道用的是旧签名 `Item.NewItem`——没有 `IEntitySource`、多人不下发、`scattered` 被忽略。
6. **掉落率上报**：物品表规则没有报表类 UI 的可见性，也没拦 `ReportDroprates`。
7. **多人同步**：规则在各端各自构建，不加通道。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 已生效 | `GlobalNpc.ModifyGlobalLoot`（对全部 NPC 加规则）、`GlobalNpc.ModifyNPCLoot`（每种 NPC 一次，样表来自 `ContentSamples.NpcsByNetId`）、`ModNpc.ModifyNPCLoot`（规则进 `ItemDropsDB`） |
| 已生效 | `ModItem.ModifyItemLoot`：规则按类型预建；mod 袋 `OpenBossBag` 接管掷骰 |
| 已生效 | `ModItem.CanRightClick` / `RightClick` / `ConsumeItem`：右键 mod 物品时派发 |
| 已生效 | `ModNpc.BossLoot`：Boss 死亡药水/红心的三 ref 派发（覆写任一重载即接管；不覆写行为与原版一致） |

## 2026-11-21 批次：BossLoot 接线

- `ModNpc.BossLoot` 从声明变成真派发：`HookId` 追加 `BossLoot` 位（覆写位集按方法名自动置位，两个重载同位）；`NpcPipeline` 新增独立 `Gates.BossLoot` 门（有覆写才装 `On.Terraria.NPC.DoDeathEvents_DropBossPotionsAndHearts`）与派发体。
- interop 签名核对：`DoDeathEvents_DropBossPotionsAndHearts` 为无参私有实例方法，药水类型/堆叠与红心数都是方法体局部量——On 包装拿不到可写引用，派发采用"覆写才整方法接管 + 按原版公式重建初值"：堆叠 `Main.rand.Next(5, 16)`、红心 `Main.rand.Next(5) + 5`、药水类型按 NPC type 对照表逐类核对（含 type==35/113/668 → HealingPotion、222 → BottledHoney、398 → SuperHealingPotion、114..221 与 245/262/370/395/439/636 → GreaterHealingPotion、其余 → LesserHealingPotion）；随机数顺序为先堆叠后红心，随后派发三 ref、≤0 取消药水、红心 clamp ≥0 逐个掉、当日双标记与 BadgersHat 彩蛋段照原版补齐。
- 异常语义：作者覆写抛异常 → `Fail` 记录并隔离，值保持初值照常掉落；未覆写与原版 NPC 直接 `orig`，行为零变化。
