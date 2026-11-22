# 内容管线：坐骑（Mount）

## 1. 职责

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `Mount.SetMount` / `Mount.DoSpawnDust` | `SetMount`（骑上去、可跳过原版尘土） |
| `Mount.Dismount` | `Dismount`（下来、可跳过原版尘土） |
| `Mount.UpdateEffects` | `UpdateEffects` |
| `Mount.UpdateFrame` | `UpdateFrame`（false = 跳过游戏动画） |
| `Mount.JumpHeight` / `Mount.JumpSpeed` | `JumpHeight` / `JumpSpeed`（改这次的计算结果） |
| `Mount.UseAbility` / `Mount.AimAbility` | `UseAbility` / `AimAbility` |
| `Mount.Draw` | `Draw`（四层，每层一次） |
| `Mount.Initialize` | mod 的 `MountData` 写回 `Mount.mounts` |

**不接**：坐骑 buff 的加/减（走 Buff 管线）、`ModMount` 的贴图上传（走安装管线的贴图阶段）。

坐骑是唯一一类"游戏对象自己就是内容载体"的内容：玩家的 `Player.mount` 是一个 `Mount` 实例，它的行为由 `Mount.mounts[type]` 那张 `MountData` 表决定。所以这条管线的核心工作**不是分派**（分派只有十个钩子），而是**把作者的描述变成一张合法的 MountData 并塞进游戏表里**。

## 2. 我们的设计

### 2.1 两个文件与一个静态面

```
Pipelines/Mount/
  MountPipeline.cs   模块：十个游戏入口的分派 + 六个安装门 + 绘制接管
  MountTable.cs      类型 ↔ 实例、MountData 建立、表扩容与写回、buff 标记
```

`Content/Loaders/MountLoader.cs` 提供静态查询：`MountCount`（当前类型总数）、`GetMount`（类型号反查实例）、`IsModMount`（一份 `MountData` 是不是 mod 坐骑的，按指针比对）。

安装走门控：六个门（骑 / 每帧 / 跳 / 能力 / 绘制 / 表重建），条件都是"存在任意一匹 mod 坐骑"。分派期用安装时预扫好的覆写位掩码（`HookScan` 对实例类型算一次挂在实例上），跳高 / 跳速 / 绘制这三个高频入口有位掩码前置，未覆写直接走原版；其余钩子直接转作者（未覆写时是一次空方法调用）。

表的职责与稀有度/前缀那两条同构：**安装期和运行时要写同一张表**（`Mount.Initialize` 会把 `Mount.mounts` 按 `MountID.Count` 重建，mod 的槽位会变 null）。

### 2.2 MountData 是"建一次、写多次"

* **建一次**：每匹坐骑一个 `Mount.MountData`，八张贴图层（`_Back/_BackGlow/_BackExtra/_BackExtraGlow/_Front/...`）按"文件存在才挂"填进去，然后补帧默认值。
* **写多次**：`Reapply` 把 mod 的 `MountData` 重新塞回 `Mount.mounts[type]`（原版槽照抄、mod 槽写入），同步把 `MountID.Sets` 十二张表扩到新长度、重标坐骑 buff，最后整表发布并同步 `MountID.Count`。它在内容就绪时跑一次，之后每次 `Mount.Initialize` 都再跑——写回必须幂等，**少写一次，骑上去就是每帧空引用**。

### 2.3 帧默认值为什么必须由管线补

`MountData` 是一张给引擎用的表，作者只写"我长什么样"。但引擎会无条件读三样东西：

* `totalFrames`（绘制要拿它做除数）；
* `playerYOffsets`（玩家位置偏移，按 `totalFrames` 索引）；
* 七组动画的 `FrameStart/FrameCount`（站立/跑/空中/飞/游/冲刺/待机）。

作者漏写任意一个，轻则画不出来重则每帧空引用。所以管线在建 `MountData` 时把它们统一兜成"单帧停在 0、偏移全 0"，贴图尺寸读得到就顺手补上（贴图还没上传时留 0，作者自己写）。只填 `<= 0`/null 的字段，作者在 `SetStaticDefaults` 里写过的值不会被覆盖。

### 2.4 骑乘的两个入口

`SetMount` 和 `DoSpawnDust` 都要能跑作者的 `SetMount`：

* 作者的 `SetMount` 想拿到"尘土可以跳过"的决定权，所以它应该在**原版撒尘土之前**跑；
* 但 `Mount.SetMount` 里 `_type` 可能还没换成新值，那时判断"这是哪匹坐骑"是不可靠的。

所以管线在 `Mount.SetMount` 里**只记上下文**（哪匹、跑过没有），真正的调用发生在 `DoSpawnDust`——那时 `_type` 已经对了。`Mount.SetMount` 之后若发现原版根本没撒尘土（没有 `DoSpawnDust` 调用），再补一次，保证作者总会被调、且只调一次。

`Dismount` 的 skipDust 记在管线字段上，由下一次 `DoSpawnDust(isDismounting: true)` 消费，用完即清。换骑与下来时顺手把 `Mount._mountSpecificData` 置 null，同步清掉 tML 形状的侧字典槽 `Mount.MountSpecificData`（见 2.6）。

### 2.5 绘制：有贴图就接管

`Mount.Draw` 每帧按层号调四次（Back / BackExtra / Front / FrontExtra），每次给出一整套绘制参数。管线只在**这层有主体贴图**时接管；没有主体贴图就交回原版（那层本来也没东西可画）。

接管时管线先把原版会给的那套值算出来——屏幕坐标、帧矩形（`totalFrames` 不大于 0 时按整张贴图算）、矿车倒退时的水平翻转、受光照提亮的发光色——连同主体贴图作为 ref 参数面交给作者的 `Draw`：

* 主体贴图、位置、帧、颜色、发光颜色、旋转、原点、缩放、翻转，管线都用作者改后的值推送；
* 作者往首参 `List<DrawData>` 里加的条目按顺序追加进 `PlayerDrawSet.DrawDataCache`（缓存满时该条丢弃，不扩容）；
* 返回 false → 这层的主体与发光都不推送（追加的条目保留）；
* 返回 true → 管线推送主体与发光两层，`shader` 统一为 `Mount.currentShader`。

发光层贴图固定用建表时挂载的那张（`glowTexture` ref 参数当前不回读）；发光层颜色是作者改后的发光色再乘主体颜色的 alpha——两次构色（初值、乘 alpha 之后）都先按字节截断、走 0..255 的 int 构造器，为什么见第 3 节。

### 2.6 作者的挂载数据

每骑数据有两个槽，换骑与下来时都清空：

* 游戏自带的 `Mount._mountSpecificData`：槽随 `Mount` 原生对象走、随游戏 GC 管理，管线不另建按玩家索引的表，只在换骑与下来时置 null。**不清就是跨坐骑串数据**，而且玩家死亡重生后 whoAmI 不变，会读到上一条命的值。存进去的对象必须是 interop 可见类型。
* tML 形状的 `Mount.MountSpecificData`（`MountTml` 侧字典，按挂载实例指针挂数）：interop 的原生 object 字段放不进纯托管对象，作者的任意托管数据走这个槽；换骑 / 下来逐实例清，`Mount.Initialize`（表重建）时全清。

## 3. 取舍理由

* **为什么 `MountData` 不留空、要一口气补全？** 引擎读它是不判空的。宁可给"单帧静止"这种观感上奇怪但不崩的默认值，也不能留 null。
* **为什么 `MountID.Sets` 一次扩十二张？** 它们都按 `MountID.Count` 索引。只扩用到的那几张，等于把"哪张没扩"这个坑留给下一个读它的原版代码。
* **为什么写回要先建副本再发布？** 写回可能发生在任意两帧绘制之间（游戏重建表之后），半成品表不能让读者看见：副本在托管侧整张建好（原版槽照抄 + mod 槽写入 + Sets 扩长），再一次替换引用并同步 `MountID.Count`，任何时机的读者要么看到旧表要么看到新表。引用数组的替换逐项走运行时 GC 写屏障，不是整块拷贝，这一步不能省。
* **为什么绘制是"有贴图就接管"，而不是"作者没改就交回原版"？** 管线重建的那套值就是原版对这层的绘制——同一层选择、同一帧公式、同一光照与发光色——"没改就交回"没有行为收益，却要在每帧每层的路径上多一次全字段比对。作者能改的参数全在 ref 面上，追加绘制走首参列表，表达能力不减。
* **为什么发光色要按字节截断？** 发光色公式 `0.25 × 光照 + 191.25` 在 0..255 空间上做，结果落在 191..255；XNA 的 `Color(float,…)` 构造器把参数按 0..1 归一化解释，0..255 的值进去会全部钳到 1.0，发光层会变成一块不透明的纯白。托管侧的构色惯例因此固定为一条边界：0..1 的值走 float 构造器（如 buff 图标的 alpha 色），0..255 的值截断后走 int 构造器（如晶石的提亮色）。发光层的两次构色都遵守这条边界。
* **为什么 `JumpHeight`/`JumpSpeed` 要反查玩家？** 手机版的这两个方法没有 `Player` 参数（与 PC 版签名不同），而作者 API 需要玩家。按 `Mount` 对象地址比对 `player.mount` 是唯一能拿到"骑它的那个玩家"的办法。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| 某层贴图缺失 | 该层留空，主体与发光都不画 |
| 层贴图加载抛错 | 记一条；该层视为不存在 |
| `SetStaticDefaults` 抛错 | 记一条；该坐骑仍可用（`MountData` 已建好） |
| `Mount.Initialize` 之后的写回失败 | 记一条；mod 坐骑暂不可骑，下一次写回补回，不影响原版坐骑 |
| `SetMount` / `Dismount` 抛错 | 记一条；骑乘本身照常完成，尘土按原版 |
| `UpdateFrame` 抛错 | 视为 true（照跑原版动画） |
| `Draw` 抛错 | 交回原版画这一层 |
| 绘制缓存满 | 该次写入丢弃（不扩容） |
| `ApplyBuffFlags` 时表长不够 | 跳过这一项（等下一次写回再补） |

作者钩子的异常按（mod 名, 钩子）去重，同一钩子只报首条。

## 5. 扩展方式

新增坐骑相关时机：`HookId` 末尾追加 → `ModMount` 加同名 `virtual` → 在 `MountPipeline` 对应入口分派。新增一层贴图：`MountTable.Layers` 加一条 `(后缀, 写哪个字段)`，绘制侧 `LayerOf` 同步。

## 6. 与其它管线的边界

* **Buff 管线**：坐骑 buff 的加/减、每帧效果归 Buff 管线；本管线只负责"指向坐骑的 buff 不显示时间、不存档"这条标记。
* **玩家管线**：玩家的移动/绘制链归玩家管线；本管线只在 `Mount.*` 的入口上分派。
* **物品管线**：召唤坐骑的物品走 `ModItem.UseItem`，归物品管线。
* **安装管线**：贴图上传归安装管线；本管线的 `MountData` 建立在 `Static` 阶段（它必须在 `Mount.mounts` 被索引之前完成）。
