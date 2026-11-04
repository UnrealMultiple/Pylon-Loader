# 内容管线：场景效果（SceneEffect）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| `Main.UpdateAudio_DecideOnNewMusic` | 选赢家（`Priority` + `GetWeight`），与原版这一帧的档位（`VanillaSceneTier`）比过之后再决定 `Main.newMusic` |
| `Main.UpdateAudio_DecideOnTOWMusic` | 记"这一帧走另一个世界音乐"的标记（给上面的档位计算用） |
| `Player.Update`（本机玩家） | `ModSceneEffect.SpecialVisuals`（每次都调，带是否激活） |
| `ModBiome.IsSceneEffectActive` | 群落走第 22 条管线的标记，本管线只问不判 |

**不接**：地下背景风格的选择、`MapBackgroundFullbright`。表层背景风格、水样式、地图全屏背景与 `MapBackgroundColor` 已接，并且水/背景也走与原版档位比较（见 6.6 末）。

## 2. 我们的设计

### 2.1 两个文件

`Pipelines/SceneEffect/` 下：`SceneEffectPipeline.cs`（选赢家 + 音乐 + 每帧回调）、`SceneEffectTable.cs`（全部场景效果的有序表 + 名字查找）。

### 2.2 表不按 `Type` 索引

群落（第 22 条）已经占了 `ModType.Type` 做自己的编号，普通场景效果不需要编号——本管线要不要再发一套号没有意义。所以 `SceneEffectTable` 是一张 **`List<ModSceneEffect>`**（含群落），按注册顺序排列，每帧线性扫一遍选赢家。数量是"每个 mod 几个"，几十个量级，线性扫比建索引更省事。

### 2.3 选赢家

```
best = null
foreach (effect in 表)
    if (!effect.IsSceneEffectActive(player)) continue
    if (best == null || effect.Priority > best.Priority) best = effect
    else if (effect.Priority == best.Priority && effect.GetWeight(player) > best.GetWeight(player)) best = effect
```

平手时**先注册的赢**（严格大于才替换），这样结果是确定的、不随每帧求值顺序抖动。

### 2.4 音乐怎么落地

`Main.UpdateAudio_DecideOnNewMusic` 跑完后，如果赢家的 `Music >= 0`，就把它写进 `Main.newMusic`：

* `-1` = 不选，交回游戏（默认值就是它，所以"没赢家"和"赢家不选音乐"是同一条路）；
* `0` = 静音（群落默认就是 0）。

只写 `newMusic`（"这一帧想换什么"），不碰 `curMusic`（"正在放什么"）——淡出淡入由游戏自己的音频状态机做，我们只提供意愿。

### 2.5 每帧回调

`SpecialVisuals(player, isActive)` 对每个效果每帧都调，包括没激活的（参数里会说 false）：作者的屏幕滤镜要靠这个"关掉"自己，只在激活时调就永远关不掉。

只对本机玩家调（`whoAmI == Main.myPlayer`），`Main.gameMenu` 时不调。

## 3. 取舍理由

* **为什么每帧线性扫？** 效果数量少（几十），每帧一次几十次虚调用的开销远小于维护"标记变了才重算"那套失效通知的复杂度；而且 `GetWeight` 依赖玩家状态，缓存反而容易读到旧值。
* **为什么只写 `newMusic`？** 直接改 `curMusic` 会把游戏音频状态机的淡入淡出、`Main.musicFade` 那套全打乱，出现"音乐切换爆音"。只表达意愿，让游戏执行。
* **为什么原版档位要照抄状态机而不是读一个值？** 原版音乐选择写死在 `UpdateAudio_DecideOnNewMusic` 内部，没有"本帧原版优先级"这个可读字段；档位只能按同样的条件推。见 6.6：作者 `Priority` 与原版档位同尺，音乐、水、背景三条共用同一套档。
* **为什么水/背景的档位在 `Player.Update` 后置里算？** 那是本管线唯一"每帧一定跑到、且在游戏选完水/背景之后"的位置；`SceneMetrics` 这一帧的数也已经刷新。算档会扫一遍 NPC（Boss 群判定），所以只在"赢家确实声明了水/背景"时才算，纯音乐效果零额外开销。
* **为什么 `SpecialVisuals` 对未激活的也调？** 见 2.5：它是"开关"语义，不是"激活时通知"语义。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| `IsSceneEffectActive` 抛异常 | 这一次当未激活，记一次 |
| `GetWeight` 抛异常 | 用 0 参与比较，记一次 |
| `SpecialVisuals` 抛异常 | 记一次，继续调下一个 |
| 赢家 `Music` = -1 | 不写 `newMusic`，游戏照常决定 |
| 没有赢家 | 不写 `newMusic` |
| `Main.gameMenu` / 玩家未激活 | 整段跳过 |

## 5. 扩展方式

接背景：在 `Pipelines/SceneEffect/` 里加一个"赢家 → 背景风格"的映射，挂 `Main.DrawBackground_PickUndergroundBackgroundStyle` 与表层背景入口；赢家的 `SurfaceBackgroundStyle` / `UndergroundBackgroundStyle` 属性已经在基类里，接上就能用。

## 6. 与其它管线的边界

* **生物群系（22）**：群落标记与进/出回调归它；本管线只把群落当普通效果问一句 `IsSceneEffectActive`。
* **音乐管线**：`Main.newMusic` 的编号空间归音乐管线；本管线只写"选中的那一首"的编号，不验证它是否存在。
* **水（29）**：本管线算出赢家之后把 `WaterStyle` 交给水管线（`WaterPipeline.Apply`），水的贴图表与编号归它。
* **背景（30）**：同水，赢家确定后把 `SurfaceBackgroundStyle` 交给背景管线（`BackgroundPipeline.Apply`）。
* **安装管线**：`SetStaticDefaults` 在 `Static` 阶段跑。

### 6.6 与原版场景效果的优先级比较（新 `VanillaSceneTier`）

原版的"这一帧轮到谁说话"是一整段状态机：制作者名单 > 火把神 > NPC 群（入侵/首领按屏幕判定）> 事件（血月/南瓜/雪/史莱姆雨/闪光/城镇/沙尘暴/地狱）> 环境群落 > 神庙/地牢/蘑菇/腐化/猩红 > 次级群落。数值全部在原版音乐决策代码里，已照抄成 `Pipelines/SceneEffect/VanillaSceneTier.cs`（每项 API 都对过 `refs/dump.cs`：`SceneMetrics` 的区旗、`CreditsRollEvent.IsEventOngoing`、`NPCID.Sets.BelongsToInvasionOldOnesArmy`、`Main.ShouldPlayRainbowBoulderMusic` 等都在）。

接入点：`DecideMusic` 里"原版有话说（`newMusic` 非 0）"时，作者的 `Priority`：

* ≥ 100（`VanillaMusicPriority`）→ 无条件压过（作者显式要求）；
* 否则 ≥ `VanillaSceneTier.Tier(otherworld)` → 压过；
* 都不到 → 不动原版的决定。

**水与背景同尺**（本轮接上）：`Player.Update` 后置里选出的赢家，只有在声明了 `WaterStyle` / `SurfaceBackgroundStyle` 且 `Priority ≥ VanillaSceneTier.Tier(false)`（或 ≥ 100）时才会写 `Main.waterStyle` / `Main.bgStyle`；压不过就不写，原版这一帧选好的值留着。视觉不分"另一个世界"那条路，固定按普通世界算档。

`otherworld` 靠同时挂 `UpdateAudio_DecideOnTOWMusic`（原版两条决策路径各发一次，挂上记标记）。

**档位是什么数**：本构建没有 `SceneEffectPriority` 枚举（tML 侧类型，dump 零命中），档位直接用 1..9 的整数——对作者来说 `Priority` 本来就是 int，语义在文档里：1=次级群落 2=神庙/地牢/腐化/猩红/雪/丛林/陨石/墓地 3=环境 4=事件/天气/城镇 5=普通 Boss 6=强力 Boss 7=火把神 8=名单 100=无条件。

**为什么照抄而不是自己发明一套**：作者要的是"我的雪原效果压得过原版雪原音乐、压不过血月"这种与原版手感一致的排序；发明第二套数值得双倍解释。代价是游戏更新要跟一次——照抄的表集中在 `VanillaSceneTier` 一个文件里，好跟。

### 这一版没接

1. **地下背景的过渡动画**：`PickUndergroundBackgroundStyle` 与贴图替换都已接（见背景管线），但过渡插值仍由游戏算。
2. ~~**水样式**~~：**已接**——赢家的 `WaterStyle` 在档位允许时写进 `Main.waterStyle`（贴图表与编号归水管线）；档位压不过原版就不写。
3. **`MapBackgroundFullbright`**：地图背景已能画（`GUIMap.DrawMapFullscreenBackground`），但"全亮"这个开关没接，颜色走 `MapBackgroundColor`。
4. ~~**与原版场景效果比较优先级**~~：**已接完整档位表**——新 `VanillaSceneTier`（见 6.6）。"拿不到原版数值"的前提不成立：数值在原版音乐决策的状态机里，可以照抄。
5. **每玩家赢家缓存**：每帧重算，没有缓存（见 4 节理由）。
6. **`GlobalSceneEffect` 一类的全局钩子**：本版没有。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 语义明确 | `SpecialVisuals` 每帧都调，未激活时 `isActive = false` |
| 语义明确 | `Music = -1` 表示"不选"，`0` 才是静音；群落默认 0 = 静音 |
| 生效 | `SurfaceBackgroundStyle` / `WaterStyle` / `MapBackground` / `MapBackgroundColor` 在赢家且档位允许时生效 |
| 本版不生效 | `UndergroundBackgroundStyle`（地下风格的选择入口在原生侧，另见背景管线）/ `MapBackgroundFullbright` |
