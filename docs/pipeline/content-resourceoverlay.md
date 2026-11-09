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

**不接**：`DisplayHoverText`（参考实现也只定义了它、没有挂调用点，见 2.6）；其它 HUD 元素。

## 2. 我们的设计

### 2.1 三个文件

作者基类 `Content/ModResourceOverlay.cs`（含 `ResourceOverlayDrawContext`）、表 `Pipelines/ResourceOverlay/ResourceOverlayTable.cs`、管线 `Pipelines/ResourceOverlay/ResourceOverlayPipeline.cs`。

### 2.2 作者 API 与参考实现逐行一致

方法面（含参数名与默认返回值）照抄参考实现：`PreDrawResource` / `PostDrawResource` / `PreDrawResourceDisplay` / `PostDrawResourceDisplay` / `DisplayHoverText`。`ResourceOverlayDrawContext` 的字段也逐个一致：`snapshot`、`resourceNumber`、`texture`、`position`、`source`、`color`、`rotation`、`origin`、`scale`、`effects`，外加 `DisplaySet`、`SpriteBatch` 两个 init 属性与 `Draw()`。

### 2.3 挂点

| 层 | 钩子 | 派发 |
|---|---|---|
| 单颗 | `GUIHearts.HeartDraw` / `GUIMana.StarDraw` | `PreDrawResource` → `orig` → `PostDrawResource`；Pre 返回 false 跳过这颗的 `orig`，Post 照派 |
| 整条 | `GUIHearts.Draw` / `GUIMana.Draw` | `PreDrawResourceDisplay` → `orig` → `PostDrawResourceDisplay`；Pre 返回 false 跳过整条 |
| 数值 | `GUIHearts.DrawLifeString` | `drawText` 为 false 时不调 `orig` |

**Install 无条件挂载**（与 menu 同一个教训）：`Install()` 跑在 `Build()`/收表之前，那时表是空的，按"有没有类型覆写"决定挂不挂永远判不到。现在全部挂上，分派时用 `HookScan` 位集跳过没覆写这一组的覆盖层，没覆写的作者零开销。

### 2.4 context 从哪来（与参考的机制差异）

参考实现没有用 `HeartDraw`/`StarDraw` 的参数拼 context，而是挂原生 `SpriteBatch.Draw`（`Host.Native()->OnResourceDraw`），拿到游戏真正要画的那一张贴图与完整变换（texture/position/source/color/rotation/origin/scale/effects）。**Pylon 没有这条原生绘制捕获通道**，所以：

* `position` / `scale` 用 `HeartDraw` / `StarDraw` 的实参（与游戏画这一颗时一致）。
* `snapshot` 用 `new PlayerStatsSnapshot(Main.LocalPlayer)`；`DisplaySet` 用 `Main.ActivePlayerResourcesSet`。
* `resourceNumber` 用这颗的序号。
* `texture` 按资源条样式给：经典 = `TextureAssets.Heart` / `TextureAssets.Mana`，FancyClassic = `Heart_Fill` / `Star_Fill`，横条 = `HP_Fill` / `MP_Fill`（`Main.Assets.Request` 取，失败回退经典）。参考是从原生绘制实参拿真值，我们只能按样式近似——这样参考作者的 `context.texture == TextureAssets.Heart` / 按名字比对的写法才能对上。
* `source` / `color` / `rotation` / `origin` / `effects` 是默认值（`null` / 白 / 0 / 零 / None）——参考拿到的是游戏当帧真实值，我们拿不到。

作者照常可以改 context 字段再调 `context.Draw()`；改完**不会**影响这一帧原版的绘制（context 按值传给每个覆盖层，与参考一致）。

**作者贴图的源矩形（真机大坑）**：作者贴图由 `LoadAsset` 创建，游戏包装的 `Texture2D.Width/Height` 在真机上是坏的（读它会 NRE，setter 静默无效；参考实现从不碰这两个，尺寸从 Unity 层读）。`source = null` 时 SpriteBatch 按宽高算源矩形 → 整张画不出来，真机表现为「回调跑了、画面没变」。所以 `ResourceOverlayDrawContext.Draw()` 在 `source` 为 null 时用 `PylonTexture.Size`（Unity 侧尺寸）补一个整张的源矩形——语义与「画整张」相同，作者不用关心。

### 2.5 三种资源条样式的验证

`Main.PlayerResourcesSets` 的键名随版本不同（本构建是 `New` / `Default` / `HorizontalBars`），
测试按字典实际键逐个切换并断言生命/魔力的 `Pre/PostResource` 都派发（RO-W2..W4，真机通过）。
作者侧比较贴图照参考示例的 `CompareAssets` 写法，但相等性用**原生指针**（与仓库里
`ModConfigMenu.SameNative` 同一套：`a.Pointer == b.Pointer`）——interop 的托管包装对象引用不稳定，
指针才是同一张资产的真判据。

### 2.6 顺序与异常

按注册序逐条派；每条各自 `try/catch`。单颗的 Pre 返回 false 只是这颗不画（Post 仍派，作者可能只想换画法）；整条的 Pre 返回 false 是整条不画。

### 2.7 DisplayHoverText

参考实现定义了它、`ResourceOverlayLoader` 也有派发方法，但参考的 hook 文件里没有调用点（本次逐行对照确认），所以本版同样只保留 API、不挂调用——写了的作者不会看到它被调用，文档明示。

## 3. 取舍理由

* **为什么 API 完全照参考？** 作者代码要能两边直接搬；签名差一个参数就是编译错误。之前的 `PreDrawLife/PreDrawMana` 版本已删。
* **为什么 context 的默认值不是游戏真值？** 没有原生绘制捕获，拿不到"这一帧这一张"的 source/color/rotation；按 `HeartDraw/StarDraw` 实参给 position/scale 已经是托管侧能拿到的最准值。
* **为什么每条覆盖层单独 `try/catch`？** 纯表现代码，一个作者画错不该让血条消失。
* **为什么 Install 无条件挂？** 见 2.3：装早了，按类型判断会永远判不到。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 覆盖层数量为 0 | 完全不介入（`orig` 照调，无额外开销） |
| `PreDrawResource` 抛异常 | 记一次，这颗照画 |
| `PreDrawResource` 返回 false | 这颗不画，`PostDrawResource` 照派 |
| `PreDrawResourceDisplay` 抛异常 | 记一次，整条照画 |
| `PreDrawResourceDisplay` 返回 false | 整条不画（数值仍按 drawText） |
| 取不到贴图（`TextureAssets.Heart` 为空） | context.texture 为空，作者应自行判空；原版绘制不受影响 |

## 5. 扩展方式

接 `DisplayHoverText`：在 `CommonResourceBarMethods.DrawLifeMouseOver` / `DrawManaMouseOver` 前后拿 `PlayerStatsSnapshot` 派发即可，基类不用改。接别的资源条样式：在对应的 `*ResourcesDisplaySet` 上挂同样两套钩子。

## 6. 与其它管线的边界

* **玩家**：生命/魔力数值与回复归玩家侧，本管线只画。
* **界面/配置**：HUD 布局与缩放归游戏的资源显示集合；覆盖层不参与布局。
* **场景效果**：无关。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 与参考一致 | `PreDrawResource` / `PostDrawResource` 收 `ResourceOverlayDrawContext`（不再是 index/position/scale） |
| 与参考一致 | 整条用 `Pre/PostDrawResourceDisplay` + `drawText` 控制数值 |
| 保留但不挂 | `DisplayHoverText`（参考也没有调用点） |