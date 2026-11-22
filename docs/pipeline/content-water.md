# 内容管线：水（Water）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（Build，游戏表就绪之后） | 发号；扩 `TextureAssets.Liquid` / `TextureAssets.LiquidSlope`、`LiquidRenderer._liquidTextures`、`WaterfallManager.waterfallTexture`；写水贴图与瀑布贴图 |
| 安装期（一次性） | `Main.DrawWaters` 两个循环的边界补丁（原版数 → 水样式总数） |
| 每帧（`DrawWaters` 的 orig 之前） | `Main.liquidAlpha` / `Main.activeLiquidAlpha` 复查扩容 |
| 每帧（本机玩家，场景效果选完赢家之后） | 赢家声明的 `WaterStyle` → `CalculateWaterStyle` 返回 mod 编号 |
| 绘制（水面） | 不接管绘制：游戏按水样式自己画；循环补丁与透明度表在钩子里就位 |
| 绘制（瀑布） | `WaterfallManager.Draw` 之后补画一次：当前水样式声明的瀑布编号 + 自维护淡入 |
| 每个瀑布的绘制 | `WaterfallManager.DrawWaterfall_1` 前置：mod 瀑布样式的 `ColorMultiplier` 乘进 `ref Color`、`AddLight(x, y)` 加灯 |
| 溅水尘 | `Dust.dustWater` 前置：当前是 mod 水样式就返回 `GetSplashDust()` |
| 水滴 gore | `TileDrawing.EmitLiquidDrops` 期间标记 + `Gore.NewGore` 里换 `GetDropletGore()` |
| 雨（贴图） | `Main.DrawRain` 前后把 `TextureAssets.Rain` 换成作者的 png，画完换回 |
| 雨（列号） | `Rain.MakeRain` 前置把 `waterStyle` 暂换 0 + `Rain.NewRain` 后置改雨滴 `type` |
| 光穿透 | `LightingEngine` / `LegacyLighting` 的 `UpdateLightDecay` 之后改 `LightDecayThroughWater`（作者的三个分量再乘 0.91） |
| 染发色 | `Player.GetHairColor` 后置：站在声明这条水的群落里就返回 `BiomeHairColor()` |

**已接**：水面循环的放开（见 2.2a）——mod 水样式超过原版数后，淡入淡出与水面绘制照常走。

**已接**：透明度表扩容（`Main.liquidAlpha` / `Main.activeLiquidAlpha`，见 2.2a）——放开的循环按总数索引这两张表，表不扩就是每帧越界读写。

**已接**：瀑布颜色（`ColorMultiplier`）与瀑布灯光（`AddLight`），见 2.2d。

**已接**：群落染发色（`Player.GetHairColor` 后置，见 2.2e）。

**已接**：贴图写入与回退链（主贴图 / `_Block` / `_Slope`，见 2.2f、2.3）。

**已接**：瀑布淡入（切进 mod 瀑布时从 0 淡到 1，见 2.2g）。

**不接**：瀑布淡出与多条瀑布的交叉淡化（原因见 2.2g）。

## 2. 我们的设计

### 2.1 三个文件 + 选择入口在场景效果侧

`Pipelines/Water/WaterTable.cs`（模板表 + 编号）、`Pipelines/Water/WaterPipeline.cs`（扩表 + 贴图 + 全部钩子 + 每帧透明度表复查）、`Pipelines/Water/DrawWatersLimits.cs`（水面循环补丁）。

选谁这件事**不新建入口**：`ModSceneEffect` 上已经有 `WaterStyle` 属性，`SceneEffectPipeline` 每帧本来就要选赢家，所以赢家确定之后由它把 `WaterStyle` 交给本管线（见 2.4）。

### 2.2 编号从运行期表长接

水样式的总数没有编译期常量（`WaterStyleID` 那套只到 15），所以起点按 `TextureAssets.Liquid.Length` 在扩表之前捕获（`WaterTables.Vanilla`），之后按注册序发号。`ModWaterStyle.Slot` 是 `Type` 的别名（= 编号），作者侧两个名字等价。

### 2.2a 水面循环的放开与透明度表

`Main.DrawWaters` 里有两个按水样式数走的循环：一个逐样式升降 `Main.liquidAlpha`（水面淡入淡出），并把本帧画过水的样式记进 `Main.activeLiquidAlpha`——斜坡/半砖后面的水面由 `TileDrawing` 按这张列表补画；另一个把正在淡出的样式的水面画出来。构建把这两个循环的边界 15 编成了立即数：前一个按元素地址索引（立即数里带 8 个 float 的数组头）、b.ne 退出，后一个在循环头比较、b.eq 退出。mod 水样式超过原版数后两个循环都停在 15——mod 样式的水面不动、不画。

放开分两件事，缺一不可：

1. **循环补丁**（一次性）：`DrawWatersLimits.Build` 用 `MethodCode.PatchLoopEnd` 把两个循环的边界立即数从原版数换成 `WaterTable.Total`（地址索引版按"目标 + 8"换，比较版直接换），总数定下来后跑一次；没匹配上不落笔、只记 warn。
2. **透明度表扩容**（每帧复查）：`On.Main.DrawWaters` 钩在 orig 之前把 `Main.liquidAlpha`（float[]，`refs/dump.cs:51373`）与 `Main.activeLiquidAlpha`（int[]，`refs/dump.cs:51372`）跟 `WaterTable.Total` 对齐——短了才扩：`ArrayGrow.GrowZeroed` 分配一张新数组、把旧内容逐格拷过去、新槽位保持 0，然后替换静态属性。这个钩只在有 mod 水样式时才装（没有 mod 样式时总数不超过原版数，两张表本来够长）。

**为什么必须扩表**：补丁后的循环按总数索引这两张表，表还停在原版数的话每一帧都是对数组的越界读写——扩容是循环补丁的前置条件，不是可选项。

**为什么是"换一张更长的"而不是原地扩**：数组长度定死在对象头里，扩容只能是分配新数组；这两张表是 `Main` 的静态属性（get/set 齐全），interop 侧直接给属性赋值就完成了替换，原版随后读静态字段拿到的就是新表。

**为什么每帧复查而不是只扩一次**：游戏可能在运行期重建这些静态表，后注册的样式也会把总数抬高；复查的成本只有两次长度比较，短了才动。

**为什么新槽位补 0**：0 = 这帧没淡入。淡入淡出循环会把当前样式的槽位逐帧推向 1、其它样式留在 0，与原版首次建表的状态一致；前 15 格的淡入淡出状态则随旧内容逐格保留。

### 2.2b 瀑布：编号、贴图表与绘制入口

瀑布样式的编号空间与水面**不同**：`WaterfallManager` 的 `maxTypes` 是 28，贴图表是 `private static Asset<Texture2D>[] waterfallTexture`——**不是** `TextureAssets` 里的公开表，但 interop 代理把私有静态字段生成为公开属性，所以读写就是 `WaterfallManager.waterfallTexture[编号] = asset`，不需要原生按名查找。

扩表复用同一套 `TableSet` / `TableMaintenance`：`WaterTables.Waterfall`（类清单只有 `Terraria.WaterfallManager` 一张表），起点按扩表前的表长捕获。

绘制分两步，都是原版已经在做的事：

1. 原版的 `WaterfallManager.Draw` 照常跑（画它认识的那 28 种）；
2. 本管线在这个钩子之后读 `Main.waterStyle`：如果不是原版水样式，就问它的 `ChooseWaterfallStyle()` 要一个瀑布编号，是 mod 瀑布样式就调一次 `self.DrawWaterfall(spriteBatch, 编号, alpha)` 补画，alpha 是自维护的淡入值（见 2.2g）。

这样"水面用 mod 水样式 + 瀑布用 mod 瀑布样式"和"水面用 mod 水样式 + 瀑布仍是森林"两种情况都只需要作者在 `ChooseWaterfallStyle()` 里改一个返回值。

### 2.2c 溅水尘与水滴 gore

两个回调都在"水花/水滴产生"那一瞬间：

* **溅水尘**（`GetSplashDust`）：原版 `Dust.dustWater()` 返回落水水花的尘编号；当前水样式是 mod 的就直接返回作者的值，否则原版原样返回。
* **水滴 gore**（`GetDropletGore`）：原版水滴是 `705 + Main.waterStyle` 这一族编号，只在方块滴水（`TileDrawing.EmitLiquidDrops`）里产生。所以先用一个线程静态标记把那段调用圈起来，`Gore.NewGore` 只在这个标记里且编号正是 `705 + Main.waterStyle` 时才替换——爆炸、掉落物碎裂走的 `NewGore` 完全不受影响。

作者返回的编号超过原版 gore 表长（指到另一个 mod 的水滴段）时：先按 `GoreID.WaterDrip` 生成，再把实例的 `gore.type` 改成目标编号。`Gore.Update` 是按实例的 `type` 找行为的，改完这一粒之后每帧都按新编号走。

### 2.2d 瀑布的颜色与灯光

瀑布的绘制有两个重载：`DrawWaterfall(spriteBatch, Style, Alpha)` 是整批入口，`DrawWaterfall_1(waterfallType, x, y, opacity, position, ref sourceRect, ref color, effects)` 才是**每个瀑布单独画**的那一次——它带 `ref Color`（能改颜色）与格坐标 `x, y`（知道画在哪），是唯一同时满足这两个需求的入口。

本管线挂它：

* `waterfallType` 是 mod 瀑布样式 → 先问作者的 `ColorMultiplier(ref r, ref g, ref b, a)`：r/g/b 是 0..255 的通道绝对值进出，`a` 是这次绘制的不透明度（只读）；把乘后的 r/g/b 写回 `color`，alpha 通道不动；
* 同一个位置调一次 `AddLight(x, y)`：沿 `waterfalls` 表里 `type == 0` 的条目、从 `y` 起步进 3 格加灯，上限取 `stopAtStep` 与 24 的较小值；
* 原版瀑布直接 `orig` 放行，一次多余调用都没有。

**为什么不用整批入口**：`DrawWaterfall(spriteBatch, Style, Alpha)` 里没有任何颜色参数，颜色是它在内部按水样式算的；在这个入口外面改不了里面那次的 `Color`。

**注意 `self` 参数**：这个钩子是实例方法，签名里带 `Terraria.WaterfallManager self`，漏了会编译不过。

### 2.2e 群落染发色

原版让"水的样式"决定发色（丛林水把头发染绿）。本管线挂 `Player.GetHairColor` 后置：当前 `Main.waterStyle` 是 mod 水样式、**且**这个玩家正站在把这条水当自己水样式的群落里，就返回作者的 `BiomeHairColor()`。

**为什么要那道群落闸**：`ModWaterStyle.BiomeHairColor()` 的默认值是丛林绿（与原版一致）。只按 `Main.waterStyle` 判断的话，任何用到 mod 水样式的场景（熔岩主题、虚空主题）都会把玩家默认染绿；加上"人在这个群落里"才符合"群落染发色"这个名字。

**代价**：每次取发色要扫一遍群落表（数量是几十），而发色只在绘制/换装时取，不在每帧热路径上。

### 2.2f 斜坡水贴图

斜坡水用另一张贴图（`TextureAssets.LiquidSlope[style]`），方块水也是（`TextureAssets.Liquid[style]`）；这两条路径由水管线自己的 `SetupTextures` 一并写（见 2.3）。

**为什么用 `_Slope` 后缀而不是约定 `<类名>/Slope`**：贴图路径已经是类名（`Mod/类名`），后缀拼法最短，也让"有没有斜坡图"在资源目录里一眼可见。

### 2.2g 瀑布淡入

原版水面的淡入淡出靠 `Main.liquidAlpha[style]`，这张表现在已经随水样式总数扩容（见 2.2a）：mod 样式有自己的格子，原版循环会替它做淡入淡出。瀑布的透明度没有跟着读表：本管线自己维护"当前瀑布编号 + 不透明度"——编号变了就从 0 重新淡，每帧 `+0.02`（约 50 帧到位，半秒左右），`DrawWaterfall` 传这个值。

**为什么只做淡入不做淡出**：补画只画"当前水样式声明的那条瀑布"，切走之后就不画了——淡出没有画面可淡。要吃到原版循环的淡出与多条瀑布的交叉淡化，得把补画范围改成"所有 alpha 大于 0 的 mod 样式"，那是绘制范围的改变，不跟表扩容捆绑（见"这一版没接"）。

### 2.2h 雨与光穿透

**雨分两条**，因为它们的作用点不同：

* **贴图**是每帧一次的事：`Main.DrawRain` 之前把 `TextureAssets.Rain` 换成作者的 png（原版画雨读的就是这一个全局 Asset），画完在 `finally` 里换回，避免影响原版水样式和其它绘制。作者的 png 按路径加载（`GetRainTexture()` 返回 Mod 目录里的路径串，不是 Asset），路径为空或不在本 mod 目录里就走原版。
* **列号**是每滴一次的事：雨滴的 `type` 是贴图里的列（原版 0..2）。原版 `Rain.MakeRain` 生成雨滴时按 `705 + Main.waterStyle` 这类编号办事，mod 水样式的编号会让它落到其它段里，所以前置钩子先把 `Main.waterStyle` 暂换成 0 并记下"正在给这个 mod 水样式下雨"，`Rain.NewRain` 之后把新雨滴的 `type` 换成 `GetRainVariant()`；出界（不在 0..2）回落到 0。

**光穿透**：`LightingEngine` 与 `LegacyLighting` 各有一个 `UpdateLightDecay`，原版在里头算好默认值 `(0.88, 0.96, 1.015)`；本管线在原版之后，如果当前是 mod 水样式就把 `self._workingLightMap.LightDecayThroughWater` 换成作者的三个分量再乘 0.91。两套引擎都要写是因为不同分辨率的设置会走不同的光照实现。

### 2.3 三张贴图的写入与回退

水面/方块/斜坡各有一张表，`SetupTextures` 一并写：

| 表 | 写什么 | 缺图时落到 |
|---|---|---|
| `LiquidRenderer._liquidTextures[style]` | 主贴图 | 原版 `[0]`（森林水） |
| `TextureAssets.Liquid[style]` | `X_Block` | 主贴图 |
| `TextureAssets.LiquidSlope[style]` | `X_Slope` | 方块贴图（再缺就是主贴图） |

加载走 `Mod.LoadAsset`，默认异步：`_Block`/`_Slope` 首帧可能还没就绪，就绪前那一格保持写入前的状态（不会因为缺图而崩）。主贴图是 `ModType.Texture` 安装期上传的，不受异步影响。

### 2.4 选择怎么进游戏

场景效果每帧（`Player.Update` 钩、只对本机玩家）选赢家，赢家声明了 `WaterStyle` 就把该效果交给本管线。本管线**不直写 `Main.waterStyle` 字段**——原版每帧自己会按场景重写它，直写是一条和原版赛跑的时序。挂的是 `On.Main.CalculateWaterStyle` 前置：先跑原版拿到原版结果，赢家声明的样式已注册且过了三道门（喷泉激活 / 血月夜 / 原版背景白名单 {1..8,13,14}）时返回 mod 编号，否则原样返回原版值。原版把"这一帧水是什么样式"的决策汇聚在这个方法上，在返回值上替换比在字段上赛跑稳。

有一个已知限制：本构建实测原生的字段写入点疑似被 AOT 内联、不消费钩子的返回值，mod 值最终落不落 `Main.waterStyle` 字段要以真机复核为准（水面绘制读的就是这个字段）。

### 2.5 没有水样式时不回写

赢家没有声明 `WaterStyle`（或压根没有赢家）时，本管线**什么都不做**：钩子里原样返回原版值，`Main.waterStyle` 保持游戏自己算出来的值。这样"没接水样式的场景"行为与不装这个管线的原版完全一致。

## 3. 取舍理由

* **为什么水样式挂在场景效果上，而不是自己选？** 见 2.1/2.4：`ModSceneEffect.WaterStyle` 是现成的声明，自己再写一套选择逻辑就等于有两套互不相通的优先级规则。
* **为什么水面循环用机器码补丁，而不是挂个钩子自己画？** 循环边界是编进方法体的立即数，托管层没有能改它的数据表；`PatchLoopEnd` 换掉两个立即数之后，淡入淡出、`activeLiquidAlpha` 记录、斜坡补画这些原版行为对 mod 样式全部自动生效——自己画等于把这些重写一遍。
* **为什么透明度表在 `DrawWaters` 钩内每帧复查，而不是只在安装时扩一次？** 循环补丁落笔后就一直在；表则可能被游戏重建、也可能被后注册的样式抬高总数。复查放在 orig 之前，正常帧只有两次长度比较，短了才分配新表。
* **为什么瀑布透明度自维护而不读 `Main.liquidAlpha`？** 表已有 mod 槽位（2.2a），读表可行；但补画只画当前一条瀑布，切走就没有画面，读表也只能吃到淡入那一半。维持自维护值（2.2g）行为不变；要交叉淡化得连补画范围一起改（见"这一版没接"）。
* **为什么水贴图三张表、回退链各自独立？** 三张表的消费者不同（水面 / 方块水 / 斜坡水），作者可能只画了其中一两张；回退链让"只放主贴图"的 mod 三张表都有合理内容，而不是留空槽。
* **瀑布表的可见性不是障碍**：`waterfallTexture` 是 `WaterfallManager` 的私有静态字段，interop 代理按内存布局生成，私有字段一样是公开属性——直接 `WaterfallManager.waterfallTexture[编号] = asset` 就行。
* **水滴为什么用线程静态标记？** `Gore.NewGore` 全游戏都在调；只有"方块滴水"这一条路径上的水滴才是要换的水滴。用 `EmitLiquidDrops` 的前后包一段标记，替换条件就收窄到"这条路径 + 这个编号"，不会误伤别的 gore。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 原版表长捕获不到（表还没建） | 不发号、不扩表，本轮什么都不做，下轮再试 |
| `DrawWaters` 循环没匹配上（补丁没落笔） | 记 warn，mod 水样式的水面不动/不画（原版行为不受影响） |
| 透明度表复查抛异常 | 记一条，orig 照跑；那一帧表可能还是短的 |
| 作者主贴图缺失 | `LiquidRenderer._liquidTextures` 槽落原版 `[0]`；`TextureAssets` 两张表该槽不写 |
| `X_Block` / `X_Slope` 缺失 | 沿回退链落主贴图/方块贴图（见 2.3），不崩 |
| 附加贴图加载抛异常 | 记一条 warn，那一格沿回退链落下去 |
| 赢家没声明 `WaterStyle` | 返回原版水样式（保持原版值） |
| 声明的样式没注册（作者手写了编号） | 返回原版值，记一次 |
| 场景赢家每帧都抛异常 | 记一条（按 `ContentErrors`），水面保持原版 |
| 游戏重建 `TextureAssets` / `waterfallTexture` 表 | `TableMaintenance` 的 Watch 周期性复查重扩（见 §6） |
| 游戏重建 `LiquidRenderer._liquidTextures` | 只在 Build 扩一次，运行期重建不自动重扩；`Refresh` 会按当前表界重写贴图 |
| `ChooseWaterfallStyle` 抛异常 | 记一条，这一帧不补画瀑布 |
| `ColorMultiplier` / `AddLight` 抛异常 | 记一条，颜色/灯用原版那次绘制的结果 |
| `GetSplashDust` 抛异常 | 记一条，回落到原版的尘 |
| `GetDropletGore` 抛异常 | 记一条，按原编号放行（mod 号落在 gore 表其它段，不会崩） |
| `GetDropletGore` 超原版表长 | 先按 `WaterDrip` 生成再改实例 `type`，效果等同直接给编号 |
| `GetRainVariant` 抛异常 / 出界 | 记一条，按列号 0 处理 |
| `GetRainTexture` 抛异常 / 路径不存在 | 记一条，这一帧用原版雨贴图 |
| `LightColorMultiplier` 抛异常 | 记一条，用原版的水体衰减值 |
| 瀑布编号指向原版段 | 跳过补画（原版那次 `Draw` 已经画过它） |
| `waterfallTexture` 还没建（LoadContent 之前） | 不写贴图，记 warn；`Refresh` 每次重试 |

## 5. 扩展方式

接雨：在雨的绘制入口把 `GetRainTexture` / `GetRainVariant` 接上；接瀑布颜色/灯光：`WaterfallManager.DrawWaterfall` 是私有方法但 interop 有钩子（`DrawWaterfall_1`），可以按编号分派到 `ColorMultiplier` / `AddLight`。

这些扩展都只动"贴图/数据"这一侧：编号、选择与水面循环已经稳定，不需要再改 `WaterTable`、`DrawWatersLimits` 或 `SceneEffectPipeline` 的交接。

## 6. 与其它管线的边界

* **场景效果（23）**：选赢家的逻辑归它；本管线只消费赢家的 `WaterStyle`（`SceneEffectPipeline.WaterEffect`）。
* **生物群系（22）**：`ModBiome` 继承自 `ModSceneEffect`，所以群落声明的 `WaterStyle` 走同一条路，本管线不需要区分。
* **贴图设施**：`TextureAssets` 与瀑布表的扩表/重建自愈归 `TableSet` / `TableMaintenance`（周期性 Watch 游戏重建）；两张透明度表与 `LiquidRenderer._liquidTextures` 不在表集里——前者由 `DrawWaters` 钩每帧复查（2.2a），后者只在 Build 扩。
* **机器码补丁设施**：水面循环补丁走 `MethodCode`（`DrawWatersLimits`），归补丁层，本管线只调用。
* **光照**：液体光照归 `Lighting`；`LightColorMultiplier` 已接（改 `LightDecayThroughWater`），见 2.2h。

### 这一版没接

1. **瀑布淡出与交叉淡化**：补画只有当前一条瀑布，切走就没有画面可淡（2.2g）。要接需要把补画范围改成"所有 alpha > 0 的 mod 样式"，透明度直接读 `Main.liquidAlpha`。
2. **染发剂的染发剂门**：当前群落门是"进群落就染、立即、无渐变、不看微光"（2.2e 的设计意图）；"装备染发剂才生效 + 颜色逐步逼近 + 微光跳过"那套没接。
3. **雨贴图返回任意资产**：`GetRainTexture` 是本 mod 目录的路径串（2.2h）；返回任意已注册 `Asset<Texture2D>` 的能力面没接。
4. **水面循环补丁的重打与 `LiquidRenderer` 的运行期自愈**：补丁一次性（总数中途抬高不重打），`LiquidRenderer._liquidTextures` 只在 Build 扩；透明度表有每帧复查兜底（2.2a）。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 语义明确 | 水样式编号由管线分配（`TextureAssets.Liquid` 原版表长起、注册序），`Slot` 别名 = `Type` |
| 生效 | 主贴图 / `_Block` / `_Slope` 三张表 + 水面循环放开 + 透明度表随总数扩容 |
| 生效（瀑布） | `ModWaterfallStyle` 发号 + 贴图 + `ChooseWaterfallStyle` 选中后的补画（自维护淡入）、`ColorMultiplier`、`AddLight` |
| 生效（水花） | `GetSplashDust` / `GetDropletGore` 按当前水样式生效 |
| 生效（雨 / 光 / 发色） | `GetRainTexture` / `GetRainVariant` / `LightColorMultiplier` / `BiomeHairColor` 按当前水样式生效 |

## 批次补充（2026-11-21）

- 水面循环放开配套补全：`Main.liquidAlpha`（float[]）与 `Main.activeLiquidAlpha`（int[]）在 `On.Main.DrawWaters` 钩内、orig 之前每帧复查扩容——短了才扩到水样式总数，`ArrayGrow.GrowZeroed` 分配新表、旧内容逐格保留、新槽位 0，替换静态属性。补丁后的循环按总数索引这两张表，表不扩就是每帧越界读写。
- 其余不变：循环补丁一次性、瀑布透明度自维护淡入、染发色群落门、雨贴图路径串、`ColorMultiplier` 直通形状（r/g/b 0..255 绝对值进出、alpha 不动）。
