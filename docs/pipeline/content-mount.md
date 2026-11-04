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

### 2.1 两个文件

```
Pipelines/Mount/
  MountPipeline.cs   模块：十个游戏入口的分派 + 按玩家记的作者数据
  MountTable.cs      类型 ↔ 实例、MountData 建立、表扩容与写回、buff 标记
```

表的职责与稀有度/前缀那两条同构：**安装期和运行时要写同一张表**（`Mount.Initialize` 会把 `Mount.mounts` 按 `MountID.Count` 重建，mod 的槽位会变 null）。

### 2.2 MountData 是"建一次、写多次"

* **建一次**：每匹坐骑一个 `Mount.MountData`，八张贴图层（`_Back/_BackGlow/_BackExtra/_BackExtraGlow/_Front/...`）按"文件存在才挂"填进去，然后补帧默认值。
* **写多次**：每次 `Mount.Initialize` 之后把 mod 的 `MountData` 重新塞回 `Mount.mounts[type]`，并把 `MountID.Sets` 扩到新长度。写回必须幂等——**少写一次，骑上去就是每帧空引用**。

### 2.3 帧默认值为什么必须由管线补

`MountData` 是一张给引擎用的表，作者只写"我长什么样"。但引擎会无条件读三样东西：

* `totalFrames`（绘制要拿它做除数）；
* `playerYOffsets`（玩家位置偏移，按 `totalFrames` 索引）；
* 七组动画的 `FrameStart/FrameCount`（站立/跑/空中/飞/游/冲刺/待机）。

作者漏写任意一个，轻则画不出来重则每帧空引用。所以管线在建 `MountData` 时把它们统一兜成"单帧停在 0、偏移全 0"，作者之后在 `SetStaticDefaults` 里改就行。

### 2.4 骑乘的两个入口

`SetMount` 和 `DoSpawnDust` 都要能跑作者的 `SetMount`：

* 作者的 `SetMount` 想拿到"尘土可以跳过"的决定权，所以它应该在**原版撒尘土之前**跑；
* 但 `Mount.SetMount` 里 `_type` 可能还没换成新值，那时判断"这是哪匹坐骑"是不可靠的。

所以管线在 `Mount.SetMount` 里**只记上下文**（哪匹、跑过没有），真正的调用发生在 `DoSpawnDust`——那时 `_type` 已经对了。`Mount.SetMount` 之后若发现原版根本没撒尘土（没有 `DoSpawnDust` 调用），再补一次，保证作者总会被调、且只调一次。

### 2.5 绘制：快照比对

`Mount.Draw` 每帧被调用四次（Back / BackExtra / Front / FrontExtra），每次给出一整套绘制参数（贴图、位置、帧、颜色、旋转、原点、缩放、翻转）。

管线的做法是：**先把原版会给的那套值算出来，交给作者改，再比一次**。

* 作者返回 false → 这层不画；
* 作者一个值都没改 → 交回原版画（原版的特例——染色、拖影、闪光——继续生效）；
* 作者改了任意一个值 → 管线自己往 `PlayerDrawSet` 里塞绘制数据，跳过原版。

比对用的是一个 `readonly record struct` 快照。旧实现在这里会把二十几个值拼成字符串再比字符串（每层每帧一次分配 + 装箱），这条路径在第 2 节意义上"每帧都要走"，所以必须零分配。

### 2.6 作者的额外数据

`ModMount.GetSpecificData/SetSpecificData` 按 `player.whoAmI` 记在管线实例上：骑上换了一匹、或下来时清掉。**不清就是跨坐骑串数据**，而且玩家死亡重生后 whoAmI 不变，会读到上一条命的值。

## 3. 取舍理由

* **为什么 `MountData` 不留空、要一口气补全？** 引擎读它是不判空的。宁可给"单帧静止"这种观感上奇怪但不崩的默认值，也不能留 null。
* **为什么 `MountID.Sets` 一次扩十二张？** 它们都按 `MountID.Count` 索引。只扩用到的那几张，等于把"哪张没扩"这个坑留给下一个读它的原版代码。
* **为什么绘制要做"没改就交回原版"？** 若一律自己画，就会丢掉原版对面部染色、无敌闪烁、速度拖影这些特例；而作者最常见的用法恰恰是"只改个位置"，此时原版的其它特例应当保留。
* **为什么用值类型快照而不是逐字段判等？** 十个字段逐条 `if` 判等要写十行、加字段就得改两处；`record struct` 自动生成 `Equals`，还能被 JIT 内联。
* **为什么 `JumpHeight`/`JumpSpeed` 要反查玩家？** 手机版的这两个方法没有 `Player` 参数（与 PC 版签名不同），而作者 API 需要玩家。按 `Mount` 对象地址比对 `player.mount` 是唯一能拿到"骑它的那个玩家"的办法。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| 某层贴图缺失 | 该层留空，原版也不会画 |
| `SetStaticDefaults` 抛错 | 记一条；该坐骑仍可用（`MountData` 已建好） |
| `Mount.Initialize` 之后的写回失败 | 记一条；这一步失败会让 mod 坐骑不能骑，但不影响原版坐骑 |
| `SetMount` / `Dismount` 抛错 | 记一条；骑乘本身照常完成，尘土按原版 |
| `UpdateFrame` 抛错 | 视为 true（照跑原版动画） |
| `Draw` 抛错 | 交回原版画这一层 |
| `ApplyBuffFlags` 时表长不够 | 跳过这一项（等下一次写回再补） |

## 5. 扩展方式

新增坐骑相关时机：`HookId` 末尾追加 → `ModMount` 加同名 `virtual` → 在 `MountPipeline` 对应入口分派。新增一层贴图：`MountTable.Layers` 加一条 `(后缀, 写哪个字段)`，绘制侧 `LayerOf` 同步。

## 6. 与其它管线的边界

* **Buff 管线**：坐骑 buff 的加/减、每帧效果归 Buff 管线；本管线只负责"指向坐骑的 buff 不显示时间、不存档"这条标记。
* **玩家管线**：玩家的移动/绘制链归玩家管线；本管线只在 `Mount.*` 的入口上分派。
* **物品管线**：召唤坐骑的物品走 `ModItem.UseItem`，归物品管线。
* **安装管线**：贴图上传归安装管线；本管线的 `MountData` 建立在 `Static` 阶段（它必须在 `Mount.mounts` 被索引之前完成）。
