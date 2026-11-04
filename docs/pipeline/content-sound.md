# 内容管线：音效（Sound）

## 1. 职责

Mod 音效是"数据 + 播放"两段，没有作者钩子（`HookId` 不增项）。

| 时机 | 做什么 |
|---|---|
| 作者 `new SoundStyle(path, options)`（加载期，不是游戏钩子） | 定位 `.ogg/.wav/.mp3`、交给 Android SoundPool 异步解码、发一个表内下标 |
| `LegacySoundPlayer.PlaySound`（type = 1000） | 认出 Mod 声音（`Style` 就是表下标），按选项播；**不**进原版 `SoundEffect` 表 |
| `Main.UpdateAudio`（每 10 帧） | 按估算时长回收已结束的流，维护 `MaxInstances` 计数 |
| `SoundEngine.PlaySound / FindActiveSound / GetSoundState / StopSound` | 门面：播放、同时在响的个数、句柄状态、停止 |

**不接**（本版没有可用入口，或主动不做；理由见第 2 节）：

* 距离衰减与左右声道——`AndroidAudio.PlaySound` 只暴露一个音量参数，没有左右声道入口；
* `SoundStyle.Type`（`SoundType.Ambient` / `Music` / `Sound` 三个音量组）——本版统一乘 `Main.soundVolume`；
* `IsLooped`、`PlayOnlyIfFocused`、`Identifier`、`Variants`——Pylon 的 `SoundStyle` 里本来就没有这些字段；
* 后台自动暂停：`SoundPool.autoPause` 没有调用点，和旧实现一致（见第 2 节"已知缺口"）。

## 2. 我们的设计

### 2.1 文件

```
Pipelines/Sound/
  SoundPipeline.cs   模块：两个游戏入口 + 播放策略（限流、回收、句柄）
  SoundTable.cs      注册表与活动流表（密集 struct + 侵入式链表），无逐次分配
Core/AudioFiles.cs   路径解析（Sound 与 Music 共用；只在注册期跑）
```

### 2.2 注册一次，播放不再拼字符串

`SoundStyle` 构造时就把下标写进 `Style`（管线发号），播放路径拿 `style.Style` 直接索引。
旧实现每次播放都调 `Register(style)`，内部再拼一遍 "路径|音量|音高|..." 的字符串做字典查找——那是每声一次分配。
现在字符串只在注册期出现，热路径是"一次边界检查 + 一个下标"。

### 2.3 数据：密集 struct + ref

```csharp
private struct SoundEntry   // 按注册下标密集存放
{
    public int SoundId;                     // SoundPool id；0 = 没加载成功
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

* 条目是 struct 数组，按注册下标密集索引，不装箱、不复制；活动流是第二个 struct 数组 + 空闲链表 + 每条目一条双向链表，播放/停止**零分配**（旧实现每次播放 `new Stream`、每句柄一次字典插入）；
* 回调句柄 = `(代次 << 12) | 槽位`。代次在槽位复用时自增，老句柄落到新流上会被代次比对挡掉，因此不需要"句柄 → 条目"的字典。

### 2.4 播放决策

1. `SoundId == 0` 或 SoundPool 不可用 → 不播（每路径只报一次）；
2. 先回收本条目过期的流；
3. `Active >= MaxInstances` 时：`IgnoreNew` 直接拒绝，`ReplaceOldest` 停掉链表头（最早那条）；
4. 音量 = 选项音量 × `volumeScale` × `Main.soundVolume`，音高 = 基值 + 随机抖动 + `pitchOffset`，倍率夹在 0.5~2；
5. `SoundPool.play` 返回 0（还在解码 / 被拒）→ 不记账、返回 0。

### 2.5 安装

安装只做两件事：挂 `LegacySoundPlayer.PlaySound` 与 `Main.UpdateAudio`。音效不占游戏 ID，所以没有表扩容、也不参与安装管线的阶段推进。

## 3. 取舍理由

**4.1 为什么不像参考实现那样把播放交给内核？**
参考实现的内核同时管声道、限流与循环；我们的内核导出面里没有这些（`ig*` 系列只有音频解码的一小段），
而 SoundPool 已经提供了限流所需的全部信息（stream id、时长估算）。把"限流 + 回收"留在托管侧，换来的是不需要和内核约定新的 ABI。

**4.2 为什么走 `LegacySoundPlayer.PlaySound` 而不是通用 `SoundStyle` 路径？**
通用路径（`SoundEngine.PlayTrackedSound` / `SlotId`）要求声音已经在 `SoundEffect` 表里；这版 Unity 造不出 `AudioClip`，解码只能走 SoundPool。
`LegacySoundPlayer.PlaySound` 是唯一"游戏把 `(type, x, y, style)` 原样交给我们"的入口，`SoundEngine.PlaySound(...)` 的所有重载最终都落到这里，所以一个 detour 覆盖全部调用方式。

**4.3 为什么不做距离衰减？**
`AndroidAudio.PlaySound(id, volume, rate)` 只接受一个音量。没有左右声道，做"半套"衰减的结果是：屏幕外的声音变小、但永远在正中间——比不做更容易让作者以为"定位生效了"。
本版选不做，并在第 1 节列明；要补齐得先把声道加进 JNI 包装层（另一个改动）。

**4.4 为什么注册时就解码、句柄要编代次？**
SoundPool 的解码是异步的，第一次 `play` 往往返回 0；注册期就开始解码，等于把关卡加载的时间用来准备音频，这是"第一次挥剑没声音"的唯一低成本解法。
句柄编代次则是因为播放是高频路径：字典插入是一次分配 + 一次哈希，代次编码是两次位运算；代价是槽位上限 4096，而 SoundPool 只开 32 路，这个量级打不到。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| 路径找不到文件 | 注册成功但 `SoundId = 0`；首次播放时按路径报一次，之后静默 |
| SoundPool 不可用 | 首次播放报一次，之后静默；游戏其余部分不受影响 |
| `SoundPool.play` 返回 0（未解码 / 被拒） | 不记账、返回 0；每路径报一次 |
| `StopSound` 抛错 | `Safe.Report` 一条，仍然释放表项（不能因为停不掉就泄漏槽位） |
| 活动流超过 4096 | 拒绝这次播放，报一次；不覆盖正在响的流 |
| 钩子内部异常 | `ContentErrors.Report` 一条，本次播放放弃，不回落原版（原版没有这个声音） |

**已知缺口**：App 进后台时 SoundPool 不会被 `autoPause`，回前台可能有一两条流已在系统层被截断，表里的计数要等估算时长到期才回收；旧实现同样没处理，补齐前要先在真机确认 `autoPause` 不踩 Android 音频系统。
另外 SoundPool 查不到真实时长，流的存活期按 1 秒估算：超过 1 秒的长音效会被提前从 `MaxInstances` 计数里释放。

## 5. 扩展方式

* **加一个播放选项**：`SoundStyle.Options` 加公共字段（必须字段，不能改属性）→ `SoundEntry` 加对应字段 →
  `Register` 抄进去 → 播放路径读 `ref entry`。去重 key 也要带上它，否则两组选项会共用一格。
* **加一个音量组**：把 `Main.soundVolume` 换成按 `SoundType` 选择（`Main.ambientVolume` / `Main.musicVolume`），
  只需要改 `Play` 里算 master 的一行。
* **真的要做定位**：先给 `AndroidAudio.PlaySound` 加左右声道重载，再在 `Play` 里按屏幕中心算声像；
  结构不用动，位置参数已经一路传到 `Play` 的入口（本版只是不用它）。
* **将来加作者钩子**（例如 `ModSound`）：`HookId` 末尾追加 → 新内容基类加 `virtual` → 在 `Play` 前后分派；本版没有这个内容类型，因此 `HookId` 没有增项。

## 6. 与其它管线的边界

* **音乐管线**：共用 `Core/AudioFiles.cs` 的路径约定与 `Core/AndroidAudio.cs` 的 JNI 包装；除此之外互不调用。音效不参与"该放哪首 BGM"的决策，音乐也不读音效表。
* **触屏按钮 / 配置 UI**：它们放的是原版音效（`SoundEngine.PlaySound(int)`），不经过本管线。
* **网络管线**：音效不同步。对端在自己的 Mod 目录里解析同一路径，播自己的那份；这是"同一份资源"而不是"同一路流"。
* **物品 / 弹幕 / NPC 管线**：它们只持有 `SoundStyle` 引用并在合适的时机调 `SoundEngine`，不碰本管线的表。
