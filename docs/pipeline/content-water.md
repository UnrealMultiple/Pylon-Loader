# 内容管线：水（Water）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（`Surface`） | 水样式的主贴图写进扩长后的 `TextureAssets.Liquid` |
| 每帧（本机玩家，场景效果选完赢家之后） | 赢家 `ModSceneEffect.WaterStyle` → 写 `Main.waterStyle` |
| 绘制（水面） | 不挂钩子：游戏自己按 `Main.waterStyle` 取 `TextureAssets.Liquid[style]` 画 |
| 绘制（瀑布） | `WaterfallManager.Draw` 之后补画一次：当前水样式声明的瀑布编号 + 原版 `DrawWaterfall` |
| 每个瀑布的绘制 | `WaterfallManager.DrawWaterfall_1` 前置：mod 瀑布样式的 `ColorMultiplier` 乘进 `ref Color`、`AddLight(x, y)` 加灯 |
| 溅水尘 | `Dust.dustWater` 前置：当前是 mod 水样式就返回 `GetSplashDust()` |
| 水滴 gore | `TileDrawing.EmitLiquidDrops` 期间标记 + `Gore.NewGore` 里换 `GetDropletGore()` |
| 雨（贴图） | `Main.DrawRain` 前后把 `TextureAssets.Rain` 换成作者的 png，画完换回 |
| 雨（列号） | `Rain.MakeRain` 前置把 `waterStyle` 暂换 0 + `Rain.NewRain` 后置改雨滴 `type` |
| 光穿透 | `LightingEngine` / `LegacyLighting` 的 `UpdateLightDecay` 之后改 `LightDecayThroughWater` |

**已接**：瀑布颜色（`ColorMultiplier`）与瀑布灯光（`AddLight`）——挂在 `WaterfallManager.DrawWaterfall_1`（带 `ref Color` 与格坐标的那个重载），见对应小节。

**已接**：群落染发色（`Player.GetHairColor` 后置，见 2.2e）。

**已接**：斜坡水贴图（作者放 `<类名>_Slope.png` 就写 `TextureAssets.LiquidSlope`，见 2.2f）。

**已接**：瀑布淡入（切进 mod 瀑布时从 0 淡到 1，见 2.2g）。

**不接**：瀑布淡出（原因见 2.2g）。

## 2. 我们的设计

### 2.1 两个文件 + 选择入口在场景效果侧

`Pipelines/Water/WaterTable.cs`（模板表 + 编号）、`Pipelines/Water/WaterPipeline.cs`（扩表 + 贴图 + 每帧写 `Main.waterStyle`）。

选谁这件事**不新建入口**：`ModSceneEffect` 上已经有 `WaterStyle` 属性，`SceneEffectPipeline` 每帧本来就要选赢家，所以赢家确定之后由它把 `WaterStyle` 交给本管线。

### 2.2 编号从运行期表长接

水样式的总数没有编译期常量（`WaterStyleID` 那套只到 15），所以起点按 `TextureAssets.Liquid.Length` 在扩表之前捕获（`TypeTables.WaterVanilla`），之后按注册序发号。

### 2.2b 瀑布：编号、贴图表与绘制入口

瀑布样式的编号空间与水面**不同**：`WaterfallManager` 的 `maxTypes` 是 28，贴图表是 `private static Asset<Texture2D>[] waterfallTexture`（`refs/dump.cs:73678`）——**不是** `TextureAssets` 里的公开表，但 interop 代理把私有静态字段生成为公开属性，所以读写就是 `WaterfallManager.waterfallTexture[编号] = asset`，不需要原生按名查找。

扩表复用同一套 `TableGrower`：`TypeTables` 新增 `TableKind.Waterfall`（类清单只有 `Terraria.WaterfallManager` 一张表），起点按扩表前的表长捕获。

绘制分两步，都是原版已经在做的事：

1. 原版的 `WaterfallManager.Draw` 照常跑（画它认识的那 28 种）；
2. 本管线在这个钩子之后读 `Main.waterStyle`：如果不是原版水样式，就问它的 `ChooseWaterfallStyle()` 要一个瀑布编号，是 mod 瀑布样式就调一次 `self.DrawWaterfall(spriteBatch, 编号, 1f)` 补画。

这样"水面用 mod 水样式 + 瀑布用 mod 瀑布样式"和"水面用 mod 水样式 + 瀑布仍是森林"两种情况都只需要作者在 `ChooseWaterfallStyle()` 里改一个返回值。

### 2.2g 瀑布淡入

原版的液体淡入淡出靠 `Main.liquidAlpha[style]`，这张表只到原版水样式数，没有 mod 水样式的槽位。本管线自己维护一个"当前瀑布编号 + 不透明度"：编号变了就从 0 重新淡，每帧 `+0.02`（约 50 帧到位，半秒左右），`DrawWaterfall` 传这个值。

**为什么只做淡入不做淡出**：只有"当前水样式声明的那条瀑布"会被补画，切走之后就不画了——淡出没有画面可淡。真要做淡出，得在切换后继续画上一条若干帧，那需要额外记录"上一条是哪个"并处理它已经不属于当前水样式的情况；收益（半秒的视觉过渡）不值得这份状态。

### 2.2f 斜坡水贴图

斜坡水用另一张贴图（`TextureAssets.LiquidSlope[style]`），表长度与 `Liquid` 相同、由 `TypeTables` 一起扩。`InstallSteps` 每个内容只写一张表，所以这条路径由水管线自己补：`Build` 里对每个水样式尝试 `Mod.LoadAsset(类名 + "_Slope")`，成功就写进 `LiquidSlope[style]`；没有这个文件只记一条 warn，那一格保持空——游戏画斜坡水时退回主贴图，画面不坏。

**为什么用 `_Slope` 后缀而不是约定 `<类名>/Slope`**：贴图路径已经是类名（`Mod/类名`），后缀拼法最短，也让"有没有斜坡图"在资源目录里一眼可见。

### 2.2e 群落染发色

原版让"水的样式"决定发色（丛林水把头发染绿）。本管线挂 `Player.GetHairColor` 后置：当前 `Main.waterStyle` 是 mod 水样式、**且**这个玩家正站在把这条水当自己水样式的群落里，就返回作者的 `BiomeHairColor()`。

**为什么要那道群落闸**：`ModWaterStyle.BiomeHairColor()` 的默认值是丛林绿（与原版一致）。只按 `Main.waterStyle` 判断的话，任何用到 mod 水样式的场景（熔岩主题、虚空主题）都会把玩家默认染绿；加上"人在这个群落里"才符合"群落染发色"这个名字。

**代价**：每次取发色要扫一遍群落表（数量是几十），而发色只在绘制/换装时取，不在每帧热路径上。

### 2.2d 瀑布的颜色与灯光

瀑布的绘制有两个重载：`DrawWaterfall(spriteBatch, Style, Alpha)` 是整批入口，`DrawWaterfall_1(waterfallType, x, y, opacity, position, ref sourceRect, ref color, effects)` 才是**每个瀑布单独画**的那一次——它带 `ref Color`（能改颜色）与格坐标 `x, y`（知道画在哪），是唯一同时满足这两个需求的入口。

本管线挂它：

* `waterfallType` 是 mod 瀑布样式 → 先问作者的 `ColorMultiplier(ref r, ref g, ref b, ref a)`（默认全 1，等于不改），再把四个通道乘到 `color` 上（越界钳到 0..255）；
* 同一个位置调一次 `AddLight(x, y)`，作者的灯就加在瀑布所在的格上；
* 原版瀑布直接 `orig` 放行，一次多余调用都没有。

**为什么不用整批入口**：`DrawWaterfall(spriteBatch, Style, Alpha)` 里没有任何颜色参数，颜色是它在内部按水样式算的；在这个入口外面改不了里面那次的 `Color`。

**注意 `self` 参数**：这个钩子是实例方法，签名里带 `Terraria.WaterfallManager self`（`on-signatures.txt` 里就写着），漏了会编译不过。

### 2.2c 溅水尘与水滴 gore

两个回调都在"水花/水滴产生"那一瞬间：

* **溅水尘**（`GetSplashDust`）：原版 `Dust.dustWater()` 返回落水水花的尘编号；当前水样式是 mod 的就直接返回作者的值，否则原版原样返回。
* **水滴 gore**（`GetDropletGore`）：原版水滴是 `705 + Main.waterStyle` 这一族编号，只在方块滴水（`TileDrawing.EmitLiquidDrops`）里产生。所以先用一个线程静态标记把那段调用圈起来，`Gore.NewGore` 只在这个标记里且编号正是 `705 + Main.waterStyle` 时才替换——爆炸、掉落物碎裂走的 `NewGore` 完全不受影响。

编号超过原版表长（指到另一个 mod 的水滴 gore）时：先按 `GoreID.WaterDrip` 生成，再把实例的 `gore.type` 改成目标编号。`Gore.Update` 是按实例的 `type` 找行为的，改完这一粒之后每帧都按新编号走。

### 2.2d 雨与光穿透

**雨分两条**，因为它们的作用点不同：

* **贴图**是每帧一次的事：`Main.DrawRain` 之前把 `TextureAssets.Rain` 换成作者的 png（`EmitLiquidDrops` 那种"每条路径"的粒度不存在——原版画雨读的就是这一个全局 Asset），画完在 `finally` 里换回原版，避免影响原版水样式和其它绘制。作者的 png 按路径加载（`GetRainTexture()` 返回路径，不是 Asset），路径不存在或返回空就走原版。
* **列号**是每滴一次的事：雨滴的 `type` 是贴图里的列（原版 0..2）。原版 `Rain.MakeRain` 生成雨滴时按 `705 + Main.waterStyle` 这类编号办事，mod 水样式的编号会让它落到其它段里，所以前置钩子先把 `Main.waterStyle` 暂换成 0 并记下"正在给这个 mod 水样式下雨"，`Rain.NewRain` 之后把新雨滴的 `type` 换成 `GetRainVariant()`；出界（不在 0..2）回落到 0。

**光穿透**：`LightingEngine` 与 `LegacyLighting` 各有一个 `UpdateLightDecay`，原版在里头算好默认值 `(0.88, 0.96, 1.015)`；本管线在原版之后，如果当前是 mod 水样式就把 `self._workingLightMap.LightDecayThroughWater` 换成作者的三个分量。两套引擎都要写是因为不同分辨率的设置会走不同的光照实现。

### 2.3 只写水面那一张

`TextureAssets.Liquid[style]` 写作者的主贴图；`TextureAssets.LiquidSlope[style]` 在作者另外放了 `<类名>_Slope.png` 时也写（见 2.2f）。

和发型 alt 贴图同一个取舍：水下斜坡的水面会按空槽处理，所以文档里明确列为缺口，而不是让作者以为写了就有。

### 2.4 为什么在 `Player.Update` 之后写

`Main.waterStyle` 是全局单值，原版每帧按本机玩家所在的群系重写它（丛林→2、腐化→3 ……）。我们的写入必须在原版那次之后，否则会被覆盖。

落点是 `SceneEffectPipeline` 里已有的每帧回调（`Player.Update` 之后、只对本机玩家），那里赢家已经算出来了，顺手把 `WaterStyle` 交过来——省掉一次"再选一遍赢家"的重复计算。

### 2.5 没有水样式时不回写

赢家没有声明 `WaterStyle`（或压根没有赢家）时，本管线**什么都不做**：`Main.waterStyle` 保持游戏自己算出来的值。这样"没接水样式的场景"行为与不装这个管线的原版完全一致。

## 3. 取舍理由

* **为什么水样式挂在场景效果上，而不是自己选？** 见 2.1：`ModSceneEffect.WaterStyle` 是现成的声明，自己再写一套选择逻辑就等于有两套互不相通的优先级规则。
* **为什么主贴图与斜坡贴图分两处写？** 见 2.3：`InstallSteps` 每个内容只对着一张表写一次；斜坡是第二条贴图路径，由 `WaterPipeline.WriteSlopeTextures` 自己加载、自己写表，失败只记 warn（多数作者不放这张图）。
* **瀑布表的可见性不是障碍**：表是 `WaterfallManager` 的私有静态字段（`waterfallTexture`），但 interop 代理按内存布局生成，私有字段一样是公开属性——直接 `WaterfallManager.waterfallTexture[编号] = asset` 就行。（我最初按"私有 = 拿不到"写了原生按名查找的绕路，是多余的，已删。）
* **水滴为什么用线程静态标记？** `Gore.NewGore` 全游戏都在调；只有"方块滴水"这一条路径上的水滴才是要换的水滴。用 `EmitLiquidDrops` 的前后包一段标记，替换条件就收窄到"这条路径 + 这个编号"，不会误伤别的 gore。
* **为什么瀑布不透明度固定 1？** 原版 `Main.liquidAlpha` 的长度只到原版水样式数，mod 水样式没有槽位（参考实现靠原生侧 `SetWaterStyleCount` 把数组撑大，本版没有这条原生通道），所以"瀑布跟着液体淡入淡出"不接；固定 1 在正常情况下与原版一致。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 原版表长捕获不到（表还没建） | 不发号、不扩表，本轮什么都不做 |
| 作者贴图缺失 | 该样式槽留空：用到它时水面画不出来（不崩） |
| 赢家没声明 `WaterStyle` | 不写 `Main.waterStyle`（保持原版值） |
| 声明的样式没注册（作者手写了编号） | 不写，记一次 |
| 游戏重建贴图表 | `TableGrower` 的补扩逻辑会重新扩一遍（水面那张）；瀑布那张由 `Maintenance` 的 Watch 逻辑同样处理 |
| `ChooseWaterfallStyle` 抛异常 | 记一条，这一帧不补画瀑布 |
| `GetSplashDust` / `GetDropletGore` 抛异常 | 记一条，回落到原版的尘/水滴 |
| `GetRainVariant` 抛异常 / 出界 | 记一条，按列号 0 处理 |
| `GetRainTexture` 抛异常 / 路径不存在 | 记一条，这一帧用原版雨贴图 |
| `LightColorMultiplier` 抛异常 | 记一条，用原版的水体衰减值 |
| `GetRainVariant` 抛异常 / 出界 | 记一条，按列号 0 处理 |
| `GetRainTexture` 抛异常 / 路径不存在 | 记一条，这一帧用原版雨贴图 |
| `LightColorMultiplier` 抛异常 | 记一条，用原版的水体衰减值 |
| `GetDropletGore` 超原版表长 | 先按 `WaterDrip` 生成再改实例 `type`，效果等同直接给编号 |
| 瀑布编号指向另一个 mod 的瀑布 | 正常：`WaterfallTable.Of` 只认自定义段，指到原版编号时那次补画直接跳过（原版那次 `Draw` 已经画了它） |
| `waterfallTexture` 还没建（LoadContent 之前） | 不发号、不扩表、不写贴图；`Refresh` 每次重试 |
| 场景效果每帧都抛异常 | 赢家为空 → 不写 `Main.waterStyle`，水面保持原版（异常按 `ContentErrors` 记） |

## 5. 扩展方式

接雨：在雨的绘制入口把 `GetRainTexture` / `GetRainVariant` 接上；接瀑布颜色/灯光：`WaterfallManager.DrawWaterfall` 是私有方法但 interop 有钩子（`DrawWaterfall_1`），可以按编号分派到 `ColorMultiplier` / `AddLight`。

这些扩展都只动"贴图/数据"这一侧：编号与水样式的选择已经稳定，不需要再改 `WaterTable`、`WaterfallTable` 或 `SceneEffectPipeline` 的交接。

## 6. 与其它管线的边界

* **场景效果（23）**：选赢家的逻辑归它；本管线只消费赢家的 `WaterStyle`。
* **生物群系（22）**：`ModBiome` 继承自 `ModSceneEffect`，所以群落声明的 `WaterStyle` 走同一条路，本管线不需要区分。
* **贴图设施**：扩表与写槽归 `TypeTables` / `TextureSlots`。
* **光照**：液体光照归 `Lighting`；`LightColorMultiplier` 已接（改 `LightDecayThroughWater`），见 2.2c 的雨/溅水/光穿透三条。

### 这一版没接

1. ~~**斜坡水贴图**（`LiquidSlope`）~~：**已接**，见 2.2f。
3. ~~**雨**（`GetRainTexture` / `GetRainVariant`）~~：**已接**（`Rain.MakeRain` 暂换列 + `Main.DrawRain` 换贴图）。
4. ~~**溅水尘 / 水滴 gore**（`GetSplashDust` / `GetDropletGore`）~~：**已接**（`Dust.dustWater` 前置 + `EmitLiquidDrops` 标记 + `Gore.NewGore`）。
5. ~~**光穿透倍率**（`LightColorMultiplier`）~~：**已接**（两套光照引擎的 `UpdateLightDecay` 之后改 `LightDecayThroughWater`）。
6. ~~**群落染发色**（`BiomeHairColor`）~~：**已接**，见 2.2e。
7. ~~**瀑布颜色与灯光**~~：**已接**，见对应小节。
8. ~~**瀑布淡入淡出**~~：**淡入已接**（见 2.2g）；淡出不做（切走就不画了，没有画面可淡）。

### 已接（原缺口）

9. ~~**瀑布样式**~~：已接，见 2.2b。
10. ~~**溅水尘 / 水滴 gore**~~：已接，见 2.2c。
11. ~~**雨的贴图与列选择**~~（`GetRainTexture` / `GetRainVariant`）：已接，见对应小节。
12. ~~**光穿透倍率**~~（`LightColorMultiplier`）：已接，见对应小节。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 语义明确 | 水样式编号由管线分配（`TextureAssets.Liquid` 原版表长起、注册序） |
| 本版生效 | 只要场景效果赢家声明了 `WaterStyle`，主贴图就会生效 |
| 本版生效（瀑布） | `ModWaterfallStyle` 发号 + 贴图 + `ChooseWaterfallStyle` 选中后的补画 |
| 本版生效（水花） | `GetSplashDust` / `GetDropletGore` 按当前水样式生效 |
| 本版生效（雨 / 光） | `GetRainTexture` / `GetRainVariant` / `LightColorMultiplier` 按当前水样式生效 |
| 本版不生效 | 瀑布颜色/灯光/淡入淡出、斜坡、染发色 |
