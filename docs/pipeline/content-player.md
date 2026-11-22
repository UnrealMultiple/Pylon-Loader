# 内容管线：玩家（Player）

## 1. 职责

玩家管线的产品不是"一个新 id"，而是一组**按玩家存在的实例**：作者的 `ModPlayer` 每个玩家一份，由游戏的各种玩家入口分派。玩家身上的钩子面很宽——每帧更新、死亡与受伤、物品使用、钓鱼、护士与商店、联网与存档——所以分派点不止一个文件，但**实例保管只有一份**（§2.2），所有入口共用。

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `Player.ResetEffects` | `ResetEffects`、`ModifyMaxStats`（聚合写回上限，见 §2.5） |
| `Player.Update` | `PreUpdate`（原版之前）、`PostUpdate`（之后） |
| `Player.UpdateBuffs` | `PreUpdateBuffs` / `PostUpdateBuffs` |
| `Player.UpdateEquips` | `UpdateEquips`（`PostUpdateEquips` / `FrameEffects` 由装备管线的入口代派） |
| `Player.UpdateLifeRegen` | `UpdateBadLifeRegen`（原版之前）、`UpdateLifeRegen`（之后） |
| `Player.UpdateDead` | `UpdateDead`（原版之前派发） |
| `Player.KillMe` | `PreKill`（可否决）、`Kill`（真的死了才发） |
| `Player.ItemCheck` | `PreItemCheck`（可否决，只跳过原版这一次物品检查）、`PostItemCheck`（之后无条件派发） |
| `Player.CanUseItem` | `CanUseItem`（可否决；`ModItem`/`GlobalItem` 的一半归物品管线） |
| `Player.ApplyPotionDelay` | 药水链：`ModifyPotionDelay` → `ApplyPotionDelay`（可否决，见 §2.6） |
| `Player.RecalculateLuck` | `PreModifyLuck`（可否决原版求和）、`ModifyLuck` |
| `Player.RefreshInfoAccs` | `ResetInfoAccessories` |
| `Player.KeyDoubleTap` / `KeyHoldDown` | `ArmorSetBonusActivated` / `ArmorSetBonusHeld` |
| `Player.TrySwitchingLoadout` | `OnEquipmentLoadoutSwitched`（预设真的换了才发） |
| `Player.Spawn` | `OnRespawn`（死亡重生才发；进世界不算复活） |
| `Player.Teleport` | `CanBeTeleportedTo`（可否决） |
| `Player.PickupItem` | `OnPickup`（可否决：否决的物品就地消失、不进背包） |
| `Player.OnHit` | `OnHitAnything` |
| `Player.HorizontalMovement` 与四处碰撞入口 | `PostUpdateRunSpeeds`；`PreUpdateMovement`（本帧第一次移动步进之前，每帧至多一次） |
| `Player.CapAttackSpeeds` / 暂停时的本地更新 | `PostUpdateMiscEffects` / `UpdateAutopause` |
| `Player.UpdateDyes` / `UpdateVisibleAccessories` | `UpdateDyes`、`UpdateVisibleAccessories`、`UpdateVisibleVanityAccessories` |
| `TriggersSet.CopyInto`（仅本地玩家） | `ProcessTriggers` |
| `Player.Hurt` 全链（`PlayerHurtHooks`） | `ImmuneTo`、`FreeDodge`、`ConsumableDodge`、`ModifyHurt`、`OnHurt`、`PostHurt`、`CanBeHitByNPC`、`CanBeHitByProjectile`、`ModifyHitBy*`、`OnHitBy*`、`CanHitPvpWithProj` |
| 钓鱼族（`PlayerFishingHooks` / `FishingLineHooks`） | `GetFishingLevel`、`CanConsumeBait`、`ModifyFishingAttempt`、`CatchFish`、`ModifyCaughtFish`、`AnglerQuestReward`、`ModifyFishingLine` |
| 护士与商店（`PlayerNurseShopHooks`） | `ModifyNurseHeal` / `ModifyNursePrice` / `PostNurseHeal`、买卖四钩子（`Can*/Post* BuyItem/SellItem`） |
| `ItemSlot.OverrideHover` / `OverrideLeftClick` | `HoverSlot` / `ShiftClickSlot` |
| 合成材料源（`PlayerCraftingHooks`） | `AddMaterialsForCrafting`（含消耗回调 `ItemConsumedCallback`） |
| 开局背包两个入口 | `AddStartingItems`、`ModifyStartingInventory` |
| 捕捉链（NPC 管线的 `CatchScan`） | `CanCatchNPC` / `OnCatchNPC` |
| 联网族（`PlayerNetPipeline`） | `SyncPlayer`、`SendClientChanges`、`CopyClientState`、`PlayerConnect`、`PlayerDisconnect`、`OnEnterWorld` |
| 存档族（`PlayerSavePipeline`） | `PreSavePlayer`、`PreSaveCustomData`、`SaveData`、`LoadData`、`PostSavePlayer` |
| 自然再生中体桥（`SystemStages`） | `NaturalLifeRegen` |
| 额外跳跃（`PlayerJumpHooks` / `ExtraJumpTable`） | 双跳八钩子（见 §2.10） |
| 玩家绘制 | `DrawEffects` / `ModifyDrawInfo` / `TransformDrawData` / `DrawPlayer` / `HideDrawLayers` / `ModifyDrawLayerOrdering` / 相机与缩放——入口在绘制管线（见 content-playerdrawlayer.md），实例分派复用本管线的每玩家数组 |

`ModPlayer` 上声明的每个钩子都有分派点；分派点不在本目录的（武器修正、发射与弹药、命中 NPC、治愈与魔力、染料商奖励池等）由对应管线的入口代派，实例同样从这里的每玩家数组取。

## 2. 我们的设计

### 2.1 文件分工

```
Pipelines/Player/
  PlayerPipeline.cs        主模块：安装门表 + 二十来个游戏入口的显式循环分派
  PlayerBinding.cs         模板表 + 每玩家实例数组 + 钩子位集
  PlayerUpdateHooks.cs     时精确更新族（幸运、移动前、跑速、杂项、暂停、染料/可见饰品）
  PlayerHurtHooks.cs       受伤与闪避链
  PlayerDamageSources.cs   受伤的来源侧（ModNpc/ModProjectile 的打玩家钩子）开关缓存
  PlayerNetPipeline.cs     联网生命周期与客户端状态通道
  PlayerSavePipeline.cs    侧车存档（§2.7）
  PlayerFishingHooks.cs    钓鱼条件、掷骰、扣饵、交付、渔夫奖励
  FishingLineHooks.cs      鱼线偏移与染色
  PlayerCraftingHooks.cs   合成原料注入
  PlayerNurseShopHooks.cs  护士三钩子与商店四钩子
  PlayerSlotHooks.cs       槽位悬停与 Shift 点击
```

伴生文件不另建实例表：都通过 `PlayerPipeline.Instance.Binding` 取同一份模板与实例。

### 2.2 实例保管

* 模板表：注册时按顺序追加，`Index` 就是它在数组里的下标（作者 API 不暴露 Index，这里只是内部映射）。
* 每玩家数组：`Dictionary<nint, ModPlayer[]>`，键是 `Player` 的对象指针——**不能用 `whoAmI`**：同一个槽位上的玩家会换人（退出重进、多人服务器），指针换人时自然失效。
* 数组按需变长：已有实例原样保留（作者的字段不能丢），新模板补建实例并 `Initialize()`。
* 实例的 `Player` 字段在建实例时写入，钩子里不再传第二遍。
* `Refresh`（模板集变化时）：重扫每个模板的覆写位集并**清空实例表**，下一次访问按新模板集重建——内容集变了，旧实例的状态不再可信。
* 位集门控：注册/刷新各扫一次覆写位集（`HookMask`），分派期 `HookEnabled` 是一次位运算；全体模板的位集并集 `Active(hook)` 是安装门与循环早退的依据——"哪个玩家身上有作者 A"退化成"数组下标 + 一次位运算"，不需要每帧遍历全部作者。

### 2.3 安装门与分派形状

每个钩子族一门（`PlayerPipeline.Gates`），条件与订阅同居一门、由 GateHub 派发：`Wanted` 问位集并集，非空才挂对应入口；药水、钓鱼、护士、受伤这几族还要看 `ModItem`/`GlobalItem`/`ModNpc`/`ModProjectile` 侧有没有人覆写（同一链上的另一半在那边）。没人用的族保持原样，普通玩家路径零开销。

分派一律是显式循环 + 直接调用。动机是热路径约束：这些钩子多数**每帧每玩家**（`Update`、`ResetEffects`、`UpdateEquips`、`UpdateLifeRegen`…），闭包或按名反射都会变成每帧一次的查找或堆分配；显式循环不产生任何分配，出错时堆栈直接指到具体钩子。代价是每个钩子要写一遍约十行的循环——钩子集合固定且都在同目录里，换来的是"读一个钩子就看完它的全部分派语义"，而钩子名拼错会直接编译失败，不会静默不分派。

### 2.4 否决型钩子的合并规则

否决票一律**全量 AND 不短路**：`CanUseItem`、`PreItemCheck`、`PreKill`、`CanBeTeleportedTo`、`OnPickup`、`PreModifyLuck`、`ApplyPotionDelay`，以及受伤族与商店的 `Can*` 查询——任一返回 false 就否决，但**所有实例都会跑**（后面的实例可能只是在做记录）。回调抛错的那一票按"没投否决票"处理。

`PreItemCheck` 的契约是这一族里最容易被想当然的：

* `PreItemCheck` 否决（返回 false）时**只跳过原版这一次物品检查**（不调 `orig`），玩家本帧不再执行原版的使用判定；
* `PostItemCheck` **随后无条件照常派发**——被否决了也跑。收尾类钩子依赖它清理每帧状态（清标志、递减计数）：如果否决会跳过收尾，一次否决就把实例留在脏状态，下一帧的行为不可预期。

其余否决点的"否决"各自落到明确的后果上：

* `PreKill` 否决：不调原版 `KillMe`，也**不发 `Kill`**——作者要求"别死"，就不该再收到"死了"。
* `CanUseItem` 否决：不调原版判定（与物品侧链取 AND 后交回原版返回值）。
* `CanBeTeleportedTo` 否决：这次传送不发生。
* `OnPickup` 否决：物品 `TurnToAirAndSync`——从地上消失、不进背包。
* `ApplyPotionDelay` 否决或延迟 ≤ 0：不进药水病（见 §2.6）。
* `PreModifyLuck` 否决：跳过原版那次幸运求和（瓢虫、火把、药水、风筝、钱币），`ModifyLuck` 照发。

### 2.5 上限修正

`ModifyMaxStats` 的时序是固定的：`ResetEffects` 入口里原版重置之后，先派作者的 `ResetEffects`，再让作者写 `ModifyMaxStats`（基数是 `statLifeMax`/`statManaMax`）；管线把各作者的 `StatModifier` 用 `CombineWith` 聚合成一个，然后把增量写回 `statLifeMax2`/`statManaMax2`（带下限 1/0）。聚合抛错只丢这一个作者，写入抛错记一条、这一帧的修正不生效。

### 2.6 药水链

药水病建立走的是 `AddBuff(PotionSickness)`，这条链被完整接管：

1. `Player.ApplyPotionDelay` 入口先建线程内作用域（`[ThreadStatic]`，记玩家、药水、`potionDelay` 前值）；
2. 原版照常跑——回血与消耗都在这一步完成；
3. 原版随之调用的 `AddBuff(PotionSickness)` 被拦下，按固定顺序问作者：`ModItem.ModifyPotionDelay` → `GlobalItem.ModifyPotionDelay`（`ref delay`）→ 夹回 ≥ 0 → `ModPlayer.ApplyPotionDelay(item, delay)` → `ModItem` → `GlobalItem`（后三个全量 AND）；
4. 任一否决或 `delay <= 0`：这次不加计时、不给药水病，作用域标 `Blocked`，`finally` 里把 `potionDelay` 回滚成前值；否则写入 `delay` 并照常 `AddBuff`。

嵌套与原版异常都在 `finally` 恢复外层状态；回血与消耗不因否决回滚。

### 2.7 侧车存档（playersave）

玩家的 mod 数据放在**侧车文件** `<玩家文件>.pl`：GZip 压缩的 `TagCompound` 二进制。侧车是既定设计：ModPlayer 的存读、发型、mod 物品与前缀**共用**这一个文件，云存档不写，写盘走 tmp + 原子替换。键是 `"Mod名/类名"`；它与其它加载器的玩家存档互不相通——换加载器后各自从空开始，不会崩。

时序与游戏自己的存档咬合：`InternalSavePlayerFile` 里 `PreSavePlayer` → 原版写 `.plr` → `PreSaveCustomData` → 逐作者 `SaveData`（空 compound 删键；全空则删侧车文件，不留空壳）→ `PostSavePlayer`（原版抛异常也发）。读档在原版 `LoadPlayer` 之后逐作者 `LoadData`，键不存在的不调。

### 2.8 联网同步（playernet）

补的是"我看别人"的那份 mod 状态通道：进世界的 `SyncPlayer`（原生同步之后）、`clientClone` 两个入口跟着原生的克隆走（派 `CopyClientState`）、本地每帧 `SendClientChanges` 与上一份克隆比对、`PlayerConnect`/`PlayerDisconnect` 生命周期、`EnterWorld` → `OnEnterWorld`。

### 2.9 钓鱼与鱼线

* `GetFishingConditions`：作者改的是**等级倍率**；最终等级按"基础 × 倍率"回推，与原版口径对不上时记一条并按商回退，避免倍率白改。
* `FishingCheck_ProbeForQuestFish`：`ModifyFishingAttempt` 在原版选鱼前改条件，掷骰结果字段原样保留；`FishingCheck_RollItemDrop`：`CatchFish` 在掷骰后回写物品/敌人结果与声纳弹窗。
* 浮标领奖入口建立线程内作用域，两个 `QuickSpawnItem` 的 Item 重载在原版交付前调 `ModifyCaughtFish` 改同一份鱼；先消费标志再调用作者，避免重载转调或作者另行生成物品导致重复；嵌套领奖与原版异常都在 `finally` 恢复外层状态。普通生成物品不触发钓获回调。
* `CanConsumeBait`：原版先完整跑（含扣饵），拒绝时按背包快照把扣掉的饵回补；交给作者的饵是找回来的实物，找不到时给占位物品。
* `AnglerQuestReward`：作用域收集稀有度倍率与奖励物品，作者改完统一发放。
* `FishingLineHooks` 复用已绑定的弹幕和手持物品实例，按覆写位集门控，依次执行弹幕、物品作者。偏移从零累加到原版 `mountedCenter`，两个竿端 `ref float` 始终传给原版并保留输出；颜色以原版鱼竿默认色初始化，传入染色入口后继续原版染色，嵌套和异常时恢复，无覆写的嵌套鱼线单独使用原版颜色。正常分派不创建闭包、列表或状态对象。

### 2.10 额外跳跃（ExtraJump）

一个内容类 + 一张状态表 + 六个游戏入口：

* **内容类** `Content/ExtraJump.cs`：作者派生 `ExtraJump`，声明 `GetDefaultPosition()`（After/Before 某个原版跳跃）与 `GetDurationMultiplier`；可选 `GetModdedConstraints`（同类排序约束，成环报错）、`ShowVisuals`、`CanStart`、`OnStarted`（可压音效）、`OnEnded`、`UpdateHorizontalSpeeds`、`OnRefreshed`。`Content/VanillaExtraJumps.cs` 是 10 个内置跳跃（五挂载 + 五瓶，先占号 0..9；Flipper 走 wet 特判没有 Player 字段）。
* **状态表** `Pipelines/ExtraJump/ExtraJumpTable.cs`：按玩家指针保管 `ExtraJumpState`（Enabled/Available/Active）。原版跳跃的状态在 Player 字段与状态表之间**双向镜像**（Push/Pull）：作者改状态表、游戏读自己的字段，游戏改字段作者也看得到。作者查询面（`GetJumpState`/`ExtraJumpStates`/`AnyExtraJumpUsable`/`StopExtraJumpInProgress`/`ConsumeAllExtraJumps`/`Get/SetBlockExtraJumps`）落在这个静态类上——游戏 `Player` 是 interop 类型，加不了部分类成员。
* **入口** `Pipelines/ExtraJump/PlayerJumpHooks.cs`：`ResetEffects`（重置启用标志）、`UpdateJumpHeight`（禁跳/离地清理）、`RefreshDoubleJumps`（落地回充）、`JumpMovement`（选跳与起跳）、`CancelAllJumpVisualEffects`、`DoubleJumpVisuals`，外加 `SoundEngine.PlaySound` 两重载（压原版起跳音效）。热路径靠 `Wanted` 门（有 mod 跳跃或八钩子有人覆写才进）；mod 跳跃的水平速度修正挂在 `HorizontalMovement` 入口最前。
* **ModPlayer 八钩子**：`ModifyExtraJumpDurationMultiplier/CanStartExtraJump/OnExtraJumpStarted/OnExtraJumpEnded/OnExtraJumpRefreshed/ExtraJumpVisuals/CanShowExtraJumpVisuals/OnExtraJumpCleared`——对原版跳跃同样生效（起跳播报、回充、视觉、清空都发链）。

### 2.11 自然生命再生（NaturalLifeRegen，中体桥）

游戏算自然再生没有可挂的 On 入口——数值是 `Player.UpdateLifeRegen()` 方法体中部的局部量。SystemStages 在 +0x828 打点（4 条指令指纹校验，指纹按本构建重提取）：桥读算到此点的再生值与玩家，逐作者 `NaturalLifeRegen(ref regen)`，改后且 finite 才写回，抛错/不写回就用原版的数。安装门是 ModPlayer 位集（没人覆写就不装桥）。

## 3. 取舍理由

* **为什么每个玩家一份实例而不是一份"状态对象"？** 作者的字段（冷却、计数、标志位）是每玩家独立的；共享一份就得让作者自己用字典，那本框架该做的事。
* **为什么键是对象指针而不是 `whoAmI`？** 槽位会换人，按槽位键保留的实例会把上一个玩家的字段带给下一个玩家；指针随对象生灭，换人自然失效、数组自然重建。
* **为什么显式循环而不用委托/反射？** 每帧每玩家的钩子容不下闭包分配和按名查找；显式调用零分配、堆栈直指钩子、钩子名拼错编译期就报。
* **为什么否决票全量 AND 不短路？** "能不能用"是聚合裁决，而每个作者还要靠这次调用做记录或收尾；短路会把后面的作者留在不知情的状态。`PostItemCheck` 无条件派发是同一原则的另一半：查询可以否决，收尾不能被跳过。
* **为什么 `PreKill` 一票否决连 `Kill` 都不发？** 否决的语义是"这次没死"；发 `Kill` 等于向作者确认一个假事实。
* **为什么 `Spawn` 只发 `OnRespawn` 且限定死亡重生？** 本构建的 `Player.Spawn` 覆盖进世界、复活等多种用途，而作者 API 里"复活"与"进世界"是两件事；把进世界也算成复活会重复触发。`OnEnterWorld` 的入口在联网管线（`EnterWorld`）。
* **为什么开局背包按 mod 分组再摊平？** `AddStartingItems` 只添自己的，`ModifyStartingInventory` 要看得到全部来源才能调整；分组字典是两阶段的交界，摊平时前 50 格按序回填、溢出的走掉落。
* **为什么侧车共用一个文件？** 玩家相关的持久数据（ModPlayer、发型、mod 物品、前缀）都在同一次读写时机上，分开文件只会多几次打开与半写状态；一个文件 + 原子替换让"存了一半"不可能出现。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| 任意作者钩子抛错 | 记一条；只丢这一个作者这一次，其余照跑。主模块把同一（作者类，钩子）的重复异常折叠成一条，避免每帧刷屏；伴生文件逐条报 |
| `Initialize` 抛错 | 记一条；该实例仍然入表（否则后续钩子会每次都重建实例） |
| `ResetEffects` 抛错 | 该实例这一帧的效果没重置（可能残留），玩家的其它逻辑不受影响 |
| 否决型钩子抛错 | 该票按"没投否决票"处理：`PreKill` 照常死、`PreItemCheck` 照常检查、`PostItemCheck` 照常派发 |
| `ModifyMaxStats` 写入抛错 | 记一条；这一帧的上限修正不生效 |
| `SaveData` / `LoadData` 抛错 | 记一条；该作者的键这次跳过，其余作者照常，游戏存档流程不受影响 |
| 侧车读不出来 | 记一条（每文件一次）；按空档案处理，游戏照常 |
| 实例数组键失效（玩家对象被换掉） | 新玩家第一次问到时新建一份，旧数组随指针回收 |

## 5. 扩展方式

新增玩家钩子：`ModPlayer` 加 `virtual` → `HookId` 末尾追加 → 在对应游戏入口（主模块或伴生文件）加一段显式循环；属于新族的再配一个安装门。新增一个"每玩家数组"类的内容（例如 GlobalPlayer）时，照 `PlayerBinding` 的形状再写一份即可。

## 6. 与其它管线的边界

* **Buff 管线**：buff 的施加与每帧效果归 Buff 管线；本管线的 `ResetEffects` 承载作者的每帧重置与上限修正。
* **物品管线**：使用链的物品侧（`CanUseItem` 的 `ModItem`/`GlobalItem` 一半、发射、弹药、武器修正）归物品与伤害管线；本管线只管 `Player.CanUseItem`/`ItemCheck` 这两个玩家入口上的玩家链。
* **装备管线**：防具套装、翅膀、贴图槽归装备管线；`PostUpdateEquips`/`FrameEffects` 由装备管线的入口代派到本管线实例。
* **玩家绘制管线**：绘制族钩子（含 `HideDrawLayers`、层排序、相机与缩放）的入口在绘制管线，实例与门控复用本管线的每玩家数组。
* **NPC / 弹幕管线**：捕捉链（`CanCatchNPC`/`OnCatchNPC`）与"玩家打 NPC"的命中族归 NPC/弹幕管线；"NPC/弹幕打玩家"的来源侧开关在 `PlayerDamageSources`，链本体在 `PlayerHurtHooks`。
* **联网与存档**：联网生命周期与侧车存档是本目录的伴生管线（§2.7、§2.8），共享同一份实例表，不与其它管线的存档路径交叉。
* **额外跳跃**：状态表与跳跃入口自成一族（§2.10），实例分派与门控仍走玩家管线的基础设施。
