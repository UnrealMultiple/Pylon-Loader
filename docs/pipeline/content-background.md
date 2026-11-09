# 内容管线：背景（Background）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（`Static`） | 表层背景风格的编号（`Slot`）分配 + `SetStaticDefaults` |
| 每帧（本机玩家，场景效果选完赢家之后） | 赢家 `ModSceneEffect.SurfaceBackgroundStyle` → 写 `Main.bgStyle` |
| `Main.DrawBackground` | `Main.bgStyle` 是 mod 槽位时由我们画（用作者自己的贴图），原版分支不跑 |

地下背景也接上了：赢家声明 `UndergroundBackgroundStyle` 就换号，并把 5 层贴图换掉（靠新生成的属性访问器钩子 `Main.get_backTextureValues`）。

**不接**：背景之上的云/星/月亮（归云管线）。地图背景归场景效果管线。`UndergroundBackgroundStyle` 与原版地下风格的优先级比较也只做"赢家声明了就换"。

## 2. 我们的设计

### 2.1 两个文件

`Pipelines/Background/BackgroundTable.cs`（风格表 + 编号）、`Pipelines/Background/BackgroundPipeline.cs`（接管 `DrawBackground` + 每帧写 `Main.bgStyle`）。

作者基类不需要新增成员：`ModSurfaceBackgroundStyle` 上的 `TexturePath`（`ModType` 的既有机制）就是那张背景图。

### 2.2 为什么用自己的贴图，而不是 `TextureAssets.Background` 的槽

契约原本只给 `ChooseFarTexture` / `ChooseMiddleTexture` / `ChooseCloseTexture` 三个"返回贴图编号"的方法，但**编号要指向 `TextureAssets.Background` 里的槽**，而原版槽位是游戏内容：

* 往原版槽位里写 = 覆盖原版背景（不能做）；
* 扩表之后用尾部槽位 = 可以，但每个风格要占几个槽、三个 `Choose*` 分别指哪一层，契约里没有规则。

既然 mod 槽位的背景**本来就由我们画**（见 2.3），最直白的做法是用作者自己的 `Texture` 直接画，不经过游戏那张表——省掉扩表、省掉槽位规则，也不会和原版槽位抢地方。

代价：`Choose*` 返回的编号在本版没有意义。这一版只从 `ChooseCloseTexture` 取它的**四个引用参数**（`scale` / `parallax` / `a` / `b`），返回值不看。

**签名与参考逐行一致**：`ChooseCloseTexture(ref float scale, ref double parallax, ref float a, ref float b)`——`parallax` 是 **double**，不是 float（参考 `BackgroundHooks.DrawClose` 里就是 `double parallax = 0.37`，写错成 float 作者代码搬过来会编译不过）。

### 2.3 三层：远 / 中 / 近

`DrawBackground` 钩子里，mod 槽位走我们自己的绘制，顺序是：

```
远层：ChooseFarTexture() 返回的编号 → TextureAssets.Background[id]，视差 0.12
中层：ChooseMiddleTexture() 返回的编号 → 同上，视差 0.22
近层：作者自己的贴图，scale / parallax 取 ChooseCloseTexture 的引用参数
最前层：PreDrawCloseBackground(spriteBatch) 交给作者自己画
```

远层与中层直接用游戏那张背景表里的贴图（返回 -1 就跳过那一层），所以作者可以复用原版素材拼自己的分层；近层用作者自己的图。

**顺序与参考一致**：近层先问 `PreDrawCloseBackground(spriteBatch)`——false = 这层不画（作者自己画），然后才 `ChooseCloseTexture`、取贴图、平铺。第一版把 PreDraw 放在最后，导致任何 early return（例如贴图尺寸取不到）都会让它不计数，真机测试直接暴露成 "PreDrawCloseBackground 0 次"。

**作者贴图的尺寸**：和资源覆盖层同一个坑——`LoadAsset` 建出来的游戏 `Texture2D.Width/Height` 是 0（读/写都不可靠）。近层与远/中层的平铺都改用 `PylonTexture.Size`（Unity 侧尺寸）算宽度，否则 `width <= 0` 直接跳过，画面不变。

`a` / `b` 那套"高度 = a × 位置 + b"的垂直偏移**已采用**（照原版）：`topY = Step1Top × a + b + scAdj + push`，`Step1Top` 与 `push` 从 `Main.DrawSurfaceBG_BackMountainsStep1` 的参数里存下来。默认值就是游戏自己的 `a = 1800`、`b = 1750`、`scale = 1.25`、`parallax = 0.37`，作者在 `ChooseCloseTexture` 里改引用。

### 2.4 为什么 mod 槽位不调 `orig`

原版 `DrawBackground` 是按 `Main.bgStyle` 的一大串分支画的；我们的槽位它不认识，进去既不画东西、又可能改状态（背景闪光之类）。所以 mod 槽位**完全接管**这一帧的背景绘制。

### 2.5 地下背景：靠属性访问器钩子

地下背景的 5 层贴图在游戏里是私有属性 `backTextureValues`，原先生成的钩子里没有它。这一版把 `OnHookGenerator` 的过滤条件改成"允许 `get_`/`set_` 访问器"，重新生成后才有 `On.Terraria.Main.get_backTextureValues` / `get_oldBackTextureValues` 两个钩子。

管线在这两个钩子里把 5 层贴图换掉：先调作者的 `FillTextureArray(slots)`，编号能落到 `TextureAssets.Background` 就用那张，落不到就整层用作者自己的贴图。

### 2.7 编号

按契约写死的规则：表层从 17 起（`SurfaceBackgroundID` 有 16 个）。地下从 22 起那条这一版不发号——本构建里地下背景查不到可用字段，也不接地下绘制。

## 3. 取舍理由

* **为什么不用游戏那张贴图表？** 见 2.2：槽位规则契约里没有，而背景反正是我们画。
* **为什么只画一层？** 见 2.3：可验证的少做、不可验证的不假装做。
* **为什么不接 `ModifyFarFades`？** 那套淡入是给三层视差用的；只画一层时"淡入"没有意义（要么画要么不画）。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 作者没给贴图（`Texture` 为 null） | 这一帧不画背景（不崩） |
| `ChooseCloseTexture` 抛异常 | 记一次，用默认 scale/parallax 继续画 |
| 贴图资源还没加载好 | 这一帧不画 |
| 赢家没声明背景风格 | 不写 `Main.bgStyle`，原版背景照常 |
| `Main.bgStyle` 落在 mod 槽位之外 | 原样交给 `orig` |

## 5. 扩展方式

接三层：把绘制拆成三次，各自视差与 `a`/`b` 的垂直偏移，需要真机校准；同时要恢复"图层编号"的语义（那时才需要扩 `TextureAssets.Background` 或另立一张表）。接地下背景：先找到地下背景的绘制入口与风格字段（本构建里查不到）。

## 6. 与其它管线的边界

* **场景效果（23）**：选赢家与把 `SurfaceBackgroundStyle` 交出来归它，本管线只消费。
* **水（29）**：同一个赢家，各取各的字段。
* **云（26）**：云是背景之上的一层，归云管线。
* **贴图设施**：本管线不扩表（见 2.2），只用作者自己的资源。

### 这一版没接

1. ~~`a` / `b` 的垂直偏移~~：已用上（照参考实现的 `DrawClose`）。
2. **地下背景的过渡动画**：`oldBackTextureValues` 也换成了作者的图，但过渡时长/插值仍由游戏算，我们只换贴图。
3. **淡入淡出**（`ModifyFarFades`）：已接（见对应小节）。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 无需新增成员 | 背景图用 `TexturePath` 提供（`ModType` 既有机制） |
| 生效 | `ChooseFarTexture` / `ChooseMiddleTexture` 的返回值现在会用（指向游戏背景表的编号）；`PreDrawCloseBackground` 会被调用 |
| 语义明确 | 近层用作者自己的贴图，远/中层用游戏背景表 |
| 语义明确 | `ChooseCloseTexture` 只取 `scale` / `parallax`，返回值不看 |
| 本版生效 | `a`/`b` 垂直偏移、`ModifyFarFades` 淡入淡出、地下背景 |

| 本版不生效 | 云/星/月亮（归云管线） |