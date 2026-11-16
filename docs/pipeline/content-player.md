# 内容管线：玩家（Player）

## 1. 职责

玩家管线的产品不是"一个新 id"，而是一组**按玩家存在的实例**：作者的 `ModPlayer` 每个玩家一份，由游戏的各种玩家入口分派。

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `Player.ResetEffects` | `ResetEffects`、`ModifyMaxStats`（可写 `CumulativeHealth/CumulativeMana`） |
| `Player.UpdateEquips` | `UpdateEquips`，之后把 `CumulativeHealth/CumulativeMana` 加到上限 |
| `Player.UpdateLifeRegen` / `UpdateManaRegen` | `UpdateLifeRegen` / `UpdateManaRegen` |
| `Player.UpdateDead` | `UpdateDead` |
| `Player.Update` | `PreUpdate`（原版之前）、`PostUpdate`（原版之后） |
| `Player.KillMe` | `PreKill`（可否决）、`Kill`（之后） |
| `Player.ItemCheck` / `CanUseItem` | `PreItemCheck`、`PostItemCheck`、`CanUseItem` |
| `Player.ApplyPotionDelay` | `ApplyPotionDelay`（false = 不加药水病计时） |
| `Player.RefreshInfoAccs` | `ResetInfoAccessories` |
| `Player.KeyDoubleTap` / `KeyHoldDown` | `ArmorSetBonusActivated` / `ArmorSetBonusHeld` |
| `Player.TrySwitchingLoadout` | `OnEquipmentLoadoutSwitched` |
| `Player.Spawn` | `OnRespawn` |
| `Player.Teleport` | `CanBeTeleportedTo` |
| `Player.PickupItem` | `OnPickup` |
| `Projectile.FishingCheck_ProbeForQuestFish` / `FishingCheck_RollItemDrop` | `ModifyFishingAttempt` 前置修改条件，保留掷骰结果；`CatchFish` 后置回写结果 |
| `Projectile.AI_061_FishingBobber_GiveItemToPlayer` → `Player.QuickSpawnItem` 的两个 Item 重载 | `ModifyCaughtFish` 在原版交付前修改同一物品，仅消费一次 |
| `Main.DrawProj_FishingLine` / `TryApplyingPlayerStringColor` | 弹幕 → 手持物品 `ModifyFishingLine`，回写竿端偏移及染色前颜色 |
| `Player.NaturalLifeRegen` 的再生数值 | 中体桥（UpdateLifeRegen+0x828，见下方"自然生命再生"） |

**已接（移到玩家绘制管线）**：`DrawEffects` / `ModifyDrawInfo` / `TransformDrawData` / `DrawPlayer`（见 content-playerdrawlayer.md）与 `HideDrawLayers`（`VanillaDrawLayer` 逐层点名，54 层）

**不接**：`HoverSlot/ShiftClickSlot`（声明在、分派未接——物品槽位 UI 入口归物品/界面管线后续批次核对）；`CanCatchNPC/OnCatchNPC`（基线有、本版未声明，见 API 复检清单）。其余历史上列在这里的钩子（buff 更新前后、杂项后置/跑速/移动前、暂停更新、FrameEffects、幸运、捕鱼与奖励、商店四钩子、护士三钩子、ProcessTriggers、存档与同步族）都已接线——接线批次见 audit 台账 §7 与 §8 表，别再按这份旧清单理解。

理由统一为一句话：**入口不存在就不假装接上**。旧实现把它们都声明成 `virtual` 却从不分派，作者写了也静默失效——那比不提供更糟。API 上仍然保留这些 `virtual`（删掉会破坏已有 Mod 编译），但文档与启动日志都明说"本版不分派"。

## 2. 我们的设计

### 2.1 两个文件

```
Pipelines/Player/
  PlayerPipeline.cs   模块：二十来个游戏入口 → 显式循环分派
  PlayerBinding.cs    模板表 + 每玩家实例数组 + 位集
```

### 2.2 实例保管

* 模板表：注册时按顺序追加，`Index` 就是它在数组里的下标（作者 API 不暴露 Index，这里只是内部映射）。
* 每玩家数组：`Dictionary<nint, ModPlayer[]>`，键是 `Player` 的对象指针——**不能用 `whoAmI`**：同一个槽位上的玩家会换人（退出重进、多人服务器），指针换人时自然失效。
* 数组变长：新模板补建实例并 `Initialize()`；老实例原样保留（作者的字段不能丢）。
* 实例的 `Player` 字段在建实例时写入，钩子里不再传第二遍。

### 2.3 分派为什么不用委托/反射

参考实现用 `Action<ModPlayer>` + 闭包捕获 `player`/`item` 之类的参数。我们的热路径约束更严：

* 这些钩子多数是**每帧每玩家**（`Update`、`ResetEffects`、`UpdateEquips`、`UpdateLifeRegen`…），闭包会变成每帧一次的堆分配；
* 显式循环 + 直接调用不产生任何分配，而且出错时的堆栈直接指到具体钩子。

代价是每个钩子要写一遍循环（约十行）。因为钩子数量是固定的、且都写在同一个文件里，这个代价换来的是"读一个钩子就看完它的全部分派语义"。

### 2.4 门控

每个模板在注册/刷新时算一次 `HookMask`（对照基类 `ModPlayer`），分派时先问 `HookEnabled`。这样"哪个玩家身上有作者 A"这个问题退化成"数组下标 + 一次位运算"，不需要每帧遍历全部作者。

### 2.5 否决型钩子的合并规则

* `CanUseItem` / `PreItemCheck` / `PreKill` / `CanBeTeleportedTo` / `OnPickup`：**与**语义——任一返回 false 就否决，但**所有实例都会跑**（后面的实例可能只是在做记录）。
* `ApplyPotionDelay`：任一 false 就跳过原版（不加计时、不给药水病），回血与消耗照旧。
* `PreKill` 否决时**不调原版 `KillMe`**，也**不调 `Kill`**：作者要求"别死"，就不该再收到"死了"。

### 2.6 上限加成

`CumulativeHealth/Mana` 的时序是固定的：`ResetEffects` 里清零 → 作者在 `ModifyMaxStats` 里加 → `UpdateEquips` 之后由管线加到 `statLifeMax2/statManaMax2`。

**为什么加在 `UpdateEquips` 之后？** 因为 `statLifeMax2` 是游戏在 `UpdateEquips` 里算出来的（生命水晶 + 饰品 + buff）。在更早的地方加会被覆盖，在更晚的地方加则这一帧的再生已经用旧值算过了。

### 2.7 捕鱼与鱼线

`PlayerFishingHooks` 在浮标领奖入口建立线程内作用域，按玩家原生指针匹配，两个 `QuickSpawnItem` 的 Item 重载在原版交付前修改同一份鱼。先消费标志再调用作者，避免重载转调或作者另行生成物品导致重复；嵌套领奖与原版异常都在 `finally` 恢复外层状态。普通生成物品不触发钓获回调。

`FishingLineHooks` 复用已绑定的弹幕和手持物品实例，按覆写位集门控，依次执行弹幕、物品作者。偏移从零累加到原版 `mountedCenter`，两个竿端 `ref float` 始终传给原版并保留输出。颜色以原版鱼竿默认色初始化，传入 `TryApplyingPlayerStringColor` 后继续原版染色。线程内颜色在嵌套和异常时恢复；无覆写的嵌套鱼线单独使用原版颜色。正常分派不创建闭包、列表或状态对象；每个作者异常独立报告。

其余捕鱼与奖励回调仍需逐条核对；这条短链完成不代表捕鱼系统整体完成。

## 3. 取舍理由

* **为什么不用参考实现的"方法名字符串"分派？** 那套要 `ModPlayer` 的每个钩子都通过反射按名字找；我们要的是"零分配 + 编译期可检查"。显式调用在新增钩子时多写几行，但拼错钩子名会编译失败，而不是静默不分派。
* **为什么每个玩家一份实例而不是一份"状态对象"？** 作者的字段（冷却、计数、标志位）是每玩家独立的；共享一份就得让作者自己用字典，那本框架该做的事。
* **为什么未接的钩子还留着 `virtual`？** 删掉会让已有 Mod 编译不过。留着的代价只是"作者以为能用"，所以启动日志里逐条列出未接的名字——让沉默变成可见。
* **为什么 `Spawn` 只分派 `OnRespawn`？** 手机端 `Player.Spawn` 覆盖了进世界、复活、传送三种用途，作者 API 只有 `OnRespawn` 与 `OnEnterWorld`；把三种都当复活会让"进世界"也被算成复活。`OnEnterWorld` 本版没有入口，所以不进列表。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| `Initialize` 抛错 | 记一条；该实例仍然入表（否则后续钩子会每次都重试建实例） |
| 任意钩子抛错 | 记一条；只丢这一个作者，其余照跑 |
| `ResetEffects` 抛错 | 该实例这一帧的效果没重置（可能残留），但玩家的其它逻辑不受影响 |
| `PreKill` 抛错 | 视为 true（照常死） |
| 实例数组键失效（玩家对象被换掉） | 新玩家第一次问到时新建一份，旧数组随指针回收 |

## 5. 扩展方式

新增玩家钩子：`ModPlayer` 加 `virtual` → `HookId` 末尾追加 → 在 `PlayerPipeline` 找到对应游戏入口加一段循环。新增一个"每玩家数组"类的内容（例如 GlobalPlayer）时，照 `PlayerBinding` 的形状再写一个即可。

## 6. 与其它管线的边界

* **Buff 管线**：buff 的施加与每帧效果归 Buff 管线；本管线的 `ResetEffects` 只重置"饰品每帧打开的东西"。
* **物品管线**：物品的使用链归物品管线；本管线只在 `Player.CanUseItem`/`ItemCheck` 这两个玩家入口上分派。
* **坐骑管线**：骑乘与坐骑绘制归坐骑管线。
* **触屏按钮 / 键位管线**：输入读取归它们；本管线不接 `ProcessTriggers`（本版无入口）。

## 2026-11-17 批：玩家侧接线

- OnHitAnything（On.Player.OnHit 后通知）。
- ApplyPotionDelay 重排为基线链：作用域记进前值 → AddBuff(PotionSickness) 里 ModifyPotionDelay（ModItem → GlobalItem）→ Clamp → ApplyPotionDelay 全员（ModPlayer → ModItem → GlobalItem）→ 否决/≤0 不进药水病。
- 开局背包：SetupStartingItems 后 + DropItems(mediumcore) 后 Starting(byMod 摊平回前 50 格)；AddMaterialsForCrafting blocked（缺原生分配工具）。
- 生命周期钩子（PostStaticDefaults/PostSetDefaults/PostSetupContent）、UpdateManaRegen、ModPlayer.AddDrawData、NPCHeadSlot、ApplyBuffImmunity 已删。

## 额外跳跃（ExtraJump）

一个内容类 + 一张状态表 + 六个游戏入口：

* **内容类** `Content/ExtraJump.cs`：作者派生 `ExtraJump`，声明 `GetDefaultPosition()`（After/Before 某个原版跳跃）与 `GetDurationMultiplier`；可选 `GetModdedConstraints`（同类排序约束，成环报错）、`ShowVisuals`、`CanStart`、`OnStarted`（可压音效）、`OnEnded`、`UpdateHorizontalSpeeds`、`OnRefreshed`。`Content/VanillaExtraJumps.cs` 是 10 个内置跳跃（五挂载 + 五瓶，先占号 0..9；Flipper 走 wet 特判没有 Player 字段）。
* **状态表** `Pipelines/ExtraJump/ExtraJumpTable.cs`：按玩家指针保管 `ExtraJumpState`（Enabled/Available/Active）。原版跳跃的状态在 Player 字段与状态表之间**双向镜像**（Push/Pull）：作者改状态表、游戏读自己的字段，游戏改字段作者也看得到。作者 API（`GetJumpState`/`ExtraJumpStates`/`AnyExtraJumpUsable`/`StopExtraJumpInProgress`/`ConsumeAllExtraJumps`/`Get/SetBlockExtraJumps`）落在这个静态类上——基线的 `Player.GetJumpState` 部分类在互操作侧加不上去。
* **入口** `Pipelines/ExtraJump/PlayerJumpHooks.cs`：`ResetEffects`（重置启用标志）、`UpdateJumpHeight`（禁跳/离地清理）、`RefreshDoubleJumps`（落地回充）、`JumpMovement`（选跳与起跳）、`CancelAllJumpVisualEffects`、`DoubleJumpVisuals`，外加 `SoundEngine.PlaySound` 两重载（压原版起跳音效）。直接安装，热路径靠 `Wanted` 门（有 mod 跳跃或八钩子有人覆写才进，等价按需安装）；mod 跳跃的水平速度修正挂在 `HorizontalMovement` 入口最前。
* **ModPlayer 八钩子**：`ModifyExtraJumpDurationMultiplier/CanStartExtraJump/OnExtraJumpStarted/OnExtraJumpEnded/OnExtraJumpRefreshed/ExtraJumpVisuals/CanShowExtraJumpVisuals/OnExtraJumpCleared`——对原版跳跃同样生效（起跳播报、回充、视觉、清空都发链）。

### 自然生命再生（NaturalLifeRegen，中体桥）

游戏算自然再生没有可挂的 On 入口——数值是 `Player.UpdateLifeRegen()` 方法体中部的局部量。
SystemStages 的第 10 座桥在 +0x828 打点（4 条指令指纹校验，指纹按本构建重提取）：桥读 v9
（算到此点的再生值）与 x19（玩家），逐作者 `NaturalLifeRegen(ref regen)`，改后且 finite
才写回 v9，抛错/不写回就用原版的数。安装门是 ModPlayer 位集（没人覆写就不装桥）。
