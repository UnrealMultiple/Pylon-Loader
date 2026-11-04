# 内容管线：音乐（Music）

## 1. 职责

音乐管线管三件事：**给 Mod 曲子发槽位**、**每 10 帧决定该放哪首**、**把 Mod 曲与原版曲的音量交接好**。

| 时机 | 做什么 |
|---|---|
| 作者 `MusicLoader.GetMusicSlot(mod, path)`（加载期） | 定位文件、向 MediaPlayer 登记、发原版槽位之后的号，并提前 `prepareAsync` |
| `Main.UpdateAudio`（每帧，原版之后） | 重选曲目、按帧推进淡入淡出、Mod 曲听得见时把原版音乐压掉 |
| 同上（选曲时） | 读 `ModSceneEffect.IsSceneEffectActive/Music/Priority` 与 `ModNpc.Music/SceneEffectPriority` |
| `XNAUnityRunner.OnApplicationPause` | 进后台暂停 MediaPlayer，回前台恢复并把重选计时归零 |
| `MusicLoader.AddMusicBox / IsMusicPlaying / RestartMusic / StopMusic` | 音乐盒登记、在播查询、Boss 重召重置、Boss 死亡停曲 |

**不接**（第 2 节给了原因）：

* **原版优先级阶梯**（tModLoader 的 `VanillaMusic.Tier`）：本版不比较"Mod 曲 Priority 是否压得过原版这首"，
  只要场景/NPC 给出 `Music >= 0` 就按 Priority 竞争；
* 音乐盒**录制**（空音乐盒在饰品栏里录下正在放的曲子）、红石/电线翻转音乐盒、`MusicID.Sets.CanBeRecorded`；
* 主菜单音乐（`Main.gameMenu` 期间一律淡出，不用 Mod 曲）。

**与旧实现的差别**：旧 `ModMusic` 这个 `ILoaderModule` 已删除，选曲/淡出状态全部搬进 `MusicPipeline`；
`MusicLoader` 作为公开门面保留并移到 `Pipelines/Music/` 下（作者与样例按静态 API 编译）。
本版新增：饰品栏里的音乐盒道具也会出声（旧实现登记了 itemType，但没有接播放路径）。

## 2. 我们的设计

### 2.1 文件

```
Pipelines/Music/
  MusicPipeline.cs   模块：选曲、淡入淡出、原版压音、前后台暂停
  MusicTable.cs      槽位表与音乐盒表（密集 struct + ref），文件定位与解码登记
  MusicLoader.cs     公开门面：保留作者已经编译进去的静态 API
Core/AudioFiles.cs   路径解析（与 Sound 共用；只在注册期跑）
```

### 2.2 槽位表

```csharp
private struct MusicTrack
{
    public int Slot;      // = 原版槽数 + 下标
    public int MusicId;   // AndroidAudio 的 MediaPlayer 登记号；0 = 登记失败
    public float Fade;    // 0..1
    public float Sent;    // 上次下发的音量，避免每帧重复调 JNI
}
```

按"注册序下标"密集存放，`ref MusicTrack track = ref _table.TrackAt(i)`；文件名放在平行的 `string[]` 里，不塞进 struct（保持 struct 小、拷贝便宜）。
文件 → 下标的反查是 `Dictionary<string,int>`，只在注册期用。

槽位基数钉成本版 interop 的 105（`refs/dump.cs` 里 `MusicID.Count`）：interop 把游戏的 const 转成了非编译期常量，
运行时读一次再兜底反而会在版本变化时静默错位。`Install` 时和 `MusicID.Count` 比一次，不同就记一条警告。

### 2.3 选曲顺序

每 10 帧（`ChooseInterval`）重算一次，顺序固定：

1. **音乐盒**：屏幕内已开着的 Mod 音乐盒方块 → 它的槽位；没有就查饰品栏 3~19 号（跳过 10~12 的时装护甲）里的音乐盒道具；
2. **场景效果**：遍历 `ModContent.GetContent<ModSceneEffect>()`，取 `Priority` 最大且 `Music >= 0` 的；
3. **NPC**：只遍历 `npc.type >= Mod NPC 基数` 的实体（原版 NPC 直接跳过，不进内容表），屏幕外 5000 像素不考虑；
4. 没有命中就返回 -1（交回原版）。命中时必须是 Mod 槽位，否则同样返回 -1。

优先级相同的两个来源，先注册/先命中的赢（严格 `>` 比较，不交换）——保证结果与加载顺序一致、可复现。

### 2.4 淡入淡出、原版压音与暂停

* 曲目切换时对目标曲 `RestartMusic`（seek 到 0、`Fade = 0`），保证"新曲从头放"；
* 每帧：目标曲 `+FadeStep`，其余在"主曲已经听得见"时 `-FadeStep`；
* 音量 = `Fade × Main.musicVolume`，与上次下发值差小于 0.0005 才跳过——阈值必须远小于每帧增量，否则音量会卡在第一帧（"在播但听不见"）；
* Mod 曲听得见时把 `Main.musicFade[0]` 抬到 1 再令 `Main.newMusic = 0`：只设 `newMusic` 不会让正在播的原版曲淡出，会叠音；
* `Main.gamePaused` 进入时 `PauseMusicAll` 一次、退出时 `ResumeMusicAll`；`OnApplicationPause` 后台暂停、前台恢复并把 `_chooseIn` 归零。

## 3. 取舍理由

**4.1 为什么不保留 `ModMusic` 这个模块名？**
一个管线一个 `ILoaderModule`。选曲、淡出、暂停都是同一份状态（当前曲、上次曲、计时器），拆开会让两边都要读对方的表；
合并后 `MusicTable` 只被一个实例持有。

**4.2 为什么保留 `MusicLoader` 静态门面，这不是兼容层吗？**
它是**已发布的作者 API**：样例 `XiheBoss` 直接调 `GetMusicSlot` / `RestartMusic` / `StopMusic`，删掉等于让已编译的 Mod 在运行时 `MissingMethodException`。
门面里没有逻辑、没有状态，不是"旧实现的转接"。

**4.3 为什么不做原版优先级阶梯？**
那张表要把"NPC 类型 → 音乐组 → 档位"和 `SceneMetrics` 的十几个区域条件全部复刻，而且必须与原版每个版本同步；
做错的表现是"该让位时没让位"或"该接管时被压掉"，比"Mod 曲一律接管"更难排查。
本版把规则收敛成一条：**作者显式给了 `Music >= 0`，就按 Priority 竞争**；缺口写在第 1 节，后续单独做。

**4.4 为什么音乐盒扫描只在有音乐盒时才跑、每首曲子各开一个 MediaPlayer？**
`_table.BoxCount == 0` 时直接返回：没有登记音乐盒的 Mod（绝大多数）不为这个功能付每帧一次的全屏扫描；有音乐盒时也只在选曲帧扫，且先判 `tile.type >= Mod tile 基数`。
播放器按 `track id` 管音量/暂停/seek，共用一路就得自己记住"现在是谁"，切曲时反而多一次状态迁移；曲目以个位数计，多开一路可以忽略。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| 文件找不到 | `GetMusicSlot` 返回 0，按路径只报一次；调用方拿到 0 等于"不用 Mod 曲" |
| MediaPlayer 登记/准备失败 | 该曲 `MusicId = 0`，永远静音；选曲逻辑照常返回槽位，不崩 |
| `SetMusicVolume` 抛错 | `Safe.Once` 按槽位记一条，继续推进其它曲目 |
| 场景效果 / NPC 的 `Music` 或 `IsSceneEffectActive` 抛错 | `Safe.Report` 一条，跳过这个来源，继续看下一个 |
| `RestartMusic` / `StopMusic` 收到未注册槽位 | 静默忽略（Boss 死亡早于注册完成时会发生） |
| 淡出期间玩家退出世界/回主菜单 | `FadeTo(-1)`，所有曲目降到 0；表不清理（下次进世界还要用） |
| 音乐盒以非法参数登记 | 抛 `ArgumentOutOfRangeException`（作者编程错误，早失败）；重复登记同一格则后写覆盖先写 |

## 5. 扩展方式

* **加一个选曲来源**（例如 `ModBiome.Music`）：在 `Choose` 里算"候选槽位 + 优先级"参与同一套比较，`MusicTable` 不用改。
* **接原版优先级阶梯**：新增一个"原版当前档位"的纯函数，在 `Choose` 返回前比较一次 `priority >= tier`；这是本版唯一预留的插入点。
* **接音乐盒录制**：在 `Player.UpdateEquips` detour 里用 `IsMusicPlaying` 找当前曲，再按 `SlotForItem` 反查"该录成哪个物品"；表结构够用，只差一个钩子。
* **加槽位字段**（如音量偏好）：`MusicTrack` 加字段 + `Register` 抄值，读路径仍是 `ref`。

## 6. 与其它管线的边界

* **音效管线**：共用 `Core/AudioFiles.cs` 与 `Core/AndroidAudio.cs`；音乐不读 Sound 表，Sound 不参与选曲，两条管线各自挂自己的前后台钩子。
* **场景效果 / NPC 管线**：它们只提供 `Music` / `Priority` 两个属性，本管线只读不写，也不关心它们何时激活。
* **方块管线**：音乐盒是方块，但"这一格是音乐盒"由 `AddMusicBox` 登记；本管线只在 `Main.tile` 上读 `type/frameX/frameY`，不写方块、不改边框。
* **网络管线**：音乐不同步。每个客户端按自己的场景与音乐盒算出该放什么；槽位只在本进程内有意义。
* **安装管线**：音乐不占游戏内容 ID（槽位是音频侧编号），没有安装步骤；`Install()` 只挂钩子。
* **配置管线**：音乐音量仍归 `Main.musicVolume`（原版设置），本管线只乘它，不接管设置项。
