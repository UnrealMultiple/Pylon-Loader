# 内容管线：音乐（Music）

## 1. 职责

音乐管线管三件事：**给 Mod 曲子发槽位**、**每 10 帧决定该放哪首**、**把 Mod 曲与原版曲的音量交接好**。
音乐盒方块的"点击开合与电线翻转"由方块管线的交互钩子执行（见 2.5），本管线只消费它翻出来的结果。

| 时机 | 做什么 |
|---|---|
| 作者 `MusicLoader.GetMusicSlot(mod, path)`（加载期） | 按 2.3 的约定定位文件、向 MediaPlayer 登记、发原版槽位之后的号，并提前 `prepareAsync` |
| 作者 `MusicLoader.AddMusicBox`（加载期） | 把 Mod 曲绑到音乐盒方块（按 frameY）或音乐盒道具，并触发方块管线开合/电线钩子的安装 |
| 玩家点击 Mod 音乐盒方块 / 电线通电（方块管线） | 翻转 2×2 `frameX` 开/合（2.5）；选曲扫描只认 `frameX >= 36` 的"开着" |
| `Main.UpdateAudio`（每帧，原版之后） | 重选曲目、按帧推进淡入淡出、Mod 曲听得见时把原版音乐压掉 |
| 同上（选曲时） | 读 `ModSceneEffect.IsSceneEffectActive/Music/Priority` 与 `ModNpc.Music/SceneEffectPriority` |
| `XNAUnityRunner.OnApplicationPause` | 进后台暂停 MediaPlayer，回前台恢复并把重选计时归零 |
| `MusicLoader.AddMusicBox / IsMusicPlaying / RestartMusic / StopMusic` | 音乐盒登记、在播查询、Boss 重召重置、Boss 死亡停曲 |

**不接**（第 3 节给了原因）：

* **Mod 槽位不受原版档位门**：原版优先级阶梯已由场景效果管线的音乐决策实现
  （`VanillaSceneTier.Tier` + `SceneEffectPipeline.Decide`，见 `content-sceneeffect.md`），
  但只作用于写 `Main.newMusic` 的**原版槽位**；Mod 槽位只要来源给出 `Music >= 0` 就按 Priority 竞争，不过档位门。
  原版音乐盒让路不在此列——它在 Mod 槽位的选曲里同样生效（2.4）；
* 音乐盒**录制**（空音乐盒在饰品栏里录下正在放的曲子）、`MusicID.Sets.CanBeRecorded`；
* 主菜单音乐（`Main.gameMenu` 期间一律淡出，不用 Mod 曲）。

**与旧实现的差别**：旧 `ModMusic` 这个 `ILoaderModule` 已删除，选曲/淡出状态全部搬进 `MusicPipeline`；
`MusicLoader` 作为公开门面保留并移到 `Pipelines/Music/` 下（作者与样例按静态 API 编译）。
饰品栏里的音乐盒道具会出声；方块路径由方块管线的开合钩子驱动（2.5），不再是只登记不生效的数据。

## 2. 我们的设计

### 2.1 文件

```
Pipelines/Music/
  MusicPipeline.cs   模块：选曲、淡入淡出、原版压音、前后台暂停
  MusicTable.cs      槽位表与音乐盒表（密集 struct + ref），登记与去重
  MusicLoader.cs     公开门面：保留作者已经编译进去的静态 API
Audio/AudioFiles.cs  文件定位（与 Sound 共用；只在注册期跑）
Audio/AndroidAudio.cs  MediaPlayer 的 JNI 封装：登记、音量、暂停、seek
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

### 2.3 文件定位

`AudioFiles` 是音效与音乐共用的定位器，约定只有一条：**路径相对 Mod 根**。规则：

* **归一**：反斜杠算 `/`，剥掉开头的 `Mod名/` 前缀——写了 `MyMod/Music/X` 与写 `Music/X` 等价；
* **扩展名**：带 `.ogg/.wav/.mp3` 之一（大小写不敏感）的路径**按原文件名探测、不追加**；不带的按 `.ogg → .wav → .mp3` 逐个试；
* **位置两级**：先查 Mod 根的 `Assets/` 目录（`Assets/` 前缀可省），全部落空再按**原路径**回退 Mod 根。`Assets/` 内部还有两道大小写兜底（`assets/` 前缀、目录名全小写）——Android 文件系统大小写敏感，打包出来的目录常是小写；两级都命中时优先 `Assets/`，理由见 3.7；
* **单参 `GetMusicSlot("Mod名/路径")`**：首段是已装 Mod 名就精确归属到那个 Mod；首段不是 Mod 名（裸路径）才按加载序逐 Mod 回退。音效注册走的就是裸路径（如 `Sounds/Laser`），这条全局回退是音效侧的既有约定，音乐单参沿用；首段精确归属是为了杜绝同名文件静默落到别的 Mod；
* **绝对路径**原样 `File.Exists`（本机调试用）。

### 2.4 选曲顺序

每 10 帧（`ChooseInterval`）重算一次，判定顺序固定：

1. **Mod 音乐盒**：屏幕内已开着的 Mod 音乐盒方块（`frameX >= 36`，`frameY` 归一后查表）→ 它的槽位；
   没有就查饰品栏 3~19 号（跳过 10~12 的时装护甲）里的音乐盒道具；
2. **原版音乐盒让路**：附近有开着的原版音乐盒（`Main.SceneMetrics.ActiveMusicBox >= 0`）→ 返回 -1，
   场景/NPC 曲整体让位给原版盒曲——只有 Mod 盒能压住原版盒（第 1 条在它前面）；
3. **场景效果**：遍历 `ModContent.GetContent<ModSceneEffect>()`，取 `Priority` 最大且 `Music >= 0` 的；
4. **NPC**：只遍历 `npc.type >= Mod NPC 基数` 的实体（原版 NPC 直接跳过，不进内容表），屏幕外 5000 像素不考虑；
5. 没有命中就返回 -1（交回原版）。命中时必须是 Mod 槽位，否则同样返回 -1。

优先级相同的两个来源，先注册/先命中的赢（严格 `>` 比较，不交换）——保证结果与加载顺序一致、可复现。

### 2.5 音乐盒方块的开合（钩子在方块管线）

点击与电线钩子长在方块管线：`TilePipeline.Interact` 挂 `Player.TileInteractionsUse`、`TilePipeline.Wire` 挂 `Wiring.HitWireSingle`，
判定"这格是不是音乐盒"读本管线的登记表（`AddMusicBox` 登记过的 tileType）：

* **点击**：一次新的按下（非长按重复）落在音乐盒上、且作者**没有**覆写 `ModTile.RightClick` 时，
  原版交互跑完之后把点击处所在的 2×2 整块翻转 `frameX`（`< 36` 加 36、`≥ 36` 减 36；`frameX/frameY` 按 18 定位左上角），
  并置 `tileInteractionHappened`（同一按下不再触发手中物品）。作者覆写了 `RightClick` 就只走作者逻辑，开合钩子不翻；
* **电线**：原版电线处理完之后，作者**没有**覆写 `ModTile.HitWire` 时同样翻转，并对每个翻转的格子 `Wiring.SkipWire`——
  不写跳过记录，这次的改帧会被原版电线再次拾起；作者覆写了 `HitWire` 就完全归作者；
* **多人同步**：`Main.netMode != 0` 时 `SendTileSquare(-1, left, top, 2, 2)` 把这 2×2 的方块状态发给对端；
  两端各自按翻出来的 `frameX` 独立选曲，音乐本身不同步（第 6 节）；
* **安装**：`AddMusicBox` 登记成功即触发方块管线这两个钩子的安装（`GateHub` 门）；没有音乐盒登记的世界不装，零开销。

"开着"的判定是 `frameX >= 36`（2.4 第 1 条只认它）：翻开 36/54，关上回到 0/18，`frameY` 始终不变、仍按 36 的倍数对应曲子。

### 2.6 淡入淡出、原版压音与暂停

* 曲目切换时对目标曲 `RestartMusic`（seek 到 0、`Fade = 0`），保证"新曲从头放"；
* 每帧：目标曲 `+FadeStep`，其余在"主曲已经听得见"时 `-FadeStep`；
* 音量 = `Fade × Main.musicVolume`，与上次下发值差小于 0.0005 才跳过——阈值必须远小于每帧增量，否则音量会卡在第一帧（"在播但听不见"）；
* Mod 曲听得见时把 `Main.musicFade[0]` 抬到 1 再令 `Main.newMusic = 0`：只设 `newMusic` 不会让正在播的原版曲淡出，会叠音；
* `Main.gamePaused` 进入时 `PauseMusicAll` 一次、退出时 `ResumeMusicAll`；`OnApplicationPause` 后台暂停、前台恢复并把 `_chooseIn` 归零。

## 3. 取舍理由

**3.1 为什么不保留 `ModMusic` 这个模块名？**
一个管线一个 `ILoaderModule`。选曲、淡出、暂停都是同一份状态（当前曲、上次曲、计时器），拆开会让两边都要读对方的表；
合并后 `MusicTable` 只被一个实例持有。

**3.2 为什么保留 `MusicLoader` 静态门面，这不是兼容层吗？**
它是**已发布的作者 API**：样例 `XiheBoss` 直接调 `GetMusicSlot` / `RestartMusic` / `StopMusic`，删掉等于让已编译的 Mod 在运行时 `MissingMethodException`。
门面里没有逻辑、没有状态，不是"旧实现的转接"。

**3.3 为什么 Mod 槽位不过原版档位门？**
原版优先级阶梯已经在场景效果管线的音乐决策里复刻（`VanillaSceneTier` + `Decide`，详见 `content-sceneeffect.md` 2.4 与 6.6），
但它只作用于写 `Main.newMusic` 的原版槽位。Mod 槽位如果也过这道门，作者显式给的 `Music >= 0` 会被原版档位压掉，
表现是"写了音乐却不响"——比"该让位时没让位"更难排查。所以 Mod 槽位的让位收敛成一条：**原版音乐盒开着就让**（2.4 第 2 条），
这是原版语义里"音乐盒最大"的部分，不接会压掉正被播放的原版盒曲；档位门要收紧时在 `Choose` 返回前比较一次
`VanillaSceneTier.Tier` 即可（第 5 节预留的插入点）。

**3.4 为什么音乐盒扫描只在有音乐盒时才跑、每首曲子各开一个 MediaPlayer？**
`_table.BoxCount == 0` 时直接返回：没有登记音乐盒的 Mod（绝大多数）不为这个功能付每帧一次的全屏扫描；有音乐盒时也只在选曲帧扫，且先判 `tile.type >= Mod tile 基数`。
播放器按 `track id` 管音量/暂停/seek，共用一路就得自己记住"现在是谁"，切曲时反而多一次状态迁移；曲目以个位数计，多开一路可以忽略。

**3.5 为什么开合钩子长在方块管线、作者覆写了就完全不翻？**
翻转操作的对象全是方块管线的领地：`Main.tile` 的 frameX、电线跳过记录、多人 tile square。音乐管线只消费"开着没"这个结果。
作者覆写了 `RightClick` / `HitWire`，说明他在这两个入口有自己的行为，两套都跑会双倍翻转或互相打架——覆写即接管，
与方块其它交互钩子（锁、坐卧、锤坡）的规则一致。

**3.6 为什么原版音乐盒开着时 Mod 曲要整体让位？**
原版语义里音乐盒是最高优先级：玩家手动摆一只盒子在旁边并打开它，就是明确指定"现在听这首"。
Mod 场景/NPC 曲此时插进来并把它压掉，玩家既没有"关掉 Mod 曲"的入口，也没有"音量分级"可调，体验是"Mod 曲赖着不走"。
Mod 音乐盒保持在前（2.4 第 1 条）：玩家摆的是 Mod 盒，听的就是 Mod 曲。

**3.7 为什么 `Assets/` 找不到才回退 Mod 根，而不是直接按 Mod 根解析？**
`Assets/` 目录是既有 Mod 与内置样例在用的布局（XiheBoss、PipelineTest 的资源都在 `assets/` 下），把它放第一级保证已有调用解析出来的文件一个不变；
Mod 根直查是后加的第二级，只对以前找不到的路径生效。两级结构也让两类布局的 Mod 共存，不必迁移资源目录；
两级都命中时取 `Assets/` 那份，结果不随资源摆放漂移。

**3.8 为什么单参 `GetMusicSlot` 不强制要求 `Mod名/路径`？**
音效与音乐共用 `AudioFiles`，音效的既有写法就是裸路径（`new SoundStyle("Sounds/Laser")`），强制前缀等于让所有已装音效一次失声。
折中：路径首段是 Mod 名时精确归属（杜绝跨 Mod 误命中），裸路径才全局回退。作者明确指本 Mod 的资源时，用双参 `GetMusicSlot(mod, path)` 最稳。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| 文件找不到 | `GetMusicSlot` 返回 0，按路径只报一次；调用方拿到 0 等于"不用 Mod 曲"；`AddMusic` 直接抛 `FileNotFoundException`（作者在加载期就该知道路径错了） |
| MediaPlayer 登记/准备失败 | 该曲 `MusicId = 0`，永远静音；选曲逻辑照常返回槽位，不崩 |
| `SetMusicVolume` 抛错 | `Safe.Once` 按槽位记一条，继续推进其它曲目 |
| 场景效果 / NPC 的 `Music` 或 `IsSceneEffectActive` 抛错 | `Safe.Report` 一条，跳过这个来源，继续看下一个 |
| `RestartMusic` / `StopMusic` 收到未注册槽位 | 静默忽略（Boss 死亡早于注册完成时会发生） |
| 淡出期间玩家退出世界/回主菜单 | `FadeTo(-1)`，所有曲目降到 0；表不清理（下次进世界还要用） |
| 音乐盒以非法参数登记 | 抛 `ArgumentOutOfRangeException`（作者编程错误，早失败）；重复登记同一格则后写覆盖先写 |
| 开合翻转时 2×2 内某格不活跃或类型不符 | 该格跳过不翻；多人仍按整块发 tile square |

## 5. 扩展方式

* **加一个选曲来源**（例如 `ModBiome.Music`）：在 `Choose` 里算"候选槽位 + 优先级"参与同一套比较，`MusicTable` 不用改。
* **给 Mod 槽位也过原版档位门**：在 `Choose` 返回前比较一次 `VanillaSceneTier.Tier`；插入点已预留。
* **接音乐盒录制**：在 `Player.UpdateEquips` detour 里用 `IsMusicPlaying` 找当前曲，再按 `SlotForItem` 反查"该录成哪个物品"；表结构够用，只差一个钩子。
* **非 2×2 或自定义开合的音乐盒**：作者覆写 `ModTile.RightClick` / `ModTile.HitWire` 自己处理（开合钩子自动让位，见 3.5）；登记表不用动。
* **加槽位字段**（如音量偏好）：`MusicTrack` 加字段 + `Register` 抄值，读路径仍是 `ref`。
* **动文件定位规则时**：`AudioFiles` 音效与音乐共用，改探测顺序或加新布局，先过两侧样例（XiheBoss 的音乐与音效、PipelineTest 的小写 `assets/`），两条管线一起回归。

## 6. 与其它管线的边界

* **音效管线**：共用 `Audio/AudioFiles.cs` 与 `Audio/AndroidAudio.cs`，定位约定同一条（见 2.3）；音乐不读 Sound 表，Sound 不参与选曲，两条管线各自挂自己的前后台钩子。
* **场景效果 / NPC 管线**：原版槽位的音乐决策（档位门、原版盒让路）在场景管线，见 `content-sceneeffect.md`；本管线只管 Mod 槽位，读它们的 `Music` / `Priority`，不写；原版盒让路两条路径都有（场景侧 `Decide`、本管线 2.4）。
* **方块管线**：音乐盒是方块，"这一格是音乐盒"由 `AddMusicBox` 登记；开合（点击/电线/frameX 翻转/tile square 同步）由方块管线的交互钩子执行，本管线只在 `Main.tile` 上读 `type/frameX/frameY`，从不写方块。
* **网络管线**：音乐不同步。每个客户端按自己的场景与音乐盒算出该放什么；槽位只在本进程内有意义。音乐盒方块的开合状态由方块管线经 tile square 同步，两端各自选曲、结果一致。
* **安装管线**：音乐不占游戏内容 ID（槽位是音频侧编号），没有安装步骤；`Install()` 只挂钩子。
* **配置管线**：音乐音量仍归 `Main.musicVolume`（原版设置），本管线只乘它，不接管设置项。
