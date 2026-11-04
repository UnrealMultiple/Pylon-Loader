# 内容管线：NPC（含城镇 NPC）

## 1. 职责

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `NPC.SetDefaults` | 原版之后叠加：`SetDefaults` → `ApplyBuffImmunity` → `PostSetDefaults`，收尾后全类型分派 `GlobalNpc.SetDefaults` |
| `NPC.AI` | `GlobalNpc.PreAI` → `ModNpc.PreAI`（可否决）→ 游戏 AI（`AIType` 期间换 type）→ `ModNpc.AI` → `GlobalNpc.AI` → `ModNpc.PostAI` → `GlobalNpc.PostAI` |
| `NPC.NewNPC` | `OnSpawn`（本 NPC 先，Global 后） |
| `NPC.UpdateNPC_BuffSetFlags` | `ResetEffects`；顺手记下"覆写了 `DrawBehind`"的实体 |
| `NPC.checkDead` / `CheckActive` | `CheckDead` / `CheckActive`（任一 false 不调原版） |
| `NPC.NPCLoot` | `PreKill`（可否决；联机客户端不跑）→ 原版掉落 → `OnKill` |
| `NPC.HitEffect` | `HitEffect`（原版之后） |
| `NPC.ScaleStats_ByPlayerCount` | `ApplyDifficultyAndPlayerScaling`（原版之后） |
| `NPC.FindFrame` | `FindFrame`；覆写时整段接管，否则按 `AnimationType` 借原版动画分支，跑完再补跑 Global |
| `NPC.GetChat` | `GetChat`（本 NPC 先换文本，再让 Global 改 ref） |
| `Main.DrawNPCs` / `Main.DrawNPCDirect` ×2 / `NPC.GetNPCColorTintedByBuffs` / `NPC.GetAlpha` | `DrawEffects`、`PreDraw`、`PostDraw`、`GetAlpha`；mod 类型由本管线自己补画 |
| `NPC.GetBossHeadTextureIndex` / `GetBossHeadRotation` / `GetBossHeadSpriteEffects` | 首领头像三件 |
| `Main.CacheNPCDraws` | `DrawBehind` |
| 城镇：`Main.UpdateTime_SpawnTownNPCs` / `WorldGen.CheckSpecialTownNPCSpawningConditions` / `NPC.getNewNPCName` / `GUINPCDialogue.*` | `CanTownNPCSpawn`、`CheckConditions`、`SetNPCNameList`、`SetChatButtons`、`OnChatButtonClicked`、`AddShops`（安装期一次） |

**不接**，原因写进 `ModNpc` / `GlobalNpc` 的注释，作者读 API 就能看到：命中套（`CanHitNPC` / `ModifyHitByItem` …，缺 `HitModifiers`/`HurtModifiers`）、幸福度、`CanChat`（`get_CanTalk` 是属性 getter，代理里没有可挂入口）、自然生成（`SpawnChance` / `SpawnNPC`）、旗帜/图鉴/头像注册、`SendExtraAI` / `ReceiveExtraAI`（归网络管线）。

## 2. 我们的设计

### 2.1 文件拆分

```
Pipelines/Npc/
  NpcPipeline.cs   模块：AI / 事件 / 绘制 / 首领头像分派，持有跨钩子上下文
  NpcDefaults.cs   NPC.SetDefaults 接管 + 按类型的帧数与贴图尺寸缓存
  NpcBinding.cs    NPC 实体 ↔ ModNpc 实例 + 每类型覆写位集的缓存
  NpcTown.cs       城镇 NPC：入住、名字、房间条件、聊天按钮与商店
```

与参考实现的对应关系：`NpcPipeline` ≈ `NPCLoader.Lifecycle` + `Draw` + `Loot` + `BossHeads` + `Extra`；`NpcDefaults` ≈ `NPCDefaultsHooks` + `Installed`；`NpcBinding` ≈ `InstanceOf`/`Bind` + `EntityInstances`；`NpcTown` ≈ `NPCLoader.Town`。

**为什么我们不按子系统切十二个文件？** 我们能用上的时机只有参考实现的一半（命中、生成、幸福度、商店 hook 都缺入口），切成十二个文件会让一半文件只有两三行。保留的切法是**按生命周期切**：默认值（一次/每只）、绑定（每只）、分派（每帧）、城镇（每天/每次点击）——这四条生命周期的"何时失效"完全不同，混在一个文件里最容易写出串味的代码。城镇单独成文件还有一个硬理由：它与敌对 NPC **不共享任何游戏时机**，用的却是同一个内容基类，是最典型的"同基类不同生命周期"。

### 2.2 三层数据

| 层 | 生命周期 | 存哪 |
|---|---|---|
| 模板 | 进程 | `ContentRegistry` 槽表；`ModNpc.Hooks` 位集挂在模板上 |
| 实体实例 | 跟随 `NPC` 实体 | `NpcBinding`（指针 → 实例），`SetDefaults` 时强制 `Rebind` |
| 覆写位集 | 类型 | `HookScan.Of(具体类型, typeof(ModNpc))`，按类型缓存一次，绑定时**复制**到实例 |

位集这一行是照着参考实现的 `npc.Hooks` 抄的机制、不是抄的代码：旧实现里 `ModNpc.HookPlan` 每次调用都走 `HookScan.Of`（带锁 + 字典查找），而 NPC 的 AI/绘制是每帧每只的热路径。现在位集在绑定时随实例复制，分派期只是一次 `ulong` 位运算。

**为什么位集要按 `typeof(ModNpc)` 而不是 `typeof(ModType)` 算？** `HookScan` 的语义是"声明类型不是基类才算覆写"。以 `ModType` 为基类时，`ModNpc` 自己声明的那些空 `virtual` 也会被算成"覆写"，于是每个 NPC 都"覆写了"全部钩子，门控等于没有。参考实现的 `NPCEventHooks.Install(类型, 基类)` 也是这个道理。

### 2.3 分派顺序

NPC 与弹幕相反，是 **Global 先、本 NPC 后**（`AI` / `PostAI` 除外，本 NPC 先）：

| 钩子 | 顺序 | 短路 |
|---|---|---|
| `PreAI` | Global → 本 NPC | 任一 false：游戏 AI 与 `AI` 都不跑，`PostAI` 照跑 |
| `AI` / `PostAI` | 本 NPC → Global | 无 |
| `PreKill` | Global → 本 NPC | 任一 false：丢掉原版掉落与 `OnKill` |
| `CheckDead` / `CheckActive` | 本 NPC → Global | 任一 false 不调原版 |
| `PreDraw` | Global → 本 NPC | 任一 false 不画精灵，`PostDraw` 照跑 |
| `DrawEffects` | 本 NPC → Global | 无（画色是这只 NPC 本身长什么样） |
| `GetAlpha` | Global → 本 NPC | 第一个非 null 胜出 |

### 2.4 绘制：补画与帧

原版 `Main.DrawNPCs` 的循环里有 `type > NPCID.Count - 1` 这类比较，IL2CPP 托管层改不了。管线在 `DrawNPCs` 外面挂一层：自己遍历 `Main.npc`，把 mod 类型逐个交给 `DrawNPCDirect`（两趟 `behindTiles` 各画各的层），再调原版。副作用是 `PreDraw` 的返回值必须真的表达"不画精灵"，不能靠"跳过调用"。

`AnimationType` 要临时把原版槽位的贴图与帧数指向自己、把 `npc.type` 换过去，跑完原版再还原；三处必须一起换、一起还。上一次还原失败（原版 `FindFrame` 抛错）会在下一次换位前补一次还原，不让槽位被永久占用。

### 2.5 跨钩子上下文

`DrawEffects` 改过的画色要在 `orig(DrawNPCDirect)` 内部被 `GetNPCColorTintedByBuffs` 读到。用管线实例上的**定长结构体栈**（深度 2）承载，而不是每次 `new` 一个帧对象：这是每只 NPC 每帧都走的路。

## 3. 取舍理由

* **为什么 `SetDefaults` 不跳过原版（弹幕管线跳过）？** 原版对未知弹幕会把弹幕判死，对未知 NPC 则只是提前 `return`，但**它是唯一会写 `scale=1` / `color=White` / `alpha=0` 的地方**——跳过之后 `new NPC()` 的 `color` 是透明黑，绘制时整只 NPC 是透明的（真机踩过）。所以走"原版先行 + 显式补出厂值"：`ResetDefaults` 把那批字段写回 `new NPC()` 的值，等价于原版本该做的事。
* **为什么要补 `defLifeMax`？** 原版 `ScaleStats` 对 `friendly` / `townNPC` 提前退出，不写 `defLifeMax`；留 0 时城镇 NPC 的 `AI_007` 会把 `lifeMax` 清零。
* **为什么 `ScaleStats` 要传两个空 `Nullable`？** interop 里 `Nullable` 是引用包装，填值等于把托管指针交给原生；只能传"不覆盖"。
* **为什么 `DrawBehind` 要单独记一个集合？** 它的时机是 `Main.CacheNPCDraws`，那时早已离开 `ResetEffects`；而"这只 NPC 要不要进绘制缓存"只有 `ResetEffects` 期间知道。所以那时按 `whoAmI` 记一笔，`CacheNPCDraws` 时逐个调，实体消失或换类型就把记录删掉。
* **为什么 `RebindTo` 存在？** `ModNpc.CloneDefaults` 借用另一个类型的 `SetDefaults`，期间绑定会被换成克隆类型的实例；不换回来，这只 NPC 从此用别人的钩子。这是参考实现用 `Restore(npc, instance, globals)` 表达的同一件事。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| `ModNpc.SetDefaults` / `ApplyBuffImmunity` 抛错 | `ContentErrors.Report` 记一条；NPC 保持出厂值，仍然 `active` |
| 任一 `GlobalNpc.SetDefaults` 抛错 | 只丢这一个订阅者 |
| `PreAI` 抛错 | 视为放行（游戏 AI 与 `AI` 照跑） |
| `AI` / `PostAI` 抛错 | 只丢这一次调用 |
| `CheckDead` / `CheckActive` 抛错 | 视为 true |
| `PreKill` 抛错 | 视为放行，原版掉落照跑 |
| 绘制类抛错 | 画原版精灵、用原版颜色 |
| 城镇：`CanTownNPCSpawn` 抛错 | 这一只不入住 |
| 城镇：`OnChatButtonClicked` 抛错 / 打开未注册的店 | 吞掉这次点击，不开任何店，记一条 |

## 5. 扩展方式

新增一个 NPC 钩子：`HookId` 末尾追加 → `ModNpc` / `GlobalNpc` 加同名 `virtual` → 在 `NpcPipeline` 的游戏钩子里分派（本 NPC 走 `binding` + 实例位集，Global 走 `GlobalNpcRegistry.For`）→ 文档补一行。城镇侧新增时机只改 `NpcTown`。

## 6. 与其它管线的边界

* **弹幕 / 物品**：NPC 发射的弹幕、掉落的物品各归其管线；本管线只提供 `OnSpawn` 与 `ModifyNPCLoot` 的接入点。
* **音乐**：`ModNpc.Music` / `SceneEffectPriority` 由音乐管线读取。
* **网络**：`SendExtraAI` / `ReceiveExtraAI` 归网络管线；聊天按钮点击是本地行为。
* **安装管线**：静态默认值、样例、贴图槽、商店登记归安装管线；本管线不写表。
