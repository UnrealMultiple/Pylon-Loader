# 内容管线：Boss 血条（BossBar）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| `GUIBossBar.Draw` | `ModBossBar.ModifyInfo`（要画多少/画不画）、`GetIconTexture`（图标）、`PreDraw` / `PostDraw` |
| 同一处 | `ModBossBar.Collected`（把这一帧的四个值收进作者对象） |

**不接**：血条条身贴图/位置/颜色/缩放/文字偏移这些绘制参数、`BossBarInfo.validatedAtLeastOnce`、盾以外的原版血条状态机、非 `npc.boss` 的 NPC。

## 2. 我们的设计

### 2.1 两个文件

`Pipelines/BossBar/` 下：`BossBarPipeline.cs`（`GUIBossBar.Draw` 分派）、`BossBarTable.cs`（血条模板表 + 名字查找）。

### 2.2 唯一入口是 `GUIBossBar.Draw`

本版游戏的血条状态机在 `BigProgressBarSystem` 里，它**没有**可挂的 `On.` 入口（生成器没给它出钩子），唯一能拦到的是真正执行绘制的 `GUIBossBar.Draw(life, lifeMax, icon, iconFrame, shield, shieldMax)`。所以本管线的做法是：

1. 用传进来的 `life` / `lifeMax` 反查"游戏正在看哪个 Boss"；
2. 那个 NPC 声明了 `ModBossBar` 就走作者逻辑；
3. 把作者的结论**换成参数**再交给原版绘制（`orig`），不自己画条身。

### 2.3 反查当前 Boss

```
候选 = Main.npc 中满足全部条件者：
     active && boss && 声明了 ModBossBar && (int)life == npc.life && (int)lifeMax == npc.lifeMax
候选唯一 → 就是它；0 个或多个 → 不接管，走原版
```

**这是一个可验证但脆弱的判定，必须写清楚**：`BigProgressBarSystem._info` 在游戏源码里是私有字段，但 interop 代理会把它生成出来（发型菜单已用同样方式直读私有字段），所以目标 NPC 能精确拿到。用生命值反查在"同一帧存在两只一模一样血的同名 Boss"时会歧义——那种情况下本管线选择**不接管**（画原版条），而不是猜一只。

### 2.4 参数的落地范围

`BossBarDrawParams` 里能真正起作用的只有 `Life` / `LifeMax` / `Shield` / `ShieldMax` / `IconTexture` / `IconFrame`——它们能原样喂给 `GUIBossBar.Draw`。`BarTexture`、`BarCenter`、`BarColor`、`IconColor`、`IconScale`、`ShowText`、`TextOffset` 这一版**改不动**（条身贴图与位置写死在 `GUIBossBar.Draw` 内部），写进「对作者 API 的影响」，不假装生效。

### 2.5 顺序

`Collected` → `ModifyInfo` → `GetIconTexture` → `PreDraw` → `orig` → `PostDraw`。

`ModifyInfo` 返回 `false` 时**整条都不画**（也不调 `PreDraw` / `PostDraw`），这是"作者要求不画"的唯一表达方式；`PreDraw` 返回 `false` 则不画条但**仍调 `PostDraw`**（作者可能要靠它画自己的东西）。

## 3. 取舍理由

* **为什么把作者结论换成一堆参数交给原版？** 自己画条身要复刻渐变、护盾叠层、文字位置、缩放偏移，一旦和原版不一致就会出现"mod Boss 的血条和原版长得不一样"。参数化改动最小、视觉最一致。
* **为什么用生命值反查而不是下标？** 下标（`npcIndexToAimAt`）是私有字段，本版读不到；生命值是 `GUIBossBar.Draw` 唯一带上来的"这个 NPC 是谁"的线索。歧义时选择不接管，是因为"给错的 Boss 画错的血条"比"这条不接管"更难排查。
* **为什么 `Collected` 在 `ModifyInfo` 之前？** 作者的 `ModifyInfo` 里常常要读 `Life` / `LifeMax` 做判断，先把这一帧的值收进去，读到的才是当前帧而不是上一帧。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 反查不到唯一目标 | 走原版绘制 |
| `ModifyInfo` 抛异常 | 当 `null`（用 NPC 生命），记一次 |
| `ModifyInfo` 返回 false | 这一帧不画这条，不调 `PreDraw`/`PostDraw` |
| `GetIconTexture` 抛异常 / 返回 null | 用游戏传进来的图标 |
| `PreDraw` 抛异常 | 记一次，当 `true`（照画原版） |
| `PreDraw` 返回 false | 不画条身，`PostDraw` 照跑 |
| `PostDraw` 抛异常 | 记一次，忽略 |
| `Main.spriteBatch` 为空 | 不调 `PreDraw`/`PostDraw`，只画条身 |

## 5. 扩展方式

要接"条身自绘"：在 `BossBarPipeline` 里加一条 `On.GUIBossBar.Draw` 之后的自绘分支，用 `BossBarDrawParams.BarTexture` / `BarCenter` 里的值自己画，并跳过 `orig`；判定条件保持"唯一反查"不变。

## 6. 与其它管线的边界

* **NPC 管线**：NPC 类型、`SetDefaults`、`npc.boss` 都归它；本管线只读 `ModNpc.BossBar`。
* **场景效果 / 生物群系**：无关（血条不参与音乐/背景选择）。
* **界面管线**：血条是屏幕 UI，本管线不碰别的 UI 元素；`BossBarDrawParams` 的字段语义沿用 tModLoader。

### 这一版没接

1. **条身绘制参数**：`BarTexture` / `BarCenter` / `BarColor` / `IconColor` / `IconScale` / `ShowText` / `TextOffset` 改不动（见 2.4）。
2. **精确的目标 NPC**：已接（照原版）——管线现在拦在 `BigProgressBarSystem.Update` / `Draw` 上，`_info.npcIndexToAimAt`（public 字段）就是精确序号，不再按生命值反查。
3. **非 `npc.boss` 的 NPC**：只要 `ModNpc.BossBar` 声明了血条就能拿到（参考实现的 `Find` 按"声明了血条 + 在屏幕附近 5000px"找最近的，不要求 `npc.boss`）。
4. **多节 Boss 的子血条**（如虫子的分节）：作者的 `ModifyInfo` 里能改 `info.npcIndexToAimAt`，管线画的时候用的是 `Collect` 存下的那个序号（参考实现的 `Collect` 也这么做）。
5. **`BigProgressBarInfo.validatedAtLeastOnce` / `showText`**：公开字段；本版 `showText` 照原版 从 `self._info.showText` 读，`validatedAtLeastOnce` 不填。
6. **血条的显隐状态机**：归原版 `BigProgressBarSystem`，本版只影响"画出来是什么"。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 新增 | `ModNpc.BossBar`（`ModBossBar?`）：在 `SetDefaults` 里声明这条 NPC 用哪个血条 |
| 本版不生效 | `BossBarDrawParams` 的绘制类字段（见 2.4） |
| 语义明确 | `ModifyInfo` 返回 false = 整条不画；`PreDraw` 返回 false = 只不画条身 |
