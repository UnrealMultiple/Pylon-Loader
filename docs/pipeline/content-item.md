# 内容管线：物品（Item）

## 1. 职责

物品是内容种类里每帧链最长的一个：一条"玩家拿着物品"的调用链横跨使用、攻速、魔力、发射、弹药、拾取、装备、绘制、存档、网络。本管线把这些游戏入口接给作者：

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `Player.ItemCheck_CheckCanUse_Inner` | `CanUseItem`（false = 这次用不了；ModItem 先、GlobalItem 后） |
| `Player.ItemCheck_StartActualUse` | `UseItem`（取"或"，任一 true 就算用了）、`ApplyItemTime` |
| 使用风格与挥动 | `UseStyle`、`HoldStyle`、`HoldItem`、`UseItemFrame`、`HoldItemFrame` |
| `ItemCheck_Shoot` / `NewProjectile` | `CanShoot`、`ModifyShootStats`、`Shoot`（跨两次调用的上下文） |
| 弹药选择与消耗 | `CanChooseAmmo`、`NeedsAmmo`、`CanBeChosenAsAmmo`/`CanBeConsumedAsAmmo`、`CanConsumeAmmo`/`OnConsumeAmmo`/`OnConsumedAsAmmo`、`PickAmmo` |
| 魔力 | `CheckMana`、`PredictWithoutUse`、`OnMissingMana`、`OnConsumeMana` |
| 治疗 | `GetHealLife`、`GetHealMana`、快速选药 |
| 拾取 | `CanPickup`、`GrabRange`、`GrabStyle`、`ItemSpace`、`OnPickup` |
| 装备准入与外观 | `CanEquipAccessory` 四入口、`CanEquipBothAccessories`、`IsVanitySet`、`SetMatch`、翅膀速度/自绘、`DrawArmorColor`、`ArmorArmGlowMask` |
| 命中 | `UseItemHitbox`、`MeleeEffects`（近战命中实体的重入保护在 NpcHitHooks） |
| 物品着色 | `GetAlpha`（返 null = 用原色） |
| 地上物品绘制 | `PreDrawInWorld`（false = 这件不画）、`PostDrawInWorld`（否决了也跑）；rotation/scale 是可写引用 |
| 背包图标绘制 | `PreDrawInInventory`（false = 这个图标不画）、`PostDrawInInventory`（否决了也跑） |
| `Item.SetDefaults` | `ModItem.SetDefaults`（每次实例化）、`GlobalItem.SetDefaults` |
| 玩家侧车存档 | `SaveData`、`LoadData`（`<player>.plr.pl`，行内嵌 TagCompound） |
| 网络同步 | 物品实例表同步、PvP 伤害包的来源通道 |

`Item.TML.cs` 提供 TML 风格的作者 API（`item.ModItem`、`EntityGlobals`、`GetGlobalItem`、`CloneDefaults`、`DefaultToPlaceableWall`、`ResearchUnlockCount`、`Item.Create` 等）。

**不接**：

* `Item.DamageType` / `CountsAsClass`：伤害归类写进 DamageClass 管线的行，物品行不重复维护一份。
* 世界箱子持久化：物品存档侧车目前只覆盖玩家文件（背包/装备/钱币/箱子背包/套装），`Main.chest` / `WorldFile` 没有侧车入口——mod 物品放进世界箱子后只存类型号，mod 集变化后靠注册顺序兜着。
* `Recipe.DecraftConditions` 的分解过滤归 Recipe 管线。

## 2. 我们的设计

### 2.1 一个内容种类 = 一个管线类

```
Pipelines/Item/
  ItemPipeline.cs        模块：接线 + 分派 + 射击上下文
  ItemDefaults.cs        SetDefaults 接管
  ItemBinding.cs         Item 实体 ↔ ModItem 实例
  ItemTimingHooks.cs     使用时长三层乘积
  ItemManaHooks.cs       魔力替换
  ItemAmmoHooks.cs       弹药全套
  ItemEquipHooks.cs      装备准入与外观
  ItemDrawStageHooks.cs  绘制四钩子（世界侧 On 包装 + 中体桥、库存侧 On 包装）
  ...                    治疗拾取命中帧存档排序各一文件
```

按内容种类聚合、类内按链分段：排查"为什么我的物品不发射"只需要读一个文件。每帧钩子多，再按"链"拆成小文件控制单文件长度，`ItemDefaults`、`ItemBinding` 这类纯机制独立成类。

### 2.2 三层数据

| 层 | 生命周期 | 存哪 |
|---|---|---|
| 模板 | 进程 | `ContentRegistry` 的槽表，按 `Type` 索引 |
| 实体实例 | 跟随 `Item` 实体 | `EntityBinding<Item, ModItem>`（指针 → 实例） |
| 覆写位集 | 类型 | `ModType.Plan`（`HookMask`） |

**为什么要实体实例？** `Item` 在背包里会被复制、换 type、重排；作者在 `ModItem` 上写的字段属于"这一件物品"，不属于"这个类型"。模板只放共享值（`Type`、贴图），实例跟着实体走，实体换到别的 type 才重建实例。GlobalItem 走 `GlobalItemRegistry`：静态模板 + `InstancePerEntity` 时的按实体实例，与 tModLoader 语义一致。

### 2.3 分派规则

* 顺序固定：**本物品的 `ModItem` 先，`GlobalItem` 后**（与 tModLoader 一致，作者按这个顺序推理）。需要玩家上下文的链是三层：`ModPlayer → ModItem → GlobalItem`（如 `CanUseItem`、`UseTime/UseAnimation/UseSpeed` 的乘积、`ModifyShootStats`）；`Shoot` 反过来是 ModPlayer 层全员先过、GlobalItem 全员"与"、最后 ModItem 给结论。`CanAutoReuseItem` 的 ModPlayer 层能把后面的层整体锁 false。绘制四钩按各自语义：世界侧（Pre/PostDrawInWorld）ModItem 先、库存侧 Pre（PreDrawInInventory）GlobalItem 先、库存 Post 又回到 ModItem 先——与四钩的 tML 语义逐钩一致，不为统一而统一。
* 门控用位集：`Plan.Has(HookId.X)` 是一次位运算；没覆写就直接跳过，不构造参数。`On_*` 订阅按"是否有人覆写"整门安装（`HookGuard.Wants` / `GlobalItemRegistry.AnyWith`），没人覆写的门根本不装。
* 短路语义：`CanUseItem` / `CanShoot` 返回 false 立即终止后续订阅者；`UseItem` / `Shoot` 取"或"；绘制四钩的 Pre 是"全员与"（每个订阅者都跑，任一 false 就跳过这次绘制）。
* 弹药与治疗这类"临时替换游戏字段"的钩子（`manaCost`、弹药临时替换）在 orig 前替换、finally 恢复，作者看到的值按链叠加。

### 2.4 射击链的上下文

`ItemCheck_Shoot` 与 `NewProjectile` 是两次独立调用，中间隔着原版逻辑。管线在射击门里记下"当前物品 + 玩家"，`NewProjectile` 消费一次后**立即清空**：

* 作者在 `Shoot` 里自己发弹时，不能再次命中同一份上下文（否则递归到栈溢出）；
* 处理期间再进的 `NewProjectile`（作者或原版递归发弹）透传给原版，不消费上下文；
* 上下文是管线实例字段，不是 static——同一进程只有一个物品管线，但状态归属明确。

### 2.5 绘制四钩子

绘制参数（旋转、缩放、光照色、着色）是原版绘制方法体中段的寄存器/栈现场，纯托管层在入口拿不到完整值，所以世界侧的接法是"入口 On 包装 + 一个中体桥"：

* **世界侧**（`Main.DrawItem`）：On 钩先复刻游戏自身的三道 early-out（未激活 / `IsAir` / 实例保留给别人），然后用公开方法重算四个参数——`light = Lighting.GetColor(Center → tile)`、`alpha = GetAlpha(light)` 加 shimmer 修正再过 `ItemSlot.GetItemLight`、`scale` 取同一次 `GetItemLight` 的 out、`rotation = velocity.X × 系数`（shimmered/onConveyor 时为 0）——派发 `PreDrawInWorld`。返回 false 就不调 orig（先补调一次 `DrawItem_GetBasics`，保住它内含的动画推进副作用），随后照常派发 `PostDrawInWorld`；否则调 orig、orig 返回后派发 Post。
* **ref 写回**：作者对 `ref rotation` / `ref scale` 的修改只有在方法体中段才影响原版绘制，所以装一个 `Main.DrawItem` 入口 +0x418 的中体桥（指纹校验 + `MidBodyIntercept`），桥只把**作者改过的值**写回寄存器现场（rotation→v11、scale→栈上槽，其余一概不动），值都过 IsFinite 校验。旋转系数优先在运行时从解析出的方法体指令流里解码装载对（ADRP 目标页 + LDR 偏移）得到数据地址后读取——不硬编码绝对地址；解码不符时在桥首次命中处用游戏现场的 v11/velocity.X 自校验补值，仍取不到则初值退化为 0——作者的写入依然生效，Warn 可见。
* **库存侧**（`ItemSlot.DrawItemIcon`）：参数全在入口签名里，On 钩用 `DrawItem_GetColorAndScale` 重算帧/光照/最终缩放，`origin = 帧尺寸一半`、`drawColor = GetAlpha(light)`、`itemColor = GetColor(环境色)`；Pre 返回 false 时不调 orig 并返回 finalScale（与原版返回值语义一致），Post 照常派发。
* **门控与降级**：两个 IGate（`DrawWorld` / `DrawInventory`）按"是否有人覆写"整门安装，四个钩子都没人覆写时零补丁零订阅。世界门装时才尝试中体桥；指纹不符 → Warn 一条 → 世界四钩整组不接（可见降级，游戏照常绘制），库存侧没有指纹依赖、不受影响。所有补丁点都相对运行时解析出的方法入口表达（入口经 il2cpp 元数据解析，偏移以 entry 为基准，站点 16 字节指纹做"偏移仍指向预期指令"的验证），偏移与指纹字集中在 `ItemDrawStageHooks` 顶部一处，换游戏构建时只重验那里。
* **已知差距**（入口重算相对"参数取自中段现场"的固有差，全部有界）：
  1. Pre 派发在原版方法入口、参数是重算值——数值上等价（同一批被调方法），但重算调用若被其他 mod 的 On 钩住会多一层间接；
  2. skip 少执行中段位点之前的纯计算段——其中唯一有副作用的 `DrawItem_GetBasics`（动画推进）已显式补调，只对被否决物品的隐式帧计数有意义；
  3. rotation 初值依赖系数解析：静态读失败时靠自校验兜底，再失败则初值为 0（作者读到的初值差、写入仍生效），Warn 可见；
  4. 库存侧"按类型图标帧覆盖"等原版特殊分支未复刻——只影响极少数带图标帧覆盖的原版类型的**参数显示**（原版绘制不受影响，orig 自算自画）；drawColor 也比原版少一次后续的静态色相乘；
  5. 中体桥对每次物品绘制都先进托管判空——常数量级开销（每帧约数百次指针比较早退），与既有中体站点一致。

### 2.6 与安装管线的边界

物品管线**不写表**：静态表、贴图、名字、样例由安装管线（[02-install](02-install.md)）在表定型后统一写。安装计划里与物品相关的步骤：

1. **扩表**（Layout）：按 `ContentKind.Item` 的下一个类型号把 `ItemID` 相关表扩到位；
2. **装备贴图安装**（Static）：收集各 `ModItem` 声明的装备贴图（这一步顺带分派 `ModItem.Load`）；
3. **贴图上传 / 写贴图与文本**（Surface）：物品贴图、名字、tooltip、火焰贴图写入表槽；
4. **物品静态默认值**（Samples，"物品样例"之前）：按 mod 装载顺序逐 mod 调 `ModItem.SetStaticDefaults` 与 `GlobalItem.SetStaticDefaults`，作者在这里写 `ItemID.Sets`、`AmmoID.Sets` 等静态表；每个内容单独兜异常，出错走 `ContentErrors` 记一条，不影响其它内容；
5. **物品样例**（Samples）：为每个类型建 `ContentSamples.ItemsByType` 样例并登记持久 ID——此时作者的静态表已就位，样例的 `SetDefaults` 能读到；
6. **物品排序白名单 / 聊天标签上限**（Samples）与配方、掉落、任务鱼（Mods/Recipes 阶段）消费静态表。

`ModType` 的生命线由此串起来：注册占号 → `SetStaticDefaults`（表定型后）→ `SetDefaults`（每次实例化）。

## 3. 取舍理由

* **为什么聚合到一个管线类？** 一条"用物品"的调用链（`CanUseItem → UseItem → UseStyle → Shoot`）在游戏里本来就是连续的；按关注点拆开后，排查"为什么我的物品不发射"要在四个文件之间跳。聚合到一个类、类内按链分段，读一处就能看完整条链。代价是单文件更长——用分区注释和小类控制。
* **为什么门控按"是否有人覆写"整门装？** 每帧路径要能承受"上千实体 × 每个钩子"的调用量。没人覆写的钩子连 `On_*` 订阅都不装，覆写了的链路里每次分派只是一次位运算加虚调用。
* **为什么世界侧是"On 包装 + 中体桥"，而不是把 pre/post 都做成中段钩？** pre 的 ref 修改需要写进方法体中段的寄存器现场，这是中体桥唯一不可替代的职责；skip、post 派发与参数供给用 On 包装已语义等价。全中段方案要多维护三个站点指纹还要扩桩原语，收益只是参数逐位保真——按"指纹维护面最小、不碰最敏感的桩原语"取舍，只留 1 个站点（含 1 条随链接布局变的 ADRP 字）。
* **为什么 ref 写回只写作者改过的值？** 入口重算的初值是对游戏现场值的等价重算而非同一份数据；把没改过的值原样写回，一旦重算与现场有偏差反而会污染绘制。只写"改过的"，没改的保持游戏自己的值——作者可见行为不变。
* **为什么指纹不符时世界四钩整组不接？** "skip 生效但 ref 无效"的半工作状态对作者更难排查：他用 ref 改缩放没反应，却看不出问题在哪。整组不接 + Warn 一条，降级可见、可定位，游戏不炸。
* **为什么 `SetStaticDefaults` 放在"物品样例"之前？** 原版 `SetDefaults` 与样例注册都会读静态表（弹药识别、物品集合判定）；作者在 `SetStaticDefaults` 里写的表必须在任何实例化之前就位，也必须在配方、掉落、排序这些消费方之前。它跟 NPC 的"静态默认值在样例之前"是同一条规则。
* **为什么逐内容兜异常？** 一个物品的 `SetStaticDefaults` 抛错不能拖垮整个安装批次，也不能让同 mod 的其它物品丢静态表；隔离粒度是单个内容，出错记一条可定位到 `Mod/类型`。
* **为什么射击上下文消费一次即清空？** 见 2.4：不清空会递归，不清归属会串弹。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| `ModItem.SetStaticDefaults` / `GlobalItem.SetStaticDefaults` 抛错 | 记一条（`ContentErrors`），只跳过这一个内容，其余照常 |
| `ModItem.SetDefaults` 抛错 | 记一条，物品保持 `ResetStats` 后的原版默认值 |
| `GlobalItem.SetDefaults` 抛错 | 记一条，只跳过这一个订阅者 |
| `CanUseItem` / `CanShoot` 抛错 | 视为放行 |
| `UseItem` / `Shoot` 抛错 | 视为未处理，走原版 |
| `PreDrawInWorld` / `PreDrawInInventory` 抛错 | 记一条（首见去重），该作者跳过、结果按已跑完的订阅者算，其余照常 |
| `PostDrawInWorld` / `PostDrawInInventory` 抛错 | 记一条（首见去重），不影响绘制结果 |
| 绘制站点指纹不符 / 方法没解析到 | Warn 一条，世界四钩不接；库存四钩与游戏绘制不受影响 |
| 中体桥分派自身抛错 | 记一条（首见去重），该帧放行原指令，绘制照常 |
| 实例绑定失败 | 跳过 mod 钩子，原版路径照走 |
| `ModItem.Load` 抛错（装备贴图收集时） | 记一条，该物品的装备贴图不收集 |

原则：**作者代码出错不能让游戏不可玩**，只能让这个物品表现降级。

## 5. 扩展方式

新增物品钩子：

1. `HookId` 末尾追加；
2. `ModItem` / `GlobalItem` 加 `virtual`；
3. 在 `ItemPipeline` 对应游戏钩子里分派（本物品 → 全局；需要玩家上下文的链保持 ModPlayer 层在最前；绘制类钩子的顺序按 tML 各钩自己的语义）；
4. 需要跨物品统一行为时，写 `GlobalItem`，由 `GlobalItemRegistry` 统一取订阅者；
5. 接新游戏入口时在 `ItemPipeline.Gates` 加一个 `IGate`：`Wanted()` 写覆写条件，`Install()` 写订阅，`GateHub` 自动发现；
6. 参数在原版方法体中段的钩子（寄存器/栈现场），入口重算不了的部分走 `MidBodyIntercept` 中体桥：站点指纹进站点表、handler 里只写"作者改过的"值，失败降级 Warn。

## 6. 与其它管线的边界

* **DamageClass 管线**：武器伤害/暴击/击退/体积修正与 `Item.DamageType`/`CountsAsClass` 的行归它；近战命中实体的重入保护（`ProcessHitAgainstNPC` 等）也在那一侧的 `NpcHitHooks`，物品管线只接 `UseItemHitbox`/`MeleeEffects`。
* **Player 管线**：`ModPlayer` 层的物品钩（`CanUseItem`、时长乘积、发射链）由 Player 管线供给实例；物品门的覆写条件把 `ModPlayer` 的覆写一并计入。
* **安装管线**：静态默认值、贴图、名字、样例、排序白名单都是安装期的事（见 2.6）；物品管线只负责"游戏调到我时，把调用转给作者"。
* **Loot 管线**：`ModifyItemLoot` 与掉落上限在 Loot 管线；本管线不碰掉落规则。
* **Net 管线**：物品实例表同步与 PvP 来源通道（复用原版受伤包旁路 + FullKey 匹配）由 Net 侧承载，物品管线只提供分派点。
* **Patch 层**：`Lang.GetItemName`/`GetItemNameValue` 接管与 `[i:]`/`[ct:]` 标签上限是 `ItemNamePatch`/`ChatTagPatches` 的方法补丁，不是钩子分派。
* **Patching 层**：绘制四钩子的中体桥只**复用** `MidBodyIntercept`/`InterceptStub`/`CodeSite` 的既有能力，不改 Patching 本身；站点偏移、指纹字与系数装载对偏移收在 `ItemDrawStageHooks` 一处。
* **UnloadedItems**："？"池在一切 mod 发号之前占 64 个号，mod 号段与"？"用量彻底解耦；"？"类型不是注册内容，名字/贴图/样例由 `ItemDefaults`、存档、排序各入口显式分支处理。
