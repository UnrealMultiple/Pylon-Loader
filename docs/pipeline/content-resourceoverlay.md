# 内容管线：资源覆盖层（ResourceOverlay）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 生命条画之前 / 之后 | `PreDrawLife` / `PostDrawLife`（参数是 `Main.spriteBatch`） |
| 魔力条画之前 / 之后 | `PreDrawMana` / `PostDrawMana` |
| 生命条悬停提示之前 / 之后 | `PreDrawLifeMouseOver` / `PostDrawLifeMouseOver` |
| 魔力条悬停提示之前 / 之后 | `PreDrawManaMouseOver` / `PostDrawManaMouseOver` |
| 安装期（`Static`） | `SetStaticDefaults` |
| 单颗心 / 单颗魔力星 | `PreDrawResource` / `PostDrawResource`（false = 这颗不画） |
| 条（FancyClassic 样式） | `DrawLifeBar` / `DrawManaBar` 前后各派一次 |

**已接**：HorizontalBars 样式（`HorizontalBarsPlayerResourcesDisplaySet.Draw` 前后派四次，粒度是整块，见 2.2c）。

**不接**：条本身的数值、其它 HUD 元素。

## 2. 我们的设计

### 2.1 三个文件（一个作者基类 + 表 + 管线）

这一条在 `Content/` 里没有现成的作者基类（其余 23 条都有），所以本管线**顺带定义作者 API**：`Content/ModResourceOverlay.cs` + `Pipelines/ResourceOverlay/ResourceOverlayTable.cs` + `Pipelines/ResourceOverlay/ResourceOverlayPipeline.cs`。

### 2.2 只挂在两条条的绘制上

可挂的入口里，只有 `ClassicPlayerResourcesDisplaySet.DrawLife` / `DrawMana` 是"一条资源条画一次"的粒度：

```
DrawLife：派 PreDrawLife → orig → 派 PostDrawLife
DrawMana：派 PreDrawMana → orig → 派 PostDrawMana
```

**不挂在 `Draw(self)` 上**：那是整个资源显示集合的绘制，粒度太粗——作者想在"生命条之上、魔力条之下"画东西就没有位置了。

**不调 `orig` 以外的东西**：条本身的绘制、数值、动画全部照旧，作者只能往 `spriteBatch` 里画。

悬停提示是同一套形状：`CommonResourceBarMethods.DrawLifeMouseOver` / `DrawManaMouseOver` 前后各派一次，让作者能在提示框上下加画。

### 2.3 给的是 spriteBatch，不是坐标

回调参数只有 `SpriteBatch`。理由：资源条的坐标在不同机型/缩放下由游戏自己的布局算（手机版尤其如此），把坐标算在 loader 里会在别的分辨率上错位；给 `spriteBatch` 让作者自己决定画哪儿，是本版唯一能保证正确的接口。

### 2.2b 单颗心 / 魔力星；其它资源条样式

**单颗的粒度**：`GUIHearts.HeartDraw` / `GUIMana.StarDraw` 是「画一颗」的入口（带这条里的序号、位置、缩放）。前置回调返回 false 就跳过这一颗的 `orig`（作者想换画法就自己画），后置照派——与整条「只能加画」不同，单颗上作者可以**减**。

**其它样式**：本构建有三种资源显示（`refs/dump.cs` 154298 起）：

* `ClassicPlayerResourcesDisplaySet`：整条一次（原 3.2）；单颗走的 `GUIHearts`/`GUIMana` 也在这条路径上。
* `FancyClassicPlayerResourcesDisplaySet`：`DrawLifeBar` / `DrawManaBar` 与 Classic 同形（整条 + `spriteBatch`），接同一套派发。
* `HorizontalBarsPlayerResourcesDisplaySet`：细粒度挂点是 `LifePanelDrawer` / `ManaPanelDrawer` / `LifeFillingDrawer`…（带 `ref sprite/offset/drawScale/sourceRect` 的抽屉参数）——「作者自己拼条」的形状，本版不用它；用的是整块入口 `Draw`（见 2.2c）。接它就要把抽屉参数一一映射进作者 API，本版不接；整块入口已接（见 2.2c）。

### 2.2c 横条样式（HorizontalBars）

横条样式把生命与法力画在同一块里，没有"生命条一次、法力条一次"的分开入口。本管线挂 `HorizontalBarsPlayerResourcesDisplaySet.Draw`，按顺序派四次：生命前、法力前 → 原版整块 → 法力后、生命后。

**代价写在明处**：作者拿到的是整块的时机，比心/星（单颗）与 FancyClassic（整条）都粗；想逐段控制的人得自己按 `Main.LocalPlayer.statLife` 画。换来的是三种资源条样式都有统一的"前后各一次"语义，作者不用为样式差异写分支。

### 2.4 顺序

按注册序逐条调；每条各自 `try/catch` —— 一个覆盖层抛异常不该让生命条消失。

## 3. 取舍理由

* **为什么新建作者基类而不是复用别的？** 本仓库其它管线的基类都对应游戏里已有的概念（`ModBossBar`、`ModSceneEffect`…）；资源覆盖层在游戏里没有对应类型，只能由 loader 定义。
* **为什么只有生命/魔力？** 本构建里资源显示只有这两条可挂的绘制函数；护盾、坐骑耐力这些没有独立入口。
* **为什么 HorizontalBars 只给"整块"粒度？** 它的细粒度挂点是面板/填充抽屉（参数是 `ref` 的贴图、偏移、缩放、裁剪框），接它等于让作者重新实现一遍条；而整块入口（`Draw`）与 FancyClassic 的条同形，语义不变、实现小。想要逐段控制的作者可以自己读 `Main.LocalPlayer.statLife` 画。
* **为什么每条覆盖层单独 `try/catch`？** 覆盖层是纯表现代码，一个作者的绘制出错不该让玩家的血条消失——那会让"装了个 HUD mod"变成"看不见自己血量"的事故。
* **为什么不接悬停提示？** `DrawLifeMouseOver` / `DrawManaMouseOver` 没有参数、也没有可写的文本，接了也只能"在提示前后各跑一次"，给人一个没有输入输出的回调，不如不接。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 覆盖层数量为 0 | 完全不介入（`orig` 照调，无额外开销） |
| `PreDrawLife` 抛异常 | 记一次，条照画 |
| `PostDrawLife` 抛异常 | 同上 |
| 资源显示集合不是 Classic 样式 | 本版不接，没有任何回调被调用 |
| `spriteBatch` 为 null（极少见的时机） | 照派；作者自己判空 |

## 5. 扩展方式

接别的资源条样式：在对应的 `*ResourcesDisplaySet` 上挂同样两条钩子即可，作者基类不需要改。接悬停提示：需要游戏给出可写的文本参数，当前构建没有。

两种扩展都不需要改表结构：表的粒度是"一个覆盖层实例"，与它在哪些绘制点被调用无关。新增绘制点只是在 `Install` 里多挂一个钩子、在 `Dispatch` 里多一个分支。

## 6. 与其它管线的边界

* **玩家（其他管线）**：生命/魔力的数值与回复归玩家侧，本管线只画。
* **界面/配置**：HUD 的布局与缩放归游戏的资源显示集合。
* **场景效果**：与屏幕特效无关。

覆盖层与"界面层"的分界：界面自己画的东西（按钮、面板）不归本管线；本管线只保证"生命条 / 魔力条这两个具体绘制点的前后有位置"。

另一个边界：覆盖层**不参与**布局计算——游戏把条画在哪儿、条多宽多高，本管线不读也不改。作者要贴着条画，就得自己按屏幕尺寸算（这也是 3.3 只给 `spriteBatch` 的直接后果）。

### 这一版没接

1. ~~**其它资源条样式**~~：**FancyClassic 已接**（`DrawLifeBar`/`DrawManaBar`，与 Classic 同形）；**HorizontalBars 也已接**（`Draw` 前后各派生命/法力两次，粒度是整块，见 2.2c）。
2. **条本身的绘制替换**：Classic/FancyClassic 上作者只能加画；**单颗心/星可以减**（`PreDrawResource` 返回 false），见 2.2b。
3. **坐标与布局参数**：只给 `spriteBatch` / 位置与缩放（见 2.3）。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 新增 | `ModResourceOverlay` 基类：生命/魔力条的 `Pre/PostDraw`，以及悬停提示的 `Pre/PostDraw*MouseOver` |
| 语义明确 | 回调参数只有 `SpriteBatch`，坐标由作者自己算 |
| 本版生效（单颗） | `PreDrawResource` / `PostDrawResource`：心的序号、位置、缩放原样给作者 |
| 本版生效（FancyClassic） | 条粒度的 Pre/Post 与 Classic 同一套 |
| 生效 | HorizontalBars 样式（前后各一次，粒度是整块；不是逐段） |
