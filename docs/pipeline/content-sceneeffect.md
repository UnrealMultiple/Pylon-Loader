# 内容管线：场景效果（SceneEffect）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| `Player.Update`（本机玩家，orig 后） | 全表排序（`clamp(GetWeight,0,1) + Priority`）定出各槽赢家（水/表层背景/地下背景/音乐/火把/营火/拍图配色/地图背景）；`SpecialVisuals(player, active)` 对每个效果每帧都派 |
| `Main.UpdateAudio_DecideOnNewMusic`（orig 后） | 音乐决策：mod NPC 曲与场景音乐槽过 `BiomeLow`、原版档位两道门之后写 `Main.newMusic` |
| `Main.UpdateAudio_DecideOnTOWMusic`（orig 后） | 记"这一帧走另一个世界音乐"的标记（现状见 6.6） |
| `GUIMap.DrawMapFullscreenBackground` / `Main.DrawMapFullscreenBackground`（前置） | 地图背景槽赢家：`MapBackground` 贴图、`MapBackgroundColor`、`MapBackgroundFullbright` |
| 拍相机（`CaptureBiomePipeline`，由本管线的拍图门带起） | 拍相机的水/表层背景/配色改喂快照槽值 |

**不接**：水样式与表层/地下背景的"写回"钩子在各自管线（本管线只定赢家与槽值，见 6）；`Player.ManageSpecialBiomeVisuals`（屏幕滤镜桥）；每玩家快照；地下背景的过渡插值。群落（`ModBiome`）当普通场景效果问一句 `IsSceneEffectActive`，群落标记本身归生物群系管线。

## 2. 我们的设计

### 2.1 五个文件

`Pipelines/SceneEffect/` 下：`SceneEffectPipeline.cs`（排序 + 各槽赢家 + 音乐决策 + 地图背景绘制）、`SceneEffectTable.cs`（全部场景效果的有序表 + 名字查找）、`VanillaSceneTier.cs`（原版本帧档位的复刻）、`SceneEffectInstance.cs`（每帧快照的槽结构）、`CaptureBiomePipeline.cs`（拍相机消费，判据按快照槽值）。

钩子按门分装、由 `GateHub` 自动发现：排序与每帧回调看是否有效果覆写相关成员；音乐门再叠加"有没有 `ModNpc.Music` / `ModNpc.SceneEffectPriority` 覆写"；地图背景门看 `MapBackground`；拍图门看水/表层背景/拍图配色。

### 2.2 表不按 `Type` 索引

群落（第 22 条）已经占了 `ModType.Type` 做自己的编号，普通场景效果不需要编号——本管线要不要再发一套号没有意义。所以 `SceneEffectTable` 是一张 **`List<ModSceneEffect>`**（含群落），按注册顺序排列，每帧线性扫一遍。数量是"每个 mod 几个"，几十个量级，线性扫比建索引更省事。

### 2.3 排序与各槽赢家

```
foreach (effect in 表)
    active = IsSceneEffectActive(player)                  // 抛异常按未激活
    SpecialVisuals(player, active)                        // 激活与否都派
    if (!active) continue
    weight = clamp(GetWeight(player), 0, 1) + Priority    // 抛异常本轮不参与排序
    Ranked.Add((weight, 注册序, effect))
排序：权重大者优先，平手先注册的赢
foreach (ranked) Fill(scene, effect)                      // 各槽只认排序里第一个声明者
```

每个"槽"是快照 `SceneEffectInstance` 里的一对 `{Value, Priority}`：水、表层背景、地下背景、音乐、火把物品、营火物品（这两个由群落声明）、拍图配色、地图背景。声明为 `-1`/null 的槽跳过；快照整帧重建后写进 `SceneEffectPipeline.Scene`，`PlayerTml.CurrentSceneEffect` 返回它。音乐消费读快照槽；水/背景/地图背景消费时重读赢家的属性（赢家已按同一帧状态选出）。

### 2.4 音乐怎么落地

决策挂在 `Main.UpdateAudio_DecideOnNewMusic` 之后（安装时同时挂 `UpdateAudio_DecideOnTOWMusic` 记标记）：

```
本机玩家不在 / 音乐盒激活（SceneMetrics.ActiveMusicBox >= 0） → 不做，原版说了算
music = FromNpcs()      // 屏幕 ±5000 内的 mod NPC：Music + SceneEffectPriority，优先级高者赢，平手先扫到的赢
场景音乐槽的 Priority 更高 → 换成槽里的
music < 0 或 Priority < BiomeLow(1) → 不写
Priority < 原版这一帧的档位（VanillaSceneTier.Tier） → 不写
music 指向 mod 曲槽（>= MusicTable.VanillaCount） → 不写（mod 曲归音乐管线）
否则写 Main.newMusic = music
```

`Music` 的取值：`-1` = 不选（默认值，"没赢家"和"赢家不选音乐"是同一条路），`0` = 静音（群落默认就是 0）。只写 `newMusic`（"这一帧想换什么"），不碰 `curMusic`——淡出淡入由游戏自己的音频状态机做，我们只提供意愿。

mod NPC 音乐的 `SceneEffectPriority` 默认 6，与原版"常规首领"档同尺：默认档下撞上常规首领的原版曲能放，撞上更强档让路。

### 2.5 每帧回调

`SpecialVisuals(player, isActive)` 对每个效果每帧都调，包括没激活的（参数里会说 false）：作者的屏幕滤镜要靠这个"关掉"自己，只在激活时调就永远关不掉。只对本机玩家调，`Main.gameMenu` 时不调。

### 2.6 地图背景

`GUIMap.DrawMapFullscreenBackground` 与 `Main.DrawMapFullscreenBackground` 两处前置：地图背景槽非空时整屏画作者的贴图（不然走 orig，一次多余调用都没有）。颜色：表层（`screenPosition.Y <= worldSurface*16`）且没开 `MapBackgroundFullbright` 用天空色，其余（含开了全亮的）用白；随后 `MapBackgroundColor(ref color)` 让作者再改。贴图按路径缓存，失败也缓存——本帧走原版背景，资产就绪后下一帧自动接上。

## 3. 取舍理由

* **为什么每帧线性扫？** 效果数量少（几十），每帧一次几十次虚调用的开销远小于维护"标记变了才重算"那套失效通知的复杂度；而且 `GetWeight` 依赖玩家状态，缓存反而容易读到旧值。
* **为什么是"排序 + 各槽认领"而不是单赢家？** 排序键 `clamp(GetWeight,0,1)+Priority` 里，`Priority` 决定"够不够格说话"，`Weight` 只在同级里分先后。一个效果可以同时声明水、背景、音乐，槽与槽互不抢占；某槽没人声明就整槽空着，原版照旧——不逼作者为了要背景而顺手接管音乐。
* **为什么各槽只认"排序里第一个声明者"？** 排序已经把"谁更有资格"排好了，第一个声明者就是该槽的赢家；后声明的同槽只在赢家没声明时补位。结果是确定的，不随求值顺序抖动。
* **为什么只写 `newMusic`？** 直接改 `curMusic` 会把游戏音频状态机的淡入淡出、`Main.musicFade` 那套全打乱，出现"音乐切换爆音"。只表达意愿，让游戏执行。
* **为什么 mod 曲槽不写 `newMusic`？** mod 曲的编号空间（`MusicTable.VanillaCount` 起）游戏不认识，写进去只会放错原版曲。mod 曲由音乐管线经 `MediaPlayer` 播放并在播出后清 `newMusic`，两个管线不在同一帧互相覆盖。
* **为什么原版档位要照抄状态机而不是读一个值？** 原版音乐选择写死在 `UpdateAudio_DecideOnNewMusic` 内部，没有"本帧原版优先级"这个可读字段；档位只能按同样的条件推。见 6.6：作者 `Priority` 与原版档位同尺，音乐、水、背景共用同一套档。
* **为什么档位门在原生消费钩子里算？** 水/背景的落点在 `Main.CalculateWaterStyle`、`Main.GetPreferredBGStyleForPlayer`、`Main.DrawBackground_PickUndergroundBackgroundStyle`——这些是"游戏选完之后"的最后一个位置，压不过就不写、原版值留着；赢家与槽值在 `Player.Update` 后置已经选好，门只多读一个 `Priority`。
* **为什么 `SpecialVisuals` 对未激活的也调？** 见 2.5：它是"开关"语义，不是"激活时通知"语义。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| `IsSceneEffectActive` 抛异常 | 该效果这一帧当未激活，记一次 |
| `GetWeight` 抛异常 | 该效果不参与本轮排序，记一次 |
| `SpecialVisuals` 抛异常 | 记一次，继续派下一个 |
| 槽读取（`Fill`）抛异常 | 记一次，继续下一个效果 |
| `ModNpc.Music` 抛异常 | 记一次，跳过该 NPC |
| 音乐盒激活 | 整段让路，原版决定 |
| 没有赢家、NPC 沿路也没曲 | 不写 `newMusic`，游戏照常决定 |
| `Priority` < `BiomeLow` | 不写 `newMusic`（水侧同门在水管线） |
| `Priority` < 原版本帧档位 | 不写 `newMusic` |
| 曲号指向 mod 曲槽 | 不写 `newMusic`，归音乐管线 |
| `VanillaSceneTier.Tier` 抛异常 | 记一次，本帧按 `BossHigh(8)` 处理后继续决策 |
| 音乐决策外层异常 | 记一次，本帧沿用原版决定 |
| 地图背景加载/绘制异常 | 记一次，本帧走原版地图背景 |
| `WaterStyle` / 背景样式消费异常 | 记一次，回落原版值（各自管线记） |
| `Main.gameMenu` / 玩家未激活 | 整段跳过 |

## 5. 扩展方式

加槽：`SceneEffectInstance` 加一对 `{Value, Priority}` 槽 + `Fill` 里认领 + 消费钩子读槽，三处齐了就能用。全局钩子（`GlobalSceneEffect` 一类，跨效果改选择结果）本版没有；"消费后再改一次"的口子目前只有背景管线的全局风格有（见背景管线）。接屏幕滤镜（`ManageSpecialBiomeVisuals`）要等特效管理面把 `SkyManager` 那座桥补上。

## 6. 与其它管线的边界

* **生物群系（22）**：群落标记与进/出回调归它；本管线把群落当普通效果问一句 `IsSceneEffectActive`，火把/营火物品的槽由群落声明、在这里排序。
* **音乐管线**：`Main.newMusic` 的编号空间归它；本管线只写"选中的那一首"的编号，不验证它是否存在。mod 曲槽不写（见 2.4）。
* **水（29）**：赢家与槽值在这里定；`WaterPipeline.ChooseWaterStyle` 读水槽赢家，过档位门与喷泉/血月夜/原版背景三门后写回。贴图表与编号归它。
* **背景（30）**：表层/地下同水——槽赢家在这里定，`BackgroundPipeline` 在原版选背景的入口过档位门；全局风格的后改口子归它。
* **拍图**：拍相机的水/表层背景/配色改喂快照槽值（`CaptureBiomePipeline`），判据按"槽值是否超出原版表长"。
* **安装管线**：`SetStaticDefaults` 在 `Static` 阶段跑；`Refresh` 时重建表并清各槽赢家。

### 6.6 与原版场景效果的优先级比较（`VanillaSceneTier`）

原版"这一帧轮到谁说话"是一整段状态机：制作者名单 > 火把神 > NPC 群（入侵/首领按屏幕判定，与原版音乐选择的视野一致）> 事件与环境 > 群落。数值全部在原版音乐决策代码里，已照抄成 `Pipelines/SceneEffect/VanillaSceneTier.cs`（每项 API 都对过 `refs/dump.cs`：`SceneMetrics` 的区旗、`CreditsRollEvent.IsEventOngoing`、`NPCID.Sets.BelongsToInvasionOldOnesArmy`、`Main.ShouldPlayRainbowBoulderMusic` 等）。

**档位是什么数**：本构建没有 `SceneEffectPriority` 枚举（tML 侧类型，dump 零命中），档位直接用 1..9 的整数——对作者来说 `Priority` 本来就是 int，语义在文档里：

| 档 | 含义 |
|---|---|
| 9 | 制作者名单（`CreditsRollEvent` 进行中） |
| 8 | 火把神小游戏；NPC 组 7（月主） |
| 7 | NPC 组 17/9/10/6（特殊首领那几组） |
| 6 | 其余全部 NPC 组（常规首领、史莱姆雨等） |
| 5 | 南瓜夜/雪夜（表层）、彩虹巨石；NPC 组 8/11/12（哥布林/海盗/旧日部队入侵） |
| 4 | 环境事件：日食、史莱姆雨、微光、城镇白天/夜晚、沙尘暴、地狱、太空（含醉世界血月的两条） |
| 3 | 神庙墙、地牢、蘑菇地、腐化、猩红 |
| 2 | 陨石、墓地、丛林、雪、（地下）沙漠 |
| 1 | 兜底（次级群落） |
| 0 | NPC 组 0（NPC 这一段不抬档） |

求值自上而下、第一个命中即返回（`VanillaSceneTier.Tier`）。NPC 组号 → 档位的映射：组 7 → 8；组 17/9/10/6 → 7；组 8/11/12 → 5；其余非零组 → 6；组 0 → 0。另一个世界路径下组 ≥ 18 的组不抬档（跳过 NPC 这一段，落到环境/群落那几档）。

**音乐门**：作者/场景槽的 `Priority` 先过 `BiomeLow` 门（< 1 不放），再与 `VanillaSceneTier.Tier` 比（< 档位不放），过了才写 `newMusic`。每一帧都与原版本帧的档位同尺——没有"原版有曲才比"的前提，也没有"≥100 无条件压过"的旧条件。

**视觉门**（水与背景同尺，不分"另一个世界"，固定按普通世界算档）：

* 水：`Priority >= BiomeLow`（1），再过水管线自己的三门（喷泉激活 / 血月夜 / 原版背景名单 1..8、13、14）；
* 表层背景：`>= BiomeHigh` 恒赢；`>= BiomeMedium` 赢森林（0/10/11/12）与原版 3、7；`>= BiomeLow` 只赢森林；
* 地下背景：`>= BiomeHigh` 恒赢；`>= BiomeMedium` 赢洞穴与原版 1、11；`>= BiomeLow` 只赢洞穴。

压不过就不写，原版这一帧选好的值留着。

**另一个世界的标记**：原版有两条互斥的音乐决策路径（普通 / 另一个世界），每帧只跑其中一条。决策目前只挂在普通路径的后置；`UpdateAudio_DecideOnTOWMusic` 后置置标记、决策入口先复位再用——档位计算里的 otherworld 分支（NPC 组的另一个世界映射、组 ≥ 18 跳过、血月/城镇那几条）要等 TOW 路径接入才生效，在那之前档位实际按普通世界取值，醉世界/换乐生效的帧原版曲照放。视觉侧固定按普通世界算档。

**为什么照抄而不是自己发明一套**：作者要的是"我的雪原效果压得过原版雪原音乐、压不过南瓜夜"这种与原版手感一致的排序；发明第二套数值得双倍解释。代价是游戏更新要跟一次——照抄的表集中在 `VanillaSceneTier` 一个文件里，好跟。

### 这一版没接

1. **地下背景的过渡动画**：`PickUndergroundBackgroundStyle` 与贴图替换都已接（见背景管线），但过渡插值仍由游戏算。
2. **另一个世界（TOW）的决策路径**：音乐决策只挂在普通路径后置（见 6.6），醉世界/换乐生效的帧本管线不裁决。
3. **每玩家快照**：快照只对本机玩家重建（`whoAmI == Main.myPlayer`），`CurrentSceneEffect` 固定返回本机快照；多人里读"他人"快照拿到的就是本机这份。
4. **`GlobalSceneEffect` 一类的全局钩子**：本版没有。
5. **`Player.ManageSpecialBiomeVisuals`**：本构建 interop 没有 `SkyManager` / 滤镜 / 叠加层的条目接口，屏幕滤镜桥归特效管理面。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 语义明确 | `SpecialVisuals` 每帧都调，未激活时 `isActive = false` |
| 语义明确 | `Music = -1` 表示"不选"，`0` 才是静音；群落默认 0 = 静音 |
| 语义明确 | `GetWeight` 在同级 `Priority` 里分先后，越界值钳到 0..1；平手先注册的赢 |
| 生效 | `Music`（原版曲号；mod NPC 曲默认档 6）过 `BiomeLow` 与原版档位两道门后写 `Main.newMusic` |
| 生效 | `WaterStyle` / `SurfaceBackgroundStyle` / `UndergroundBackgroundStyle` 在槽赢家且档位门允许时生效（门见 6.6） |
| 生效 | `TileColorStyle` / `MapBackground` / `MapBackgroundColor` / `MapBackgroundFullbright` |
| 生效 | `PlayerTml.CurrentSceneEffect`（本机快照，槽对 `{Value, Priority}`） |
| 本版不生效 | 另一个世界路径的裁决、每玩家快照、`ManageSpecialBiomeVisuals`、地下背景过渡插值 |
