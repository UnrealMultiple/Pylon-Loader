# 内容管线：音效（Sound）

## 1. 职责

Mod 音效是"数据 + 播放"两段，没有作者钩子（`HookId` 不增项）。

| 时机 | 做什么 |
|---|---|
| 作者 `new SoundStyle(path, options)`（加载期，不是游戏钩子） | 按候选路径（本体或各变体）定位音频文件、交给 Android SoundPool 异步解码、发一个表内下标 |
| `LegacySoundPlayer.PlaySound`（type = 1000） | 认出 Mod 声音（`Style` 就是表下标），按选项播；**不**进原版 `SoundEffect` 表 |
| `Main.UpdateAudio`（每 tick） | 有排队时先补播未就绪的请求；每 10 tick 按估算时长回收已结束的流，维护 `MaxInstances` 计数 |
| `SoundEngine.PlaySound / FindActiveSound / GetSoundState / StopSound` | 门面：播放、同时在响的个数、句柄状态、停止 |

**`SoundStyle` 已声明、本版播放不消费的字段**（字段在，只是播放路径不读）：

* `Type`（`SoundType.Ambient` / `Music` / `Sound` 三个音量组）——本版统一乘 `Main.soundVolume`，不按 `Type` 分流，去重 key 也不含它；
* `IsLooped`、`PlayOnlyIfFocused`、`Identifier`——SoundPool 的播放模型里没有对应入口，本版不读。

**本版不做**（入口缺失或既定取舍）：

* 距离衰减与左右声道——`AndroidAudio.PlaySound` 只暴露一个音量参数，没有左右声道入口；
* 切后台自动暂停——`SoundPool.autoPause` 没有调用点，音效流不随 App 进后台暂停（音乐侧在同一生命周期入口有自己的暂停/恢复，音效侧不接，见 3.5）。

## 2. 我们的设计

### 2.1 文件

```
Pipelines/Sound/
  SoundPipeline.cs   模块：两个游戏入口 + 播放策略（限流、补播、回收、句柄）
  SoundTable.cs      注册表与活动流表（密集 struct + 侵入式链表），无逐次分配
Core/AudioFiles.cs   路径解析（Sound 与 Music 共用；只在注册期跑）
```

### 2.2 注册：发号、去重、路径解析

`SoundStyle` 第一次被用到时就把下标写进 `Style`（管线发号），播放路径拿 `style.Style` 直接索引。字符串查找只发生在注册期的去重字典（key = 路径 + 变体串 + 音量/音高/PitchVariance/MaxInstances/LimitBehavior 的文本串），热路径是"一次边界检查 + 一个下标"；同一组（路径 + 变体 + 选项）复用同一格，因此也共用同一个 `MaxInstances` 计数——两组只差变体的 `SoundStyle` 各占一格。

变体语义：`Variants` 是一组**数字后缀**，逐个追加到路径末尾。`new SoundStyle("Sounds/Blade", 3)` 等价于显式给出后缀 `[1, 2, 3]`，即依次加载 `Sounds/Blade1/2/3`（起始后缀可用 `(path, variantSuffixesStart, numberOfVariants)` 构造器调整；`(path, ReadOnlySpan<int>)` 构造器接受任意后缀列表）。`Variants` 为空时只加载 `SoundPath` 本体一个文件。每个候选（本体或某个变体）独立定位与解码：单个候选失败只是被排除出候选集，其余照常；全部失败则这一格没有任何可播 id（见 2.4）。候选路径与本体的路径解析是同一套约定，同一个文件（比如两条 `SoundStyle` 共享某变体）只解码一次。

路径解析（`AudioFiles.Anywhere`）两级探测：先查 `Mod 根/Assets/`（前缀可省、`Assets/` 目录大小写兜底），落空再回退 Mod 根；路径带已注册扩展名（`.ogg/.wav/.mp3`）时按原文件名探测、不追加，不带则依次拼这三个扩展名。全局查找先按首段 mod 名精确归属到那个 Mod，失配才按加载序逐 Mod 回退。

### 2.3 数据：密集 struct + ref

```csharp
private struct SoundEntry   // 按注册下标密集存放
{
    public int[] SoundIds;                  // SoundPool id 候选集；空 = 没有任何候选加载成功
    public float Volume, Pitch, PitchVariance;
    public int MaxInstances, LimitBehavior; // 0 = 不限 / 0 = IgnoreNew，其它 = ReplaceOldest
    public int Head, Tail, Active;          // 活动流链表
}
private struct SoundStream  // 每次播放一格，不 new 对象
{
    public int StreamId, Entry, Generation;
    public long StartedMs;
    public int DurationMs, Next, Prev;
    public bool Live;
}
```

取用写 `ref SoundEntry entry = ref _table.Entry(i)`、`ref SoundStream stream = ref _table.Stream(slot)`：

* 条目是 struct 数组，按注册下标密集索引，不装箱、不复制；活动流是第二个 struct 数组 + 空闲链表 + 每条目一条双向链表，播放/停止**零分配**；
* 回调句柄 = `(代次 << 12) | 槽位`。代次在槽位复用时自增，老句柄落到新流上会被代次比对挡掉，因此不需要"句柄 → 条目"的字典。

### 2.4 播放决策

1. 候选集为空（注册时没有候选加载成功）或 SoundPool 不可用 → 不播（每路径/每池只报一次）；这两种不算"未就绪"，不排队重试；
2. 先回收本条目过期的流；
3. `MaxInstances > 0` 且 `Active >= MaxInstances` 时：`LimitBehavior = IgnoreNew` 直接拒绝，`ReplaceOldest` 停掉链表头（最早那条）；`MaxInstances = 0` 不限；
4. 音量 = `Volume × volumeScale`（夹 0~1）× `Main.soundVolume`（夹 0~1）。**音高契约**：
   `pitch = Pitch + (NextDouble() − 0.5) × PitchVariance + pitchOffset`——随机抖动是 **±PitchVariance/2**；
   倍率 = `2^pitch`，夹在 **0.5~2**。因此 `PitchRange(min, max)`（它把 `PitchVariance` 定义为 `max − min`）设定的区间与实际播放区间一致；
5. 从候选集挑本次要播的 id：单候选直接用；多候选按均匀随机挑，每次播放独立抽样、变体间等概率；
6. `SoundPool.play` 返回 0（还没解码好 / 被拒）→ 记为"未就绪"，进补播队列（见 2.6），本次返回 0、不记账。

### 2.5 安装

安装只做两件事：挂 `LegacySoundPlayer.PlaySound` 与 `Main.UpdateAudio`，在第一个 Mod 音效注册时才挂（`GateHub` 惰性门）。音效不占游戏 ID，所以没有表扩容、也不参与安装管线的阶段推进。

### 2.6 解码未就绪的补播

SoundPool 的解码是异步的，而且没有"解码完成"的查询接口：第一次 `play` 常常返回 0。管线把这种失败当"未就绪"而不是"坏文件"：请求进全局补播队列（上限 24 条），`Main.UpdateAudio` 每 tick 重试一次，就绪即补播；超过 45 tick（约 0.75s）放弃、释放名额。注册期就开始解码，把关卡加载的时间用来准备音频——这是"第一次挥剑没声音"的解法。

## 3. 取舍理由

**3.1 为什么把"限流 + 回收"留在托管侧？** SoundPool 已经提供了限流所需的全部信息（stream id、时长估算），策略留在托管层就不需要和底层约定新的接口面；`SoundEntry`/`SoundStream` 的密集结构让这条路径零分配。

**3.2 为什么走 `LegacySoundPlayer.PlaySound` 而不是通用 `SoundStyle` 路径？**
通用路径（`SoundEngine.PlayTrackedSound` / `SlotId`）要求声音已经在 `SoundEffect` 表里；这版 Unity 造不出 `AudioClip`，解码只能走 SoundPool。
`LegacySoundPlayer.PlaySound` 是唯一"游戏把 `(type, x, y, style)` 原样交给我们"的入口，`SoundEngine.PlaySound(...)` 的所有重载最终都落到这里，所以一个 detour 覆盖全部调用方式。

**3.3 为什么不做距离衰减？**
`AndroidAudio.PlaySound(id, volume, rate)` 只接受一个音量。没有左右声道，做"半套"衰减的结果是：屏幕外的声音变小、但永远在正中间——比不做更容易让作者以为"定位生效了"。
本版选不做，并在第 1 节列明；要补齐得先把声道加进 JNI 包装层（另一个改动）。

**3.4 为什么注册时就解码、句柄要编代次？**
解码是异步的，注册期就开始等于提前准备（见 2.6）。
句柄编代次是因为播放是高频路径：字典插入是一次分配 + 一次哈希，代次编码是两次位运算；代价是槽位上限 4096，而 SoundPool 只开 32 路，这个量级打不到。

**3.5 为什么切后台不暂停音效流？**
暂停入口（`AndroidAudio.PauseAll/ResumeAll`，含 `autoPause`）已经备好但没有接线；接它之前要先在真机确认 `autoPause` 不与 Android 音频系统的后台行为打架。本版按"不暂停"运行（既定取舍，不是遗漏）：后台期间 SoundPool 流由系统处理，个别流可能被截断，表内计数等估算时长到期自然回收。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| 某个候选（本体或某变体）找不到文件 / SoundPool 拒绝解码 | 注册成功，该候选不进候选集；每条候选路径在注册期各报一次，之后静默 |
| 全部候选加载失败 | 这一格无声：注册照常，播放直接返回 0，首播按路径报一次；不算"未就绪"、不进补播队列 |
| SoundPool 不可用 | 首次播放报一次，之后静默；不排队、不重试；游戏其余部分不受影响 |
| `SoundPool.play` 返回 0（未解码 / 被拒） | 记"未就绪"，进补播队列（≤24 条、≤0.75s）逐 tick 重试；每路径报一次 |
| 补播超龄（45 tick） | 放弃这次播放，释放队列名额 |
| 活动流超过 4096 | 停掉刚开的那条流、拒绝这次播放，报一次；不覆盖正在响的流 |
| `StopSound` 抛错 | `Safe.Report` 一条，仍然释放表项（不能因为停不掉就泄漏槽位） |
| 钩子内部异常 | `ContentErrors.Report` 一条，本次播放放弃，不回落原版（原版没有这个声音） |

**时长与计数**：SoundPool 查不到真实时长，流的存活期按 1 秒估算：超过 1 秒的长音效会被提前从 `MaxInstances` 计数里释放（限流被低估）；不足 1 秒的会多占约 1 秒计数（限流被高估，`ReplaceOldest` 可能提前停掉还在响的流）。

**后台行为**：音效流不随 App 进后台暂停（见 3.5）；回前台个别流可能已在系统层被截断，表内计数要等估算时长到期才回收。

## 5. 扩展方式

* **加一个播放选项**：`SoundStyle` 加 `init` 属性（`Copy`/`With*` 一并带过去）→ `SoundEntry` 加对应字段 →
  `Register` 抄进去 → 播放路径读 `ref entry`。去重 key 也要带上它，否则两组选项会共用一格。
* **加一个音量组**：把 `Main.soundVolume` 换成按 `SoundType` 选择（`Main.ambientVolume` / `Main.musicVolume`），
  只需要改播放里算 master 的一行（`Type` 字段已备好）。
* **真的要做定位**：先给 `AndroidAudio.PlaySound` 加左右声道重载，再在播放里按屏幕中心算声像；
  结构不用动，位置参数已经一路传到播放入口（本版只是不用它）。
* **接后台暂停**：生命周期入口已在（音乐管线挂的同一处 `OnApplicationPause`），音效侧补调 `AndroidAudio.PauseAll/ResumeAll` 即可。
* **将来加作者钩子**（例如 `ModSound`）：`HookId` 末尾追加 → 新内容基类加 `virtual` → 在播放前后分派；本版没有这个内容类型，因此 `HookId` 没有增项。

## 6. 与其它管线的边界

* **音乐管线**：共用 `Core/AudioFiles.cs` 的路径约定与 `Core/AndroidAudio.cs` 的 JNI 包装；除此之外互不调用。音效不参与"该放哪首 BGM"的决策，音乐也不读音效表；后台暂停也各管各的（音乐接了生命周期入口，音效没接）。
* **触屏按钮 / 配置 UI**：它们放的是原版音效（`SoundEngine.PlaySound(int)`），不经过本管线。
* **网络管线**：音效不同步。对端在自己的 Mod 目录里解析同一路径，播自己的那份；这是"同一份资源"而不是"同一路流"。
* **物品 / 弹幕 / NPC 管线**：它们只持有 `SoundStyle` 引用并在合适的时机调 `SoundEngine`，不碰本管线的表。
