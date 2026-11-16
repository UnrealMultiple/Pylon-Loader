# 内容管线：NPC（含城镇 NPC）

## 当前续接：地图头像（2026-11-12）

`NpcHeadHooks` 在 NPC 样例安装后加载 `HeadTexture` / `ShimmerHeadTexture` / `BossHeadTexture`。空路径按 NPC 主贴图的 `_Head.png` / `_Shimmer_Head.png` / `_Head_Boss.png` 查可选资源；显式路径加载失败单独上报，不中断其它头像或 NPC。

城镇头像分配到 `TextureAssets.NpcHead` 尾部，接入真实 `NPC.TypeToDefaultHeadIndex(int)` 与 `TownNPCProfiles.GetHeadIndexSafe(NPC)` 查询；实体微光头像不存在时用普通头像。`NPCHeadSlot()` 覆写按位集调用，可返回 -1 隐藏。首领头像进入 `TextureAssets.NpcHeadBoss`，写入 `NPCID.Sets.BossHeadTextures`，并作为现有 `GetBossHeadTextureIndex` 回调链的默认值；作者 `BossHeadSlot` 仍可修改，作者异常逐个隔离。表维护事件恢复头像槽与映射，重复安装保持槽号。

这补齐头像资源与查询链，尚未真机确认三种地图模式的最终绘制，也没有补齐房屋菜单的头像排序/头像反查表。`Mod.AddBossHeadTexture` 动态注册 API 仍未实现。下文有历史实施快照，涉及“头像注册未迁移”“自然生成/命中未接”的结论应以当前源码与审计 §0 为准。

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

## 2026-11-17 批：B8 补全接线（三波）

- 已接：SpecialOnKill（NPCLoot/DropEoWLoot 前缀 gate，GlobalNpc 先 ModNpc 结论）、ModifyTypeName（get_TypeName）、CanFallThroughPlatforms（Nullable 链）、CanBeCaughtBy/OnCaughtBy（ItemCheck_CatchCritters 整段接管，基线循环照抄）、UsesPartyHat、DrawHealthBar（实体血条阶段作用域 + FindHealthBarOwner 轮转配对）、ModifyHoverBoundingBox/PreHoverInteract（HoverOverNPCs 暂移/暂藏恢复）、ModifyActiveShop（SetupShop 后快照 Item[]，写回补 isAShopItem）、NeedSaving/SaveData/LoadData（NpcSavePipeline 侧车行 "d" 键）。
- blocked：PartyHatPosition/ChatBubblePosition（原生绘制调节桥）、TownNPCProfile（缺 ITownNPCProfile 具体实现）。

## 2026-11-17 批：T4 死亡消息五入口链

- 已接：`Pipelines/Npc/NpcDeathMessageHooks.cs`——`checkDead` / `DoDeathEvents_CelebrateBossDeath` 换入换出线程死亡状态（dying / deathMessageDone，仅覆写了 ModifyDeathMessage 的 NPC 或存在全局覆写时）；`Main.NewText` 两重载与 `Chat.ChatHelper.BroadcastChatMessage` 三个出口在城镇死色(255,25,25)/首领死色(175,75,255)上截获：先按 `ModNpc.DeathMessage` 属性拼字面文本，再走 ModifyDeathMessage 链——全局先（返回 false = 整条吞掉），ModNpc 后（返回 false = 吞掉，true = 用 custom/color 放行）。

## 2026-11-17 批：T3 雕像对（CanGoToStatue/OnGoToStatue）

- 已接：王座/后座雕像电线整段接管（`Pipelines/Npc/NpcStatueWire.cs`，挂在 TilePipeline 的 `Wiring.HitWireSingle` 作用域内）。识别 105 号雕像格（style 40=王座 / 41=后座）→ SkipWire 2×3 → CheckMech(300) → 候选收集（覆写 `CanGoToStatue` 的 mod NPC 作者定论：ModNpc bool、GlobalNpc bool?（false 否决/true 拍板/null 交原版类型表）→ 随机选一只 Teleport(style 14) 到雕像脚下 → `OnGoToStatue` 链。风格命中即接管，CheckMech 失败也不走原版。
- 同批补：电线作用域（基线 TileHooks.WireScope 同款，`TilePipeline.WireScope`）——同点重入放行、owner 释放时派发 ModTile/GlobalTile 的 `HitWire`（对齐基线改到原版之后）；构造期派发 `GlobalTile.PreHitWire`。
- 本批追删：NPCHeadSlot、ApplyBuffImmunity、HideFromBestiary/BestiaryRarityStars/BestiaryIcon、HideFromModMenu、CloneDefaults、NPCValue。

## 2026-11-17 批：T2 镇攻八件套（中体桥）

- 已接：TownNPCAttackStrength/Cooldown/Proj/ProjSpeed/Shoot/Magic/Swing + DrawTownAttackGun/DrawTownAttackSwing + 全局 BuffTownNPC（`Pipelines/Npc/NpcTownAttackHooks.cs`）。游戏 AI_007 本来就有城镇 NPC 的通用攻击段（投掷/开枪/施法/挥舞），桥在其 4 个攻击取值点、3 个散布调用点、1 个光环调用点、1 个挥舞宽高调用点、1 个命中盒修正点、DrawNPCExtras 2 个绘制点共 12 处中体拦截（指纹校验同 SystemStages；16 字节指纹逐字核对过本仓库 libil2cpp.so），作者链改完参数写回寄存器/栈，原版指令照常执行。
- 门控：每类型位表（声明了攻击钩子的 mod NPC 才建作用域/分派）；有 BuffTownNPC 全局时全体安装（伤害乘数要作用于原版城镇 NPC 攻击，基线 InstallAll 同款）。
- 语义照基线 NpcTownHooks.cpp + TownAttackHooks.cs：AttackArgs 逐 mode 的寄存器/栈槽映射、伤害先乘 TownDamage 再走作者链、extra/宽高钳 ≥1、random/aura 非有限回退、scope（mode+改后参数）供散布/光环/挥舞桥读取。
- CanChat（基线 kind 6）核对并对齐：ForceCanTalk 改为"覆写了 CanChat 的 mod NPC 由作者定论（异常回原版），全局 false 一票否决 / true 拍板 / null 不参与"。
- 样例：PipelineTest 的 TestTownNpc 加投掷样式四钩子（Strength/Cooldown/Proj/ProjSpeed），真机验收看攻击参数与日志。

## 2026-11-17 批：T15 表情全局链 + T17 城镇 NPC 档案

- GlobalNpc 表情两钩子补链（`Pipelines/EmoteBubble/EmoteBubblePipeline.cs`）：`PickEmote` 与
  `EmoteBubblePosition` 从"只派 ModNpc"扩为「ModNpc 先答 → GlobalNpc 逐个补答」（基线
  NPCLoader.Emotes 链；PickEmote 非 null 直接换掉结果，一方抛错不拦后面的）。
- 城镇 NPC 外观档案已接（`Pipelines/Npc/NpcProfilePipeline.cs`，安装步"城镇 NPC 档案"）：
  本构建的 `Terraria.GameContent.TownNPCProfiles` 档案系统是活的——`RerollVariation`（变体摇号）、
  `GiveTownUniqueDataToNPCsThatNeedIt`（命名）、`DrawNPCCheckAlt`（贴图）、`GetHeadIndexSafe`
  （头像）都查它的表。作者在 `ModNPC.TownNPCProfile()` 返回**游戏自带的具体实现**
  （Profiles.LegacyNPCProfile / Profiles.VariantNPCProfile / TownNPCProfiles.LegacyWithSimpleShimmer；
  托管类实现不了 il2cpp 接口，构造游戏类不需要），注册期写进 `Instance._townNPCProfiles`，
  之后游戏自己按表查询。`GlobalNPC.ModifyTownNPCProfile` 在 mod 档案之后跑，非 null 直接换掉
  （null 保持原版/mod 自己的；参数用 ContentSamples 样例 NPC）。只有 townNPC 样例才注册
  （基线 IsTownSample 同款）。档案对象须可复用（注册期解析一次，与原版表语义一致）。
- 样例：TestTownNpc 用 Guide 贴图路径注册档案；NPC-T2 断言档案进了游戏表。
