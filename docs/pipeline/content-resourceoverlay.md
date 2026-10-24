# 内容管线：资源覆盖层（ResourceOverlay）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（`Static`） | `SetStaticDefaults` |
| 单颗心 / 单颗魔力星画之前 | `PreDrawResource(ResourceOverlayDrawContext)`（false = 这颗不画） |
| 单颗心 / 单颗魔力星画之后 | `PostDrawResource(context)` |
| 整条生命 / 魔力条画之前 | `PreDrawResourceDisplay(snapshot, displaySet, drawingLife, ref textColor, out drawText)`（false = 整条不画） |
| 整条生命 / 魔力条画之后 | `PostDrawResourceDisplay(snapshot, displaySet, drawingLife, textColor, drawText)` |
| 生命数值 | `PreDrawResourceDisplay` 的 `drawText = false` 时这一帧不画 |

**已接**：单颗（心/星）与整条（生命/魔力）两层，生命数值开关。

**不接**：`DisplayHoverText`（只保留 API、没有调用点，见 2.7）；其它 HUD 元素。

## 2. 我们的设计

### 2.1 文件

作者基类 `Content/ModResourceOverlay.cs`（含 `ResourceOverlayDrawContext`）、表 `Pipelines/ResourceOverlay/ResourceOverlayTable.cs`、管线 `Pipelines/ResourceOverlay/ResourceOverlayPipeline.cs`、捕获 `Pipelines/ResourceOverlay/SpriteBatchCapture.cs`、静态门面 `Content/Loaders/ResourceOverlayLoader.cs`。

### 2.2 作者 API

基类方法：`PreDrawResource` / `PostDrawResource` / `PreDrawResourceDisplay` / `PostDrawResourceDisplay` / `DisplayHoverText`。`ResourceOverlayDrawContext` 字段：`snapshot`、`resourceNumber`、`texture`、`position`、`source`、`color`、`rotation`、`origin`、`scale`、`effects`，外加 `DisplaySet`、`SpriteBatch` 两个 init 属性与 `Draw()`。静态门面 `ResourceOverlayLoader` 提供数量/反查（`OverlayCount` / `GetOverlay`）与五个整链派发静态。

### 2.3 挂点

| 层 | 钩子 | 派发 |
|---|---|---|
| 单颗 | `GUIHearts.HeartDraw` / `GUIMana.StarDraw` | `PreDrawResource` → `orig` → `PostDrawResource`；Pre 返回 false 跳过这颗的 `orig`，Post 照派 |
| 整条 | `GUIHearts.Draw` / `GUIMana.Draw` | `PreDrawResourceDisplay` → `orig` → `PostDrawResourceDisplay`；Pre 返回 false 跳过整条 |
| 数值 | `GUIHearts.DrawLifeString` | `drawText` 为 false 时不调 `orig` |

挂载按 Plan 位集分组：有覆盖层覆写单颗组（`Pre/PostDrawResource`）才挂 `HeartDraw`/`StarDraw` 并装捕获，有覆写整条组（`*Display`）才挂 `Draw`/`DrawLifeString`。位集在注册期就算好（`RegistrationPipeline` 的 `HookScan.Of(type)` 写回 `Plan`），Install 时直接判表。分派时再用表里的 `HookMask` 跳过没覆写这一组的覆盖层，没覆写的作者零开销。表可热重建（`Refresh()`），mod 重载后每帧按表回落直通。

### 2.4 context 从哪来：SpriteBatch.Draw 捕获

单颗窗口开着时（`HeartDraw`/`StarDraw` 的 `orig` 执行期间），`SpriteBatchCapture` 用 MonoMod `On.` 钩子挂住 `SpriteBatch.Draw` 的全部 15 个重载（含 `Draw_Fast` 快路径与 destination-rect 族），把游戏这一笔的真实贴图与完整变换（texture/position/source/color/rotation/origin/scale/effects）交出来填进 context——不按样式猜。窗口由 `HeartDraw`/`StarDraw` 钩子开合：进 `orig` 前置位、返回后关闭，窗口外每一笔绘制直接直通原方法。

贴图对回 Asset：`TextureAssets.Heart` / `Heart2` / `Mana` 直接按原生指针比对，其余按 `texture.Name` 走 `Main.Assets.Request(ImmediateLoad)`；认不出的那一笔不派发给覆盖层，原版照画。资产相等性一律用原生指针（interop 的托管包装对象引用不稳定，指针才是同一张资产的真判据）。

**捕获内不重入**：每个捕获处理器在派发期间带着「派发中」标志——Pre 回调派发前置位、Post 回调派发完复位（try/finally 兜底，异常不会把标志卡死）。作者在 `PreDrawResource`/`PostDrawResource` 里调 `context.Draw()`（它自己就是一次 `SpriteBatch.Draw`）时，这笔嵌套绘制在处理器入口见到标志就直通原方法、不再进捕获：不会二次派发、不会顶掉外层静态上下文、作者在钩子里无条件自绘也不会无限递归。窗口内的普通绘制与窗口外的嵌套绘制都不受影响。

`snapshot` 用 `new PlayerStatsSnapshot(Main.LocalPlayer)`：整条钩每帧新建，只挂单颗钩时在单颗窗口首次命中时建。`DisplaySet` 用 `Main.ActivePlayerResourcesSet`，`resourceNumber` 用这颗的序号。

作者照常改 context 字段再调 `context.Draw()`；context 按值传给每个覆盖层，改完不影响这一帧原版的绘制。

**作者贴图的源矩形（真机大坑）**：作者贴图由 `LoadAsset` 创建，游戏包装的 `Texture2D.Width/Height` 在真机上是坏的（读它会 NRE，setter 静默无效）。`source = null` 时 SpriteBatch 按宽高算源矩形 → 整张画不出来，真机表现为「回调跑了、画面没变」。所以 `ResourceOverlayDrawContext.Draw()` 在 `source` 为 null 时用 `PylonTexture.Size`（Unity 侧尺寸）补一个整张的源矩形——语义与「画整张」相同，作者不用关心。

### 2.5 三种资源条样式的验证

`Main.PlayerResourcesSets` 的键名随版本不同（本构建是 `New` / `Default` / `HorizontalBars`）。作者侧比较贴图用原生指针。

**可达性（2026-11-18 真机三证）**：单颗钩与整条钩的窗口开在 `GUIHearts.HeartDraw` / `GUIMana.StarDraw` 上——本构建只有 **Fancy 系（默认 `New`）** 样式走这条路径；切到 `Default` / `HorizontalBars` 后单颗与显示钩 0 派发，记为该构建不可达（RO-W3/W4 带证 SKIP），默认样式全链绿。

### 2.6 顺序与异常

按注册序逐条派；每条各自 `try/catch`（2.7 的 `DisplayHoverText` 除外）。单颗的 Pre 返回 false 只是这颗不画（Post 仍派，作者可能只想换画法）；整条的 Pre 返回 false 是整条不画。textColor 从 White 起 ref 透传、drawText 逐个取与；整条派发在 `orig` 前、后各一轮，`drawText` 的聚合结果在生命数值钩生效。

### 2.7 DisplayHoverText

基类与 `ResourceOverlayLoader` 都有它，管线没有调用点——本版只保留 API、不挂调用，写了的作者不会看到它被调用，文档明示。这个派发方法目前也没有逐覆盖层 try/catch，与其它四个不一致；将来接入时一并补上。

## 3. 取舍理由

* **为什么单颗走绘制捕获而不是拼参数？** `HeartDraw`/`StarDraw` 的实参只有 position/scale；只有捕获 `SpriteBatch.Draw` 才拿得到游戏这一笔的真实贴图与全套变换。作者的 `context.texture == TextureAssets.Heart` 这类比较、按帧读 source/color 的画法才有真值可用。
* **为什么捕获要带重入门？** 资源覆盖层的标准用法就是改字段后 `context.Draw()`——这本身就是一次真实的 `SpriteBatch.Draw`。没有门的话它会再进一遍捕获：覆盖层回调翻倍、Post 派发拿到被嵌套轮次顶掉的静态上下文，作者不判贴图无条件自绘时更会无限递归直到栈溢出。
* **为什么捕获认不出的贴图就不派发？** context.texture 不能是空的（作者拿到就要画）；与其派一个画不出来的 context，不如让这一笔当普通绘制放过。
* **为什么每条覆盖层单独 `try/catch`？** 纯表现代码，一个作者画错不该让血条消失。
* **为什么按 Plan 位集分组挂载？** 捕获挂在全游戏的 `SpriteBatch.Draw` 上，每一笔绘制都要过一次 trampoline；没有覆盖层用到单颗钩时，这一层开销不该存在。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 覆盖层数量为 0 | 完全不介入（`orig` 照调，无额外开销） |
| `PreDrawResource` 抛异常 | 记一次，这颗照画 |
| `PreDrawResource` 返回 false | 这颗不画，`PostDrawResource` 照派 |
| `PreDrawResourceDisplay` 抛异常 | 记一次，整条照画 |
| `PreDrawResourceDisplay` 返回 false | 整条不画（数值仍按 drawText） |
| 捕获认不出贴图（反查失败） | 这一笔不派发给覆盖层，原版绘制不受影响 |
| 作者在钩子里 `context.Draw()` | 恰好画一次，不触发二次派发（捕获重入门） |
| 捕获处理器内抛异常 | 「派发中」标志由 finally 复位，后续捕获照常工作 |

## 5. 扩展方式

接 `DisplayHoverText`：在 `CommonResourceBarMethods.DrawLifeMouseOver` / `DrawManaMouseOver` 前后拿 `PlayerStatsSnapshot` 派发即可，基类不用改。接别的资源条样式：在对应的 `*ResourcesDisplaySet` 上挂同样两套钩子。

## 6. 与其它管线的边界

* **玩家**：生命/魔力数值与回复归玩家侧，本管线只画。
* **界面/配置**：HUD 布局与缩放归游戏的资源显示集合；覆盖层不参与布局。
* **场景效果**：无关。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 单颗收 `ResourceOverlayDrawContext` | 字段是游戏这一笔的真实值（绘制捕获）；可改后 `context.Draw()`，它在派发窗口内被重入门放行、只画这一笔 |
| 整条用 `Pre/PostDrawResourceDisplay` + `drawText` 控制数值 | textColor/drawText 按注册序聚合 |
| 保留但不挂 | `DisplayHoverText` |
