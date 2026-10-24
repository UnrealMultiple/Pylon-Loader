# 内容管线：Boss 血条（BossBar）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（`Static`，血条安装步） | `ModBossBar` / `GlobalBossBar` 的 `SetStaticDefaults`（共享状态初始化）；血条表重建 |
| `BigProgressBarSystem.Update` | 目标跟踪 + `ModBossBar.ModifyInfo`（经 `ValidateAndCollectNecessaryInfo` 收集生命/护盾四值） |
| `BigProgressBarSystem.Draw` | `ModBossBar.GetIconTexture`（图标）、`GlobalBossBar` / `ModBossBar` 的 `PreDraw` / `PostDraw`、用 `GUIBossBar.Draw` 换条 |
| `GUIBossBar.Draw`（原版条） | `GlobalBossBar` 的 `PreDraw` / `PostDraw`（数值、图标与皮肤参数可改，改后的值交回 `orig`） |

**不接**：血条显隐状态机（归 `BigProgressBarSystem`，本管线只决定"画出来是什么"）；`IconScale` / `TextOffset` / `BarCenter`（见 2.5）。

## 2. 我们的设计

### 2.1 文件

```
Pipelines/BossBar/BossBarPipeline.cs  管线本体：安装门、静态默认值派发、抓目标、两条绘制路径、皮肤注入窗口
Pipelines/BossBar/BossBarTable.cs     血条侧表：有哪些条、按 Mod名/类名 查
Content/ModBossBar.cs                 作者基类（四数值只读 + ModifyInfo / GetIconTexture / PreDraw / PostDraw）
Content/BossBarInfo.cs                收集参数（目标序号 + showText）
Content/Draw/BossBarDrawParams.cs     绘制参数袋（PreDraw/PostDraw 里改字段就改了这次绘制）
Globals/GlobalBossBar.cs              全局横切类（SetStaticDefaults / PreDraw / PostDraw）
Content/Tml/Npc.TML.cs                NPC.BossBar 关联扩展（读写管线的 assigned 表）
```

### 2.2 两条绘制路径

血条有两个来源，管线各有一条路径：

1. **mod 条**：NPC 在 `SetDefaults` 里 `NPC.BossBar = ModContent.GetInstance<T>()` 声明了血条。管线拦 `BigProgressBarSystem.Update` 抓目标并收集，`Draw` 时用自己的血条实例换掉游戏的条，参数化交给 `GUIBossBar.Draw` 画。
2. **原版条**：NPC 没声明 mod 条，游戏画自己的条。若 mod 挂了 `GlobalBossBar`，`On.GUIBossBar.Draw` 这一笔把游戏传入的数值与图标装进 `BossBarDrawParams` 交给 global 过一道（`PreDraw` → `orig` → `PostDraw`），global 改 `Life` / `LifeMax` / `Shield` / `ShieldMax` / `IconTexture` / `IconFrame` 有效；皮肤参数（`BarTexture` / `BarColor` / `IconColor` / `ShowText`）也在这一笔落地（见 2.4）。

两条路径互斥重入：mod 条路径内部调 `GUIBossBar.Draw` 换条时直接放行，不再过一遍 global，也不会重开皮肤窗口。

### 2.3 目标跟踪与收集（mod 条路径）

`Update` 在 `orig` 之后跑，三段式：

```
游戏已有 currentBar → 目标就是 _info.npcIndexToAimAt 指的 NPC，收集失败则清掉 currentBar（下帧重抓）
上一帧跟踪成功（Tracked）→ 继续对它收集；失败则丢掉跟踪
每 30 帧 → 扫 Main.npc 前 200 个：active、声明了血条、在屏幕四周 ±5000px 内取最近的一个
```

收集 = `ValidateAndCollectNecessaryInfo(ref BossBarInfo)`，`ModifyInfo` 三态：返回 true/false 直接采信；返回 null 时从 NPC 当前生命 clamp 回填（NPC 失效 = 收集失败）。`showText` 照传游戏的 `_info.showText`。成功后记下血条实例与目标序号（作者可在 `ModifyInfo` 里改 `info.npcIndexToAimAt`），`Draw` 阶段只消费这些字段，不再问作者。

### 2.4 皮肤注入：罩住条身那一笔 GUI 绘制

两条路径在同一点开皮肤窗口：`GUIBossBar.Draw` 这一笔的绘制前后（mod 条是管线用它换条，原版条是游戏自己画）。`PreDraw` 返回 false 不画条身时不开窗口。窗口期间，挂在共享绘制原语上的注入钩改写这一笔：

| 参数 | 接管点 | 生效方式 |
|---|---|---|
| `BarTexture` | `GUIPanel.DrawBacking` | 非 null 且与游戏这一笔的贴图不同 → 换成作者的贴图 |
| `BarColor` | `GUIPanel.DrawBacking` | 不是纯白 → 换成作者的颜色 |
| `IconColor` | `GUITransactionButton.Draw`（Texture2D + Rectangle + `Nullable<Color>` 重载） | 贴图指针等于本次血条图标的指针 → 写进该笔的可覆盖色；图标不走该原语时钩子自然不命中 |
| `ShowText` | `GUIString.Draw_*` / `DrawWithShadow_*`（8 个重载） | `false` → 这一族文字整段跳过 |

注入是改动检测式的：窗口携带 `PreDraw` 过后的参数，各注入钩只在对应参数离开窗口打开前的默认值（颜色非纯白、贴图非 null、`ShowText` 为 false）时才动手；作者没改的参数零注入，这一笔像素原样。`GUIPanel.Region` 在窗口内首次捕获面板布局，供 `BarCenter`（图标中心）取默认值。窗口在绘制调用返回时关闭（含异常路径），窗口外全部注入钩原样直通。窗口只罩一次 `GUIBossBar.Draw`：窗口内若有别的 UI 恰好走同一批原语会被一并改写，窗口外不受影响。

### 2.5 仍然不消费的参数

* **`IconScale`**：图标缩放写死在绘制原语的布局参数里，没有可改的入口。
* **`BarCenter` / `TextOffset`**：位置分别写在面板布局与 `String_Layout` 里，绘制原语没有对应参数可改。
* **`BarFrame`**：参数袋没有这个字段。

### 2.6 盾值编码

两条路径同一套：`ShieldMax > 0` 时透传作者的 `Shield` / `ShieldMax`，否则传 `(-1, 1)` 的"无盾"码。作者想给无盾 Boss 加盾写 `ShieldMax > 0`；想隐藏原版盾把 `ShieldMax` 写成 0 即可。

### 2.7 静态默认值与安装门

* **安装门**：Core 门恒装（`BigProgressBarSystem.Update` / `Draw` 两个钩子）；Gui 门在存在任一 `ModBossBar` 或 `GlobalBossBar` 时装（`GUIBossBar.Draw`、`GUIPanel`、`GUITransactionButton`、8 个 `GUIString` 重载）。
* **`SetStaticDefaults`**：安装期一次。`ModBossBar` 先置 `Registered` 再派发；`GlobalBossBar` 收进全局数组后逐实例派发，单个实例抛错记一条日志、不影响其它实例。血条不需要编号——NPC 直接声明实例，所以 global 没有"注册"标志，收进数组就是它的接入。
* **顺序**：先所有 `ModBossBar`，后所有 `GlobalBossBar`，与绘制时的钩子顺序（global 先 `PreDraw`、后 `PostDraw`）同向。

## 3. 取舍理由

* **为什么拦 `BigProgressBarSystem.Update` / `Draw`，而不只拦 `GUIBossBar.Draw`？** `GUIBossBar.Draw` 拿得到数值拿不到"这是谁"；`_info.npcIndexToAimAt` 是精确序号，画什么、画多少都在收集期定好，绘制期只消费，不再猜。
* **为什么把作者结论换参数交回 `GUIBossBar.Draw`，而不是自绘条身？** 自绘要复刻渐变、护盾叠层、文字排版，和原版长得不一样的风险大；参数化 + 皮肤注入覆盖作者最常改的外观面，改动最小、视觉最一致。
* **为什么皮肤窗口罩住整个 `GUIBossBar.Draw` 这一笔（两条路径同款）？** 注入点全是共享绘制原语（`GUIPanel` / `GUITransactionButton` / `GUIString`），把窗口压到一笔之内才能把"改的是哪一笔"限定到当前血条，窗口越小同帧误伤其它 UI 的机会越小。mod 条与原版条同款窗口，global 的皮肤能力在两条路径上表现一致。
* **为什么注入钩做成改动检测式？** 开窗口本身不能改变绘制：作者没改的参数必须零注入，原版条路径的每一笔像素都不该被加载器碰。窗口携带的参数在窗口打开前都是默认值，"离开了默认值"就是"作者改过了"。
* **为什么 `GlobalBossBar` 也要派发 `SetStaticDefaults`？** global 的 `PreDraw` / `PostDraw` 常依赖作者在 `SetStaticDefaults` 里初始化的共享状态；不派发就是静默失效（字段保持默认值、无报错），作者排查不出原因。
* **为什么收集失败要丢跟踪甚至清 `currentBar`？** 作者返回 false 的意思就是"这条别画"；拿旧值或错误值硬画比不画更糟。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 抓目标（`Update`）抛异常 | 记一次，本帧按无 mod 条处理 |
| `ModifyInfo` / 收集抛异常 | 记一次，当收集失败（丢跟踪 / 清 `currentBar`） |
| `ModifyInfo` 返回 false | 这条不接管（丢跟踪 / 清 `currentBar`） |
| `GetIconTexture` 抛异常 | 记一次，用兜底图标 |
| 兜底图标（`NpcHead[0]`）仍为 null | 本帧不画这条 mod 条 |
| 条身绘制抛异常 | 记一次，这一帧整笔不画（`orig` 也不跑） |
| `PreDraw`（global 或 mod 条）抛异常 | 记一次，当 `true` |
| `PreDraw` 返回 false | 不画条身，`PostDraw` 照跑，也不开皮肤窗口 |
| `PostDraw` 抛异常 | 记一次，忽略 |
| 原版条路径读不到目标（`_info` / NPC 无效） | 原样交回 `orig` |
| 原版条窗口内 `orig` 抛异常 | 皮肤窗口立即关闭，异常继续上抛 |
| `SetStaticDefaults`（mod 条或 global）抛异常 | 记一条，继续派发其它实例 |

## 5. 扩展方式

* **接 `IconScale`**：需要先在 `GUIBossBar.Draw` 内部找到图标缩放的落点。
* **接 `BarCenter` / `TextOffset`**：需要先找到 `GUIBossBar.Draw` 内部对应的绘制原语。

## 6. 与其它管线的边界

* **NPC 管线**：NPC 类型、`SetDefaults`、`npc.boss` 归它；`NPC.BossBar` 扩展属性是两边的接口（读写本管线的 assigned 表，Pointer 键 + type 校验）。
* **NPC 头像**：头像槽位归 NPC 头像管线（`NpcHeadBoss` 表 + `BossHeadTextures` 回填）；本管线只在 mod 条图标缺失时兜底读 `TextureAssets.NpcHead[0]`。
* **界面**：皮肤注入只挂画条会用到的 GUI 原语（`GUIPanel` / `GUITransactionButton` / `GUIString`），窗口外直通，不碰其它 UI。
* **场景效果 / 生物群系**：无关（血条不参与音乐/背景选择）。

### 这一版没接

1. **`IconScale`**：见 2.5。
2. **`BarCenter` / `TextOffset` / `BarFrame`**：形状上没有消费点（见 2.5）。
3. **血条显隐状态机**：归原版 `BigProgressBarSystem`，本管线只影响"画出来是什么"。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 关联 | `NPC.BossBar`（get/set 扩展）：`SetDefaults` 里赋实例 |
| 生效 | `ModBossBar` 与 `GlobalBossBar` 的 `SetStaticDefaults`（安装期各派发一次） |
| 生效 | `ModifyInfo` 三态、`GetIconTexture`、`PreDraw` / `PostDraw`；global 的 `PreDraw` / `PostDraw` 在 mod 条与原版条两条路径都跑 |
| 生效 | `BarTexture` / `BarColor` / `IconColor` / `ShowText`（mod 条绘制期间与 global 过手的原版条绘制期间） |
| 本版不生效 | `BarCenter` / `IconScale` / `TextOffset`；`BarFrame` 无字段 |
| 语义明确 | `ModifyInfo` false = 整条不画；`PreDraw` false = 只不画条身；`ShieldMax` ≤ 0 = 无盾 |
