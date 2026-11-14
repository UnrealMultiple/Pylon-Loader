# 内容管线：弹幕（Projectile）

## 1. 职责

接"一发弹幕从生成到消失"的全部游戏时机：

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `Projectile.SetDefaults` | 自定义弹幕跳过原版分支；`SetDefaults` / `PostSetDefaults` / `GlobalProjectile.SetDefaults` |
| `Projectile.AI` | `PreAI` / `AI` / `PostAI`、`AIType`、`GrappleRange` / `GrappleRetreatSpeed`、`NumGrappleHooks` |
| `Projectile.HandleMovement` + `GetCollisionParams` | `TileCollideStyle`、`OnTileCollide` |
| `Projectile.UpdatePosition` | `ShouldUpdatePosition` |
| `Projectile.Kill` | `PreKill`、`OnKill` |
| `Projectile.Damage` / `Colliding` / `Damage_GetHitbox` | `CanDamage`、`MinionContactDamage`、`Colliding`、`ModifyDamageHitbox` |
| `Projectile.CanCutTiles` / `CutTiles` | `CanCutTiles`、`CutTiles` |
| `Main.DrawProjDirect` / `Projectile.GetAlpha` | `PreDrawExtras`、`PreDraw`、`PostDraw`、`GetAlpha` |
| `Projectile.Damage_PVE` + `Player.CanNPCBeHitByPlayerOrPlayerProjectile`（`ProjectileHitHooks`） | `CanHitNPC`、`ModifyHitNPC`、`OnHitNPC`（含 ModPlayer 的 `CanHitNPCWithProj` / `ModifyHitNPCWithProj` / `OnHitNPCWithProj`） |
| `Player` 受击入口（`PlayerDamageSources`） | `CanHitPlayer`、`CanHitPvp`、`ModifyHitPlayer`、`OnHitPlayer` |
| `Player.FireGrapple` / `AI_007_GrapplingHooks_CanTileBeLatchedOnTo` / `Player.GetGrapplingForces` | `CanUseGrapple`、`UseGrapple`、`GrappleCanLatchOnTo`、`GrappleTargetPoint`、`GrapplePullSpeed` |
| `Projectile.NewProjectile`（浮点版 + 向量版，两个重载都挂） | `ModProjectile.OnSpawn`（生成时一次）、`GlobalProjectile.OnSpawn` |
| `Projectile.ApplyStatsFromSource` | 无钩子；补齐自定义弹幕的 `originalDamage` |
| `Main.DrawProj_FishingLine`（`FishingLineHooks`） | `ModifyFishingLine` |

**不在本管线**（已接线，归别的管线；弹幕 API 全集均已覆盖）：

* `SendExtraAI` / `ReceiveExtraAI`：网络管线（[content-net](content-net.md)）负责弹幕同步包；
* 命中判定（`CanHitNPC` / `CanHitPlayer` / `CanHitPvp` 及其修饰与通知）：由 `ProjectileHitHooks`（NPC 侧执行器 + 来源记录）与 `PlayerDamageSources`（玩家受击）分派，本管线只提供 `CanDamage` / `Colliding` / `ModifyDamageHitbox` 这些"打不打、打多大范围"的表态。

## 2. 我们的设计

### 2.1 文件拆分

```
Pipelines/Projectile/
  ProjectilePipeline.cs   模块：接线、分派、跨钩子上下文
  ProjectileDefaults.cs   Projectile.SetDefaults 接管 + 出厂值
  ProjectileBinding.cs    Projectile 实体 ↔ ModProjectile 实例
  ProjectileDrawSplit.cs  绘制拆分：SpriteBatch.Draw / DrawData.Draw 按贴图过滤
```

三个文件对应三个不同的生命周期：管线是进程级的（一个实例，装一次钩子），默认值是"每次 `SetDefaults` 跑一次"的接管点，绑定是"每个实体一份"的映射表。把默认值拆出去的理由和物品管线相同：它是唯一一处**跳过原版代码**的地方，读的人必须能一眼看出"这里没有调 orig"。

### 2.2 三层数据

| 层 | 生命周期 | 存哪 |
|---|---|---|
| 模板 | 进程 | `ContentRegistry` 槽表；`ModProjectile.Template` 保存一个真实 `Projectile` 供 `SetStaticDefaults` 读 |
| 实体实例 | 跟随 `Projectile` 实体 | `ProjectileBinding`（指针 → 实例），`SetDefaults` 时强制重建 |
| 覆写位集 | 类型 | `ModType.Plan` / `HookScan.Of(<T>, typeof(GlobalProjectile))` |

模板那一行是弹幕与物品最大的不同：弹幕在 `SetStaticDefaults` 里要写 `Main.projFrames[Type]`、`Main.projHostile[Type]`，作者需要**一个能读能写的 Projectile 对象**。所以安装期先 `new Projectile(); template.SetDefaults(type)` 造一份模板，同时把管线的 mod 分支关掉（`Suppress`），否则模板会跑成作者自己的 `SetDefaults`。

### 2.3 分派顺序与短路

| 钩子 | 顺序 | 短路语义 |
|---|---|---|
| `PreAI` | 本弹幕 → Global | 任一 false 就跳过游戏 AI **和** 全部 `AI`，`PostAI` 照跑 |
| `AI` | 本弹幕 → Global | 无短路 |
| `PostAI` | Global → 本弹幕 | 无短路 |
| `OnTileCollide` | Global → 本弹幕 | 任一 false 表示"这次撞击不死" |
| `TileCollideStyle` | 本弹幕 → Global | 任一 false 表示本帧不碰方块 |
| `CanDamage` | Global → 本弹幕 | Global 里有一个 true 就盖过本弹幕的否决 |
| `Colliding` / `CanCutTiles` / `GetAlpha` | Global → 本弹幕 | 第一个非 null 的结果胜出 |
| `PreDraw` | Global → 本弹幕 | 任一 false 不画精灵 |
| `PreKill` | Global → 本弹幕 | 任一 false 去掉死亡特效 |

`PostAI`、`OnTileCollide`、`GetAlpha` 这批是 Global 在前：它们要能"包住"本弹幕的行为（先看全局有没有意见，再看作者自己）。`PreAI` / `AI` 是本弹幕在前，因为它是自己那一发的 AI，全局只是旁听。

### 2.4 跨钩子上下文

只有两处需要"钩子 A 里算出的值，钩子 B 里还要用"，因为中间隔着原版代码：

1. **移动**：`HandleMovement` 里改 `tileCollide` / `shouldFallThrough`，`GetCollisionParams` 会在 `orig` 内部被调多次，必须把 `TileCollideStyle` 改写过的碰撞盒喂回去；`Kill` 也可能在 `orig` 内部被调用（撞块致死），必须挂起等 `OnTileCollide` 表态。
2. **绘制**：`PreDraw` 算出的光照色要给 `GetAlpha` 用（`GetAlpha` 在 `orig(DrawProjDirect)` 内部被调）。

两处都用**定长结构体栈**（深度 2）而不是堆对象：这是每帧、每实体都会走的路，`new` 一个帧对象等于给每一发弹幕每帧分配一次。深度超限时退化为"无上下文"（丢掉的是作者的光照/碰撞盒改写，不是崩溃）。`ModProjectile.OnSpawn` 的"生成时一次"用 `ModProjectile.Spawned` 挂在实例上（`NewProjectile` 分派里置位；没走 `NewProjectile` 的实体由首帧 AI 兜底），不用上下文。

### 2.5 与安装管线的边界

本管线不写表。模板初始化挂成一个安装步骤（`InstallSteps.ProjectileTemplates` → `ProjectilePipeline.SetupTemplates`），因为它必须在内容表扩长之后、`PostSetupContent` 之前跑完；写贴图槽、写 `ContentSamples` 归安装管线。

## 3. 取舍理由

* **为什么不像参考实现那样按"借用元组"访问实例？** `EntityBinding` 的字典查找是一次指针哈希，成本与借用协议同级，但它对"槽位复用"是天然正确的：参考实现的借用协议依赖"进入和离开成对"，一旦某个钩子里作者代码抛错，元组就留在旧实体上。我们的做法把正确性交给 `Rebind`（`SetDefaults` 时强制换新实例），抛错只影响一发。
* **为什么帧栈只留 2 层？** 实践中嵌套只出现在"作者的 `PreDraw` 里自己画一发别的弹幕"这种病态情况；留 2 层足够覆盖，而留 8 层会让结构体数组常驻内存翻 4 倍。溢出退化而不是扩容，是因为扩容恰好发生在最需要稳定的路径上。
* **为什么 `CanDamage` 要临时把 `Main.projPet[type]` 改成 false？** 游戏的 `Damage` 里有"宠物/仆从贴身是否造成伤害"的分支，它读的是**类型表**而不是这一发弹幕；要让 `MinionContactDamage` 生效，只能在调用期间把类型表改掉再还原。改类型表会影响同类型的其它弹幕，所以必须"改—调—还原"夹在同一个 try/finally 里，这是本管线唯一操作全局表的地方。
* **为什么 `PreDrawExtras` 的返回值要靠绘制拆分实现？** 手机版把附加物（鱼线、钩爪链、链锤铁链、悠悠球绳）画在 `DrawProjDirect` 内部，没有单独的绘制入口。要让"只去掉附加物"或"只去掉精灵"生效，只能在绘制调用这一层按贴图过滤：`ProjectileDrawSplit` 挂住 `SpriteBatch.Draw` 的四个重载与 `DrawData.Draw`，在拆分窗口内"遇到本弹幕贴图之前"按 extras 决定、"遇到（含）之后"按 sprite 决定。拆分只在 `PreDrawExtras` 与 `PreDraw` 结论**不一致**且该弹幕确实带附加物（`bobber` 或 aiStyle 7/13/15/99）时开启，普通弹幕零干预。
* **为什么 `NumGrappleHooks` 要"先藏后剪"？** 钩爪上限写死在原版 AI_007 的计数里（按 aiStyle==7 且已挂住统计），没有可改写的参数。做法：AI 里先问作者要上限；与原版默认不同时，把已挂住的钩爪临时 `aiStyle=0` 藏起来骗过原版计数，`orig` 跑完还原，再按作者上限把最老的钩爪 `Kill()` 掉。只在本地玩家（`Main.myPlayer`）且发射阶段（`ai[0]==0`）做——其它端的修剪由同步包自然收敛。
* **为什么把 `PostDraw` 放在"PreDraw 否决"之后也照跑？** `ModProjectile.PostDraw` 的文档承诺"PreDraw 返回 false 也调"，旧实现只在建了绘制帧时才跑，两者矛盾。这里按文档实现：`PreDraw` 拦掉精灵，`PostDraw` 仍有机会画自己的东西。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| `ModProjectile.SetDefaults` 抛错 | `ContentErrors.Report` 记一条；弹幕保持出厂值，照样 `active`（否则 `NewProjectile` 拿到死弹幕） |
| `PreAI` / `AI` / `PostAI` 抛错 | 记一条；这一次 AI 的被否决部分跳过，游戏 AI 与后续帧继续 |
| `TileCollideStyle` / `OnTileCollide` 抛错 | 记一条；按"没改过"处理（用原版碰撞盒、撞击致死） |
| `PreKill` / `OnKill` 抛错 | 记一条；弹幕按原版死亡流程走 |
| `PreDraw` / `GetAlpha` 抛错 | 记一条；画原版的精灵与原版颜色 |
| `CanUseGrapple` / `Grapple*Speed` 抛错 | 记一条；钩爪按原版数值工作 |
| 绑定失败（槽位换内容） | 跳过作者钩子，原版路径照走 |

原则：**一发弹幕的失败只降级这一发**，不改变其它弹幕的字段、不打断整帧。

## 5. 扩展方式

新增一个弹幕钩子：

1. `HookId` 末尾追加编号（只能追加，位集按编号索引）；
2. `ModProjectile` / `GlobalProjectile` 加同名的 `virtual` 方法（`HookScan` 按方法名匹配，名字必须与枚举项一致）；
3. 在 `ProjectilePipeline` 对应的游戏钩子里分派：本弹幕用 `mod.Plan.Has(HookId.X)` 门控，Global 用 `GlobalProjectileRegistry.For(entity, HookId.X)`；
4. 若需要新的跨钩子上下文，在管线上加一个定长结构体栈，不要退回 static 字段。

## 6. 与其它管线的边界

* **物品管线**：物品的 `Shoot` 链最终落到 `NewProjectile` 上，两条管线都订阅同一个入口；物品管线先改写参数、决定发不发，弹幕管线只负责"发出来之后通知 GlobalProjectile.OnSpawn"。二者不互相调用。
* **NPC 管线**：弹幕打 NPC 的伤害流程在 `Projectile.Damage_PVE` 里，NPC 侧的承受逻辑归 NPC 管线；本管线只提供 `CanDamage` / `Colliding` 这种"打不打"的表态。
* **网络管线**：弹幕同步包与 `SendExtraAI` / `ReceiveExtraAI` 归网络管线；本管线不碰 `NetWriter`。
* **安装管线**：模板初始化与贴图槽写入归安装管线，本管线只被它调用一次。
