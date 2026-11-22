# 内容管线：背景（Background）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（Layout / Static） | 表层/地下风格发号（`Slot`）、三类扩表（贴图表、透明度表、`SurfaceBackgroundID.Sets` 两张布尔表）、逐风格 `SetStaticDefaults` |
| 安装期（Build） | `Backgrounds/*.png` 同步加载并写进 `TextureAssets.Background` 尾部槽位 |
| `Main.GetPreferredBGStyleForPlayer` | 场景效果赢家 `ModSceneEffect.SurfaceBackgroundStyle` + 优先级档位 → 返回值换成 mod 风格号；`GlobalBackgroundStyle.ChooseSurfaceBackgroundStyle` 最后改写 |
| `Main.UpdateBGVisibility_BackLayer` / `FrontLayer` | 远层透明度推进；`ModifyFarFades`（mod 风格 + `GlobalBackgroundStyle.ModifyFarSurfaceFades`） |
| `Main.DrawSurfaceBG_BackMountainsStep1/2`、`GetFogPower` | 远层（`ChooseFarTexture`）、中层（`ChooseMiddleTexture`）、近层（`PreDrawCloseBackground` + `ChooseCloseTexture`）补画 |
| `Main.DrawBackground_PickUndergroundBackgroundStyle` | 地下换号；`GlobalBackgroundStyle.ChooseUndergroundBackgroundStyle` |
| `Main.get_backTextureValues` / `get_oldBackTextureValues` | `FillTextureArray` 的 5 层地下贴图换装（含旧帧那份） |

**不接**：背景之上的云/星/月亮（归云管线）。地图背景归场景效果管线。`Main.bgStyle` 不由管线直写，原生每帧消费 `GetPreferredBGStyleForPlayer` 的返回值。

## 2. 我们的设计

### 2.1 文件与安装门

`Pipelines/Background/` 下三个文件：`BackgroundTable.cs`（发号 + 两张布尔 Set 的扩容）、`BackgroundTextureLoader.cs`（背景贴图槽：注册、扩表、写槽、取图）、`BackgroundPipeline.cs`（六链 + 地下三条钩子）。

两个安装门（GateHub）：**表层门**在「有 mod 表层风格，或全局覆写了选号/淡入」时挂六链（换号、前后层可见性、远/中两步补画、雾功率）；**地下门**在「有 mod 地下风格，或全局覆写了选号/贴图」时挂三条（地下换号 + 新旧两份 `backTextureValues`）。全局风格按 `HookGuard.Overrides` 逐方法判定，零内容零钩子。`Refresh` 幂等重跑发号，热重载新增风格后各表按新总数补扩。

### 2.2 编号

表层风格号从 **17** 起（本构建原版有 16 个表层风格，起点写死，游戏更新加风格时要跟着改），地下从 **22** 起。发号幂等（`Registered` 标记），`Slot` 与 `Type` 同值；`BackgroundTable.Find(name)` 支持按名查找。贴图槽是另一个域：`TextureAssets.Background` 的 mod 槽从 `Main.maxBackgrounds` 之后顺延，风格号与贴图槽号不混用。

### 2.3 三类扩表

| 表 | 时机 | 目标长度 |
|---|---|---|
| `TextureAssets.Background`、`Main.backgroundWidth/Height` | `ResizeArrays`（Build 期） | `Main.maxBackgrounds + 待装贴图数` |
| `Main.bgAlphaFrontLayer` / `bgAlphaFarBackLayer` | `Ensure`（按需，本帧赢家或 `Main.bgStyle` 是 mod 槽位时） | 表层风格总数 |
| `SurfaceBackgroundID.Sets.IsForest` / `IsDesertVariant` | `BackgroundTables.Grow`（Build / Refresh，先扩再发号） | 表层风格总数（按注册内容计） |

贴图表扩容后把 `AddBackgroundTexture` 注册的待装贴图同步加载写进尾部槽位（`LoadAsset` + Unity 侧 `PylonTexture.Size` 读尺寸——互操作下游戏侧 `Texture2D.Width/Height` 读 0）；来源是 `AutoloadBackgrounds` 对 mod 目录的扫描（路径含 `backgrounds/` 目录段、大小写不敏感）与作者手动注册（只在本 mod 目录内解析，`OtherMod/Path` 不支持）。`ResizeArrays` 没有待装贴图时早退。

两张布尔 Set 只认 `IsForest` / `IsDesertVariant` 两个字段，尾部清零扩容，建表后不重建，扩一次即到位。不走 TableGrower 的通用扩表器：它给尾部填元素 0 或众数，而 `IsForest[0]` 是 true，会把 mod 风格默认标成森林系，所以直接用 `ArrayGrow.GrowZeroed`。

### 2.4 作者标记入口（森林系 / 沙漠变体）

两张 Set 在游戏里就是普通 `bool[]`（互操作层 `Il2CppStructArray<bool>`，可索引可写；游戏静态构造用 `SetFactory` 建好后即普通数组）。作者在 `ModSurfaceBackgroundStyle.SetStaticDefaults` 里写：

```
SurfaceBackgroundID.Sets.IsForest[Slot] = true;         // 森林系：参与原版「森林档位」的判定
SurfaceBackgroundID.Sets.IsDesertVariant[Slot] = true;  // 沙漠变体
```

时机由管线保证：先扩 Set、再发号（`Slot` 有效且落在表长之内）、最后逐风格跑 `SetStaticDefaults`——标记那一刻表已足够长。原版按当前 `Main.bgStyle` 读这两张表，mod 槽位扩容后在界内。

### 2.5 选号链（表层）

`GetPreferredBGStyleForPlayer`：先跑原版拿到原版风格，再按场景效果赢家的优先级换 mod 风格——`BiomeHigh` 直换；`BiomeMedium` 在原版是森林系（0/10/11/12）或 3、7 时换；`BiomeLow` 在森林系时换；最后全局 `ChooseSurfaceBackgroundStyle` 改写。赢家或 `Main.bgStyle` 是 mod 槽位时顺手 `Ensure()`。菜单态直接返回原版值（菜单背景由菜单管线换号）。异常回落原版值并记一条。

### 2.6 淡入淡出

**远层**（`UpdateBGVisibility_BackLayer`）：当前风格是 mod 槽位时走手写循环——本风格透明度按 `Main.backgroundLayerTransitionSpeed` 上涨、其余压落，各自夹 0..1（速度非正时兜底 0.005）。不调原版 `DrawBG_ModifyBGFarBackLayerAlpha`：它吃的 `Il2CppSystem.Nullable` 参数在真机互操作下读 `HasValue` 会 NRE，两个 override 参数原样交回 `orig`。

**前层**（`UpdateBGVisibility_FrontLayer`）：先 `orig`，再把 `bgAlphaFrontLayer` 拷到托管缓冲、派发 `ModifyFarFades`（mod 风格 + 全局）、写回。菜单态下全局钩子仍会收到派发（带原版菜单风格号）。

**菜单背景**：菜单管线在 `UpdateBGVisibility_BackLayer` 这同一个钩子上把 `Main.bgStyle` 改成菜单主题槽号，本管线的钩子读它生效；两条钩子的先后取决于订阅顺序（见 §4）。

### 2.7 远 / 中 / 近三层

`Step1` / `Step2` 钩子后补画远/中层，`GetFogPower` 前补画近层，保持原版层序。每个风格先读自己的透明度（0 则跳过）：远/中层 `ChooseFarTexture` / `ChooseMiddleTexture` 返回 `TextureAssets.Background` 的槽号（可复用原版素材），按原版公式平铺（`bgLoops` / `bgStartX` / `bgWidthScaled` / `bgTopY` / `bgScale`）；近层先问 `PreDrawCloseBackground`（false = 作者自画进 spriteBatch），再取 `ChooseCloseTexture` 的四个引用参数 `scale` / `parallax` / `a` / `b`（默认即原版值 1.25 / 0.37 / 1800 / 1750），`topY = Step1Top × a + b + scAdj + push`，并经 `SkyManager.DrawToDepth` 按视差入深度。着色统一 `Main.ColorOfSurfaceBackgroundsBase × alpha`，写回 `Main.ColorOfSurfaceBackgroundsModified`。贴图槽越界或资产为空该层该帧跳过。

### 2.8 地下

`PickUndergroundBackgroundStyle`：先跑原版，赢家声明 `UndergroundBackgroundStyle` 且 `Slot >= 0` 时按同样的档位换（「洞穴」按游戏自己的 `caveBackX` / `caveBackStyle` 推，数组不足 4 槽按 -1 处理，走非洞穴分支），全局改写，异常回落。贴图换装按风格缓存（槽号 + 5 张贴图）：mod 风格从作者 `FillTextureArray` 取 5 槽（第 6 层固定 6），原版风格仅在存在全局时透传原版 `backTexture` 槽号再换装；`get_backTextureValues` / `get_oldBackTextureValues` 两份都换，`backTexture` 编号一并回写。

## 3. 取舍理由

* **为什么 mod 槽位完全接管不调 `orig`**：原版按 `Main.bgStyle` 的绘制分支不认识 mod 槽位，进去不画东西还可能改状态。
* **为什么远层淡入手写循环**：互操作下 `Nullable` 不可读，原版过渡方法不可直调；手写循环与原版逐帧结果一致（见 §5 的回迁条件）。
* **为什么两张 Set 用清零扩容**：Set 的语义是「默认皆非、作者显式标 true」；尾部填非零值会悄悄改变所有 mod 风格的归类，且原版按槽号直读的路径会拿到假 true。
* **为什么贴图未就绪跳帧而不是重试**：mod 贴图 Build 期已同步加载、原版槽由游戏启动期加载，常态不触发；跳帧是可验证的最小行为。
* **为什么编号起点写死**：互操作层没有可靠的编译期原版风格数可依（游戏侧 `Count` 走运行期字段存储），先按本构建已知内容数写死，行为可预期（见 §5）。
* **为什么菜单态不拦全局钩子**：前层淡入的全局派发在菜单态也跑，标题画面允许全局改透明度；这是登记过的宽松行为。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 贴图槽越界 / 资产为空 / 读取异常 | 该层该帧跳过（`Texture` 返回 null） |
| 扩表后槽位仍越界 | 记 Error 一条，该贴图不写槽 |
| 贴图加载失败 | 记 Error 一条，该槽留空 |
| 两张 Set 的数组未建（互操作未就绪） | 跳过扩容；此时也不应有 mod 槽位在用 |
| 作者 `SetStaticDefaults` 抛异常 | 上抛，由安装管线按步捕获；其后风格的 `SetStaticDefaults` 不再跑 |
| 两张 Set 扩容异常 | 记一条，表保持原版长度（mod 标记会越界抛错，原版读不受影响） |
| 选号 / 淡入 / 绘制 / 地下各钩子异常 | 各自 try/catch，回落 `orig` 或跳过，记一条 |
| `bgAlpha*` 尚未建表 | `Ensure` 对 null 不分配；BackLayer / FrontLayer 判空回落或返回；DrawBack / DrawClose 会 NRE（理论隐患：原版在背景绘制前总会建好这两张表） |
| 洞穴数组不足 4 槽 | 洞穴判定按 -1，`BiomeMedium` / `BiomeLow` 档不换地下风格 |
| 菜单主题钩子晚于本管线 BackLayer | 该帧远层透明度不向菜单槽位涨，近层不受影响 |

## 5. 扩展方式

* 编号起点改运行期锚点（按原版表长捕获），免跟游戏更新手改常量；两张 Set 的目标长度随之自适应。
* `Texture()` 里补一次按需拉起重试，贴图未就绪可自愈。
* 选号链改用场景效果管线的赢家槽号快照，保证同帧各消费点一致（当前每帧重读作者 getter）。
* 菜单换号与淡入收进同一钩子，消除订阅顺序依赖。
* 远层淡入在互操作修复 `Nullable` 后可换回原版过渡方法，吃齐带 override 的强制过渡。

## 6. 与其它管线的边界

* **场景效果（23）**：选赢家与优先级归它，本管线每帧读其 `SurfaceBackgroundStyle` / `UndergroundBackgroundStyle`（异常有上报）。
* **菜单（MenuPipeline）**：主题选择/落盘/切回归它；菜单背景绘制复用本管线钩子。
* **云（26）**：云/星/月亮归云管线，本管线不碰天空内容。
* **贴图 / 安装设施**：贴图槽扩表与写槽在 `BackgroundTextureLoader`，Build 期一次完成；透明度表按需扩；两张 Set 的扩容归 `BackgroundTables`。
* **Tables 设施**：两张 Set 不走 TableGrower 通用扩表器（见 2.3），直接用 `ArrayGrow`。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 生效 | `SetStaticDefaults` 里可写 `SurfaceBackgroundID.Sets.IsForest[Slot]` / `IsDesertVariant[Slot]`，表已扩容、槽位在界内 |
| 语义明确 | 两张 Set 默认皆非，只有作者显式写 true 才归类；原版按 `Main.bgStyle` 读，mod 槽位认账 |
| 生效 | `ChooseFarTexture` / `ChooseMiddleTexture` / `ChooseCloseTexture` 的返回值是 `TextureAssets.Background` 槽号（mod 槽经 `GetBackgroundSlot` 获取，可复用原版槽号） |
| 语义明确 | `AddBackgroundTexture` 只在本 mod 目录内解析路径，`Backgrounds/` 目录的 png 自动注册 |
| 生效 | 地下 `FillTextureArray` 给 5 槽（第 6 层固定 6），新旧两帧都换 |
