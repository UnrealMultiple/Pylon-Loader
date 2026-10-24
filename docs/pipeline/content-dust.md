# 内容管线：尘土（Dust）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（`Static`） | 扩表先行 → 发号（类型 = 原版表长 + 注册序）+ `SetStaticDefaults` |
| 生成（`Dust.NewDust`，管线 `Spawn` 是薄壳） | `OnSpawn(dust)`（原版 NewDust 池内落位后触发；frame 规范化 x=0、y%=30） |
| 每帧更新（`Dust.UpdateDust` 前后） | orig 前：`Update(dust)`（true = 交给原版物理；false = 本帧藏起跳过原版循环；`dust.active = false` = 杀）。orig 后：`MidUpdate(dust)`（true = 抵消原版横向刹车 velocity.X /= 0.99） |
| 每帧绘制（`Main.DrawDust` 前后） | orig 前：mod 尘土整体藏匿（active 翻转），orig 只画原版尘土；orig 后：恢复 + 自开批次 → `PreDraw(dust)`（false = 跳过这张精灵）→ `GetAlpha(color)` + 作者贴图 |

**暂不接**：原版绘制 pass 接入（帧动画、`Main.Transform`/PointClamp、逐贴图组换 `TextureAssets.Dust` pass——单张图集，B2）、原版 `GetAlpha` 光照（lightColor 现为 dust.color）、碰撞、联网同步、`DustID.Sets` 的逐类型取值、freeDust 全池巡检（D7）。

## 2. 我们的设计

### 2.1 进池：原生表就是接口

mod 尘土与原版共用 `Main.dust` 池（6000 槽），类型号从原版表长续发（本构建 332）：`332 + 注册序`。原版系统（弹幕、NPC、环境效果）与作者的 TML 式代码都能直接 `Dust.NewDust` 传 mod 号；管线挂在原版 `NewDust` 的后置：池满/槽无效直接返回（不误触 OnSpawn），按**实际 `dust.type`** 反查（不信传入号——ChildSafety 可能换型或杀掉），frame 规范化，触发 `OnSpawn`。

按类型索引的原生表随号段扩容：`Dust.StaticAlpha` / `Dust.StaticAlphaValue`（原版 `Dust.GetAlpha` / `Dust.UpdateDust` 等读取点编译时去掉了越界检查）与 `ChildSafety.SafeDust`（ChildSafety 按它换型/杀尘土）。扩表走框架 TableGrower（众数填充，SafeDust 尾部填 true = 安全），**先于任何 mod 号进原版**：Build/Refresh 都先 Grow 再 Assign，安装门在「扩表到位」前不挂钩。原版表长运行期捕获：主 `ChildSafety.SafeDust.Length`、备 `Dust.StaticAlpha.Length`，读到 0 不缓存，两者不等即停用报错。

ChildSafety 开启时原版可能把「危险尘土」换型成 16 或不生成——SafeDust 扩容后 mod 号永远「不危险」；即便扩表兜底失效（扩容失败、表被游戏重建未及重扩），管线的全部派发也以当下 `dust.type` 反查：换了型只是不派发，不会错派发。

### 2.2 池容量与标记槽位表

mod 尘土与原版共享 6000 槽：池满时原版 `NewDust` 静默不生成，`OnSpawn` 不误触，`Spawn` 返回 null（顺带有了上限限流）。管线维护一张标记槽位表：NewDust 钩打标，更新/绘制的每帧快照只扫标记槽；槽被原版尘土复用或尘土被换型时，扫描自动丢标，天然收敛。

### 2.3 更新：包住原版循环的两遍派发

* **第一遍（orig 前）**：对有 `Update` 钩的尘土逐颗派发（flags 缺省 true）。杀优先：回调里 `dust.active = false` → 由 kill tracking 正确还槽，藏匿与派发都跳过它；返回 false → 记下旗标（藏匿放到派发后统一做，见下）；返回 true → NaN/Inf 数值卫生检查（坏数据 `SetNotActive` 回收）后交给原版物理分支，常数与原版逐位一致。
* **藏匿**：派发完对快照统一扫一遍，返回 false 且仍 active、仍是 mod 类型的尘土临时置 `active = false`（原版循环跳过它），orig 后恢复——本帧照常被自开批次画、下一帧继续派发。又杀又返回 false 的尘土按死处理。
* **第二遍（orig 后）**：对快照内仍 active 且有 `MidUpdate` 钩的尘土派发（派发中途不查 active）；返回 true → `velocity.X /= 0.99`，抵消原版循环刚施加的横向刹车；异常不除。
* **本帧新生的尘土**（回调里 Spawn 的）不在本帧快照，下一帧才开始派发。
* **消亡归原版**：原版循环自己管消亡（收缩、寿命、入水……），管线没有出屏裁剪与收缩回收判定。数值卫生保留为 orig 前守卫；`Update` 抛异常按 D9 记一条并 `Dust.SetNotActive` 回收（回收且正确还槽）。

### 2.4 绘制：中间态（藏匿 + 自开批次）

原版 `TextureAssets.Dust` 是**一张图集**（不是数组），mod 贴图没有逐类型槽位：orig 前把 mod 尘土全部藏匿（原版 pass 画不到它们），orig 后恢复，再由管线自开一个批次只画 mod 尘土——整张贴图直画、无视 `dust.frame`，混合/采样/层级沿用旧版语义（恒在原版尘土之上）。`PreDraw` 里作者自己画的仍然垫底（先于批次常规绘制），返回 false = 本帧不画这张精灵。
B2 接入原版绘制 pass 后本节改写：帧动画（10×30 布局）、`Main.Transform`、PointClamp、逐贴图组换图集 pass、原版 `GetAlpha` 光照。

### 2.5 freeDust 栈与 kill tracking

手机版原版自由槽是栈（`Dust.freeDust`/`freeDustCount`），只有 `Dust.SetNotActive` 会还槽；直写 `dust.active = false` 会让槽位永久丢失（时间久了全局生不出尘土的原版顽疾）。管线三处派发（Update / MidUpdate / PreDraw）都包 kill tracking：派发开始记 `freeDustCount` 最低点，结束时在 free 栈段 `[最低点+1, 当前值]` 回扫，变 inactive 又不在栈里的尘土先 `active = true` 再 `SetNotActive`，按游戏的规矩还槽。

## 3. 取舍理由

* **为什么进池**：原版系统按类型号生成尘土，作者的 TML 式代码也直调 `Dust.NewDust`；进池后两边看到同一批尘土，`DustID` 之外的新号经扩表消掉越界风险。
* **为什么「返回 false」是跳过一帧而不是消失**：接管式更新需要一个"我还在管，只是这一帧不用你管"的信号（沿用 2026-11-21 批次的语义）。
* **为什么物理交给原版循环**：原版分支是按 `DustID` 写死的几百个特例；进池后原版循环自然跑通用分支（常数与原版一致），管线不再养一套近似物理。
* **为什么需要 kill tracking**：TML 惯用的 `dust.active = false` 在手机版漏槽；三个回调都可能杀尘土，跟踪一次管完。原版循环自己的杀走 `SetNotActive`，无需跟踪。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 池满 / 槽无效 | 静默不生成，`Spawn` 返回 null，`OnSpawn` 不误触 |
| `OnSpawn` 抛异常 | 记一次，尘土照常存在（已在池内） |
| `Update` 抛异常 | 记一次，该尘土经 `SetNotActive` 回收（正确还槽） |
| `MidUpdate` / `PreDraw` / `GetAlpha` 抛异常 | 记一次，走缺省（不抵消 / 照画 / 用 dust.color 原样画） |
| 贴图没加载好 | 这一帧不画（其他尘土照画） |
| `Spawn` 传 null / 未发号的 `ModDust` | 返回 null |
| 运行期指纹不符（表长/槽位/签名） | 池路径整体停用并报错，`Spawn` 一律返回 null |

## 5. 扩展方式

接原版光照、帧动画与贴图组 pass：见 B2 计划（绘制阶段重构：PreDraw 批次 + 逐贴图组换 `TextureAssets.Dust` + `Dust.GetAlpha` 双重载 hook）。
接联网：mod 尘土是纯表现，按原版的规矩本来也不该同步。

## 6. 与其它管线的边界

* **血腥粒子（19）**：那条是 `Gore`，有自己的数组与绘制，和尘土无关。
* **弹幕 / 物品**：要 mod 类型的尘土直接 `Dust.NewDust`（与原版号同入口）；`ModDust.Spawn` 是精确值薄壳（无 w/h 随机框，color 缺省 `default(Color)`）。
* **贴图设施**：本管线经 TableGrower 扩原版三张表；作者贴图仍在安装期上传。
* **场景效果**：无关。

### 迁移说明（进池，2026-11-22 生效）

| 变更 | 说明 |
|---|---|
| 号段 | mod 尘土类型号从 `10000+k` 改为 `332+k`（原版表长 + 注册序）；`ModContent.DustType<T>()` / `GetModDust` 语义不变，返回值随之变化。尘土不入存档，无持久化风险 |
| 生成入口 | `Dust.NewDust` 可直传 mod 号；`ModDust.Spawn` 变为 `Dust.NewDust` 薄壳，`color` 缺省从 `Color.White` 改为原版的 `default(Color)`（透明）；w/h 随机落点框仍未接（另列） |
| 消亡 | 管线的出屏（屏下 +200）裁剪与收缩回收判定退役——原版 `UpdateDust` 自己管消亡；NaN/Inf 数值卫生保留为 orig 前守卫 |
| 更新语义 | 返回 true = 原版物理（不再是管线近似物理）；false = 藏匿跳帧（同前）；`dust.active = false` = 杀，三处回调内都生效且正确还槽 |
| 容量 | 与原版共享 6000 槽，池满静默不生成（不再无上限） |
| B2 待办 | 帧动画（10×30 布局）、贴图组 pass / `Main.Transform` / 采样器、原版 `GetAlpha` 光照、标记槽位全扫与池巡检（D7） |

### 这一版没接

1. **原版绘制 pass 接入**（B2：帧动画、贴图组换图集、`Main.Transform`/PointClamp、`GetAlpha` 双重载光照）。
2. **碰撞 / 灯光 / `DustID.Sets`**。
3. **联网同步**。
4. **freeDust 全池巡检 / 重建**（D7，另列排期；B1 的 kill tracking 已覆盖「mod 派发期直写 active=false」主场景）。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 号段 | `DustType<T>()` 返回 `332+k`；按 10000 写的判断要更新 |
| 生成 | `ModDust.Spawn` 与 `Dust.NewDust` 直传都可用；color 缺省对齐原版 `default(Color)` |
| 语义不变 | `Update` 返回 false = 接管本帧；`dust.active = false` = 杀；`MidUpdate` true = 抵消刹车；`PreDraw` false = 跳过精灵 |
| 仍不生效 | 帧动画、光照（`GetAlpha` 的 lightColor 仍是 dust.color）、碰撞、联网 |

## 批次补充（2026-11-21）

- `ModDust.Update` 的返回值语义改为「false = 本帧跳过管线物理，尘土存活、下一帧继续派发」；回收尘土的惯用写法是在 `Update` 里置 `dust.active = false`，管线每帧派发后检查它，命中即走与自然消亡相同的回收路径。`DustPipeline.UpdateDust` 的 Live 循环同步加入 `active` 检查（先查 `active` 再看返回值），`Content/ModDust.cs` 的 `Update` 契约注释一并更新。
- `MidUpdate` 从第一遍循环拆出，成为独立的第二遍后置遍历：对仍在 `Live` 的尘土派发（含本帧返回 false 跳过物理的），返回 true 才做 `velocity.X /= 0.99`（抵消本帧横向刹车），回调异常不除；回调中置 `dust.active = false` 的尘土不做除法并当场移出 `Live`。第二遍不做物理、数值卫生与出屏收缩回收判定。`Content/ModDust.cs` 的 `MidUpdate` 契约注释同步改准（撤销刹车而非追加）。

## 批次补充（2026-11-18，队列第 8 行）

- `DustID.TML` 的 191 个原版尘土号别名补齐：`DustIdTml` 扩展成员（interop 不能写 partial，const 以只读静态属性提供）。
- 尘土本体无改动：`Type`/`UpdateType`/`Texture2D` 维持不移植（mod 尘土不进游戏图集的自管设计），详见台账 §17.2。
