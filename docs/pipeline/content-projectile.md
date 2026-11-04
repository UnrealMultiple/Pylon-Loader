# 内容管线：弹幕（Projectile）

## 1. 职责

接"一发弹幕从生成到消失"的全部游戏时机：

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `Projectile.SetDefaults` | 自定义弹幕跳过原版分支；`SetDefaults` / `PostSetDefaults` / `GlobalProjectile.SetDefaults` |
| `Projectile.AI` | `OnSpawn`（首帧一次）、`PreAI` / `AI` / `PostAI`、`AIType`、`GrappleRange` / `GrappleRetreatSpeed` |
| `Projectile.HandleMovement` + `GetCollisionParams` | `TileCollideStyle`、`OnTileCollide` |
| `Projectile.Kill` | `PreKill`、`OnKill` |
| `Projectile.Damage` / `Colliding` / `Damage_GetHitbox` | `CanDamage`、`MinionContactDamage`、`Colliding`、`ModifyDamageHitbox` |
| `Projectile.CanCutTiles` / `CutTiles` | `CanCutTiles`、`CutTiles` |
| `Main.DrawProjDirect` / `Projectile.GetAlpha` | `PreDrawExtras`、`PreDraw`、`PostDraw`、`GetAlpha` |
| `Player.FireGrapple` / `AI_007_GrapplingHooks_CanTileBeLatchedOnTo` / `Player.GetGrapplingForces` | `CanUseGrapple`、`UseGrapple`、`GrappleCanLatchOnTo`、`GrappleTargetPoint`、`GrapplePullSpeed` |
| `Projectile.NewProjectile`（浮点版） | `GlobalProjectile.OnSpawn`（`ModProjectile.OnSpawn` 不在这里，见 2.4） |
| `Projectile.ApplyStatsFromSource` | 无钩子；补齐自定义弹幕的 `originalDamage` |

**不接**（本版签名索引里没有对应 detour，或没有可靠的切入面）：

* `ShouldUpdatePosition`：`On.Terraria.Projectile` 下没有这个入口，接不了；
* `SendExtraAI` / `ReceiveExtraAI`：网络管线（[content-net](content-net.md)）负责弹幕同步包，本管线不重复接；
* `NumGrappleHooks`：游戏把钩爪上限写死在 `Player` 的多处判断里，改它要同时在射程、拴住、回收三处对齐；覆写了它的 Mod 会在注册期收到一条一次性警告；
* `CanHitNPC` / `CanHitPlayer` / `CanHitPvp`：命中判定点在 `Damage_PVE` / `Damage_PVP` 内部，且与 `HitModifiers` 半成品的改造纠缠，留给命中管线一起做。

## 2. 我们的设计

### 2.1 文件拆分

```
Pipelines/Projectile/
  ProjectilePipeline.cs   模块：接线、分派、跨钩子上下文
  ProjectileDefaults.cs   Projectile.SetDefaults 接管 + 出厂值
  ProjectileBinding.cs    Projectile 实体 ↔ ModProjectile 实例
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

两处都用**定长结构体栈**（深度 2）而不是堆对象：这是每帧、每实体都会走的路，`new` 一个帧对象等于给每一发弹幕每帧分配一次。深度超限时退化为"无上下文"（丢掉的是作者的光照/碰撞盒改写，不是崩溃）。`ModProjectile.OnSpawn` 的"首帧一次"用 `ModProjectile.Spawned` 挂在实例上，不用上下文。

### 2.5 与安装管线的边界

本管线不写表。模板初始化挂成一个安装步骤（`InstallSteps.ProjectileTemplates` → `ProjectilePipeline.SetupTemplates`），因为它必须在内容表扩长之后、`PostSetupContent` 之前跑完；写贴图槽、写 `ContentSamples` 归安装管线。

## 3. 取舍理由

* **为什么不像参考实现那样按"借用元组"访问实例？** `EntityBinding` 的字典查找是一次指针哈希，成本与借用协议同级，但它对"槽位复用"是天然正确的：参考实现的借用协议依赖"进入和离开成对"，一旦某个钩子里作者代码抛错，元组就留在旧实体上。我们的做法把正确性交给 `Rebind`（`SetDefaults` 时强制换新实例），抛错只影响一发。
* **为什么帧栈只留 2 层？** 实践中嵌套只出现在"作者的 `PreDraw` 里自己画一发别的弹幕"这种病态情况；留 2 层足够覆盖，而留 8 层会让结构体数组常驻内存翻 4 倍。溢出退化而不是扩容，是因为扩容恰好发生在最需要稳定的路径上。
* **为什么 `CanDamage` 要临时把 `Main.projPet[type]` 改成 false？** 游戏的 `Damage` 里有"宠物/仆从贴身是否造成伤害"的分支，它读的是**类型表**而不是这一发弹幕；要让 `MinionContactDamage` 生效，只能在调用期间把类型表改掉再还原。改类型表会影响同类型的其它弹幕，所以必须"改—调—还原"夹在同一个 try/finally 里，这是本管线唯一操作全局表的地方。
* **为什么 `PreDrawExtras` 的返回值被忽略？** 手机版把附加物（鱼线、钩爪链、链锤铁链）画在 `DrawProjDirect` 内部，没有单独的绘制入口，返回 false 也不可能只去掉附加物。保留调用是为了让作者的副作用（换贴图、改 alpha）照常发生，返回值则明确不生效——这比"假装支持然后再静默失效"诚实。
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
