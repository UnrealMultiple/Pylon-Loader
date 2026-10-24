# 内容管线：菜单主题（Menu）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（`Static`） | `ModMenu.SetStaticDefaults`（把 `Music` 之类的值定下来） |
| 主题生效 / 失效 | `OnSelected` / `OnDeselected` |
| 标题画面每帧 | `Update(isOnTitleScreen)` |
| 生效期间 | 画 logo / 日月前把作者给的 `Logo` / `SunTexture` / `MoonTexture` **每帧**换进 `TextureAssets`，画完立刻还原（不持久占用游戏的槽） |
| 生效期间 | `Music >= 0` 时写 `Main.newMusic` |
| 设置分类装配完成之后 | 往通用页插一行「Menu Theme:」；行标题、当前主题名与点击行为由本管线提供，点击循环到下一个可用主题 |

**已接**：选择持久化（`PylonLoader.menu.json`，见 2.7）；切换入口有三个——原版设置里的「Menu Theme:」循环行（见 2.6，玩家的日常入口）、作者侧的 `ModMenu.Select()` / `Deselect()`、以及"没存过选择就取最后注册且可用的主题"这条默认解析。

**不接**：标题画面的切换按钮（见 2.7 的失败教训）。logo 绘制前后回调（`PreDrawLogo` / `PostDrawLogo`）已接，但几何参数是 loader 估算的（见 2.5）。

## 2. 我们的设计

### 2.1 三个文件与安装门

`Pipelines/Menu/MenuTable.cs`（主题表 + 可用性判定）、`Pipelines/Menu/MenuPipeline.cs`（换贴图 / 每帧 / 音乐 / 循环切换 / 持久化）、`Pipelines/Menu/MenuSettingsRow.cs`（设置行）。

安装门（`MenuPipeline.Gates`，条件与订阅同居一门，由 GateHub 每轮重评）：

* **Core**：注册了至少一个 `ModMenu` 就挂，常驻——logo 绘制回调 + 设置行（2.6）；
* **Music / Bg / SunMoon**：按覆写分装，只有作者重写了对应 getter（反射判 `get_Music` / `get_MenuBackgroundStyle` / `get_SunTexture`、`get_MoonTexture` 的声明类型）才挂对应的 hook。

### 2.2 每帧换入、画完还原

游戏的标题画面从 `TextureAssets` 的**贴图槽**取图。我们不持久替换这些槽，做法是：

```
DrawLogo / DrawSunAndMoon 进来：把作者贴图写进槽（记录原值）
orig 画完（finally）：把原值写回
```

**移动版的 logo / 太阳是多槽的（真机踩过的坑）**：这套构建里 `TextureAssets` 有 `Logo`~ `Logo6` 和 `Sun`/`Sun2`/`Sun3`，`GUILogo.Draw` 用 `LogoA`/`LogoB` 索引选其中一套画，**不是只读 `Logo`**。第一版只换 `TextureAssets.Logo`，真机上日志显示槽里的 `Asset.Name` 已经是作者贴图，屏幕上却还是原版 logo——换了个寂寞。现在 `SwapIn`/`SwapOut` 把 `Logo`~ `Logo6` 与 `Sun`/`Sun2`/`Sun3` **全部**换入/还原；月亮换 `Moon` 数组的全部月相槽（本构建的 `TextureAssets.Moon` 是 `Il2CppReferenceArray<Asset<Texture2D>>`，逐槽换入即可，不需要动 `moonPhase`）。

**为什么不能持久替换**（第一版踩过的坑）：`Build` 阶段选中主题时就把 `TextureAssets.Logo/Sun/Moon` 永久换成作者贴图，存档里的主题这次没装载或贴图加载失败时，游戏的 logo 槽落在坏值上——真机上表现为**标题画面整屏黑**。每帧换入 + finally 还原后，任何一帧失败都只影响那一帧，游戏的槽永远回到原值。

**日月 hook 按覆写分装，hook 体内再复核一遍**。只有某个主题真的重写了 `SunTexture` / `MoonTexture` 的 getter 才挂 `DrawSunAndMoon`（安装门每轮重评，注册晚的主题下一轮也会被评到）；hook 体内 `_current is null || !_current.OverridesSunMoon || !Main.gameMenu` 就 `orig` 直通，没重写 getter 的主题仍然零开销。

hook 体里换入/换出都包了 `try/catch`：换入失败直接 `orig` 直通（这一帧用游戏原图），换出失败只记日志——**不能把异常从 hook 里漏出去**，否则同一帧后面挂着的托管 hook（包括 logo）会跟着断，真机上表现为"碰一下日月，logo 变回原版"。

**拖动日月不会退回原版**：原版拖动/重拾日月（`Main.alreadyGrabbingSunOrMoon`）最终仍走 `DrawSunAndMoon`，所以按帧换入对拖动路径同样生效；真机实测拖走后贴图仍是作者的。

### 2.4 默认主题

没有选择持久化时，生效的是**最后注册且 `IsAvailable` 的主题**（与文档里写的语义一致）。`IsAvailable` 每帧求值，所以"按日期开放"的写法（只读 `get`）也能生效：一旦它变 false，管线换回上一个可用主题。

### 2.5 logo 的前后回调（接得有保留）

`GUILogo.Draw()` **不带参数**（`refs/dump.cs:27017`，字段只有 `LogoA` / `LogoB` / `LogoT` 三个动画计数器）——原版画 logo 的真实位置、旋转、缩放只存在于它的绘制调用里，托管侧拿不到。

所以回调接上了，但语义有边界：

* `PreDrawLogo(batch, ref center, ref rotation, ref scale, ref color)` 的四个 ref 是 **loader 估算**的值：屏幕水平中心、垂直五分之一处、旋转 0、缩放 1、白色——**不是**原版这一帧的真实几何。
* 返回 false = 这一帧不画原版 logo（`orig` 不调），作者整段自己来；`PostDrawLogo` 无论返回什么都调，收到的就是作者在 Pre 里改过的那组 ref（作者自绘以它为准）。
* 原版 logo 本体的绘制几何由游戏自己决定，本管线不干预——槽换入只换"画的内容"，不改"画在哪、多大、什么角度"。
* 只有主题生效时才介入；无主题时 `orig` 直通。

**为什么不补真实几何**：那要把 `SpriteBatch.Draw` 这一层挂成全局滤网，全游戏的绘制都过一遍，只为了四个数字；估算值 + `PostDrawLogo` 已把「能画、能盖、能 veto」三件事全给了。

### 2.6 设置界面的「菜单主题」行

玩家切主题的入口在**原版设置里**：设置 → 通用，多一行「Menu Theme:」，点一下就循环到下一个可用主题（含原版项）。行本体是游戏自己的 `GUIOpenUIButtonSetting`（`refs/dump.cs:30135`），我们不改任何原版数据表，三步接进来：

1. **插行**：`On.GUISettingsOverlay.SetCategory`（`refs/dump.cs:28628`）在 `orig` 装配完某一页设置列表之后，往 `GUISettingsOverlay._categorySettings` 里插入这一行。只在 主菜单（`Main.gameMenu`）+ 通用分类（`Selected == Category.General`）+ 注册了至少一个主题时插；插入位置是第一个 `_id == GUISettingID.None`（`refs/dump.cs:30528`）的按钮行之后，找不到这样的行就插在最前。行对象只建一次（静态持有；托管包装自带 il2cpp 句柄，对象不会被回收），已在列表里就不重复插——原版每次换页都重装列表，靠这个保持幂等。
2. **文案**：行标题与按钮文案挂在 `GUIOpenUIButtonSetting` 自己的 `GetOption` / `GetButtonCaption` 上（`refs/dump.cs:30158` / `:30155`），hook 里按指针认出是这一行才接手，其余同名行 `orig` 直通。行标题是「Menu Theme:」（葡语文化给「Tema do Menu:」）；按钮文案是当前主题的 `DisplayName`（原版项就是 "Terraria"），用 `GetLayout().Caption` 的布局（`refs/dump.cs:30146`）拿字体与按钮宽度，放不下就逐字截断加「...」。
3. **点击**：这一行的 `OpenUI`（`refs/dump.cs:30170`）改成调本管线的 `Next()`：**原版项在最前、注册序在后**，从当前主题往后找第一个 `IsAvailable` 为真的就切过去，转满一圈必有去处（原版项永远可用）；**先落盘再切换**（回调里再怎么折腾，这次的选择已经写进存档）。

### 2.7 选择持久化

* 落盘文件：`<存档根>/PylonLoader.menu.json`，内容是 `{"theme": "Mod/类名"}`；`Build` 时先读它，读不到或那个主题这次没装就回默认（最后注册且可用的）。
* 作者侧 API：`ModMenu.Select()` / `ModMenu.Deselect()`，切完立刻落盘；设置行的循环（2.6）同样落盘。
* 边界：循环到原版项落盘的是空串键，读档对空串回默认主题——「回到原版」的选择跨启动不保持（会话内一致，重启后主题解析走默认规则）。
* **标题画面不画切换按钮**（第一版试过"底部一行可点文字"，真机上 `SpriteBatch` 状态与字体就绪时机的问题让它在菜单画面反复抛 `NullReferenceException`）——`DrawMenu` 钩子保留空转，将来接真正的 UI 控件时再用。

## 3. 取舍理由

* **为什么每帧换入而不是持久替换？** 见 2.2：持久替换把失败（贴图加载不到、主题没装载）固化进游戏的槽，真机上整屏黑；每帧换入 + finally 还原让失败最多影响一帧。
* **为什么 logo 回调的参数是估的？** 见 2.5：原版 `GUILogo.Draw()` 不带参数，真实几何只在绘制调用里；给出估算值并让作者自绘，比"假装接上但参数是编的"好——文档写明它是估算，作者就知道该自己校。
* **为什么切主题的入口是原版设置里的行？** 玩家的心智是"设置里改设置"；原版的 `GUIOpenUIButtonSetting` 提供现成的行样式、聚焦与点击处理，我们只需要在装配完成后插一行、接管这一行的三段文案与点击，不用自建列表控件，也不用碰标题画面（那里踩过字体时机的坑，见 2.7）。
* **为什么循环要"先落盘再切换"？** 切换回调是作者代码，失败与否不该影响选择是否记住；落盘在前保证回调怎么折腾，这次的选择都已经写进存档。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 作者没给某个贴图 | 那一项保持游戏原值（不写 null） |
| `IsAvailable` 抛异常 | 当作不可用，换下一个 |
| `Update` 抛异常 | 记一次，主题仍然生效 |
| `OnSelected` 抛异常 | 记一次，贴图照换（作者的回调失败不该让标题画面变形） |
| 所有主题都不可用 | 换回游戏原值，清掉"当前选择" |
| 主题贴图是 0 尺寸/空资源 | 每帧换入时 `Value` 为 null 当作"没给"，不写槽 |
| 日月换入抛异常 | 记一次，这一帧走 `orig`（游戏原图），槽不会被改坏 |
| 日月换出抛异常 | 记一次；每帧换入都会重新备份，下一帧照常 |
| 设置行插入失败 | 记一次，这一页设置少一行，游戏其余设置功能不受影响 |
| 行标题 / 按钮文案异常 | 记一次，回退到原文案或原版方法 |
| 循环切换抛异常 | 记一次，选择保持不变 |

## 5. 扩展方式

接更多切换入口：`MenuPipeline.Next()`（循环）与 `SelectAndSave`（指定）都是管线内的现成函数，ImGui 页面或别的 UI 想加切换控件直接调它们。接 logo 真实几何：需要在 logo 的绘制调用层做捕获（当前构建没有这条入口，见 2.5）。设置行文案目前是两条硬编码字符串，接本地化键时换成 `Language.GetText` 即可。

## 6. 与其它管线的边界

* **场景效果 / 背景**：世界的背景与水归它们；本管线只管**标题画面**（菜单背景经 `MenuBackgroundStyle` 走背景管线的绘制链）。
* **音乐**：菜单音乐写 `Main.newMusic`，不占用场景效果的音乐优先级。
* **配置**：主题选择落在自己的小文件（`PylonLoader.menu.json`），不走配置管线——它是单值小存档，不值得拖配置格式的版本兼容约束。
* **资源加载**：作者的 `Logo` 等资源由 `Mod.LoadAsset` 提供，本管线只搬运。
* **设置界面**：本管线只往通用页插一行、接管这一行的文案与点击；设置列表的装配、其余行的行为与存档读写全部是游戏自己的代码。

### 这一版没接

1. **标题画面的切换按钮**：不接（2.7 的失败教训）——玩家入口在原版设置里（2.6）。
2. **logo 的真实几何**：`PreDrawLogo` / `PostDrawLogo` 已接，但参数为估算值（2.5）；真实几何需要绘制捕获层，本版回避。
3. **「回到原版」跨启动保持**：不保持（2.7 的边界）——落盘键为空串，读档回默认主题；会话内循环到原版的行为是一致的。
4. **`IsAvailable` 的轮换动画**：`Next()` 只做跳过与循环，没有过渡动画。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 语义明确 | 主题生效 = 贴图被换进 `TextureAssets`，不是"我们自己在绘制时选图" |
| 语义明确 | `IsAvailable` 每帧求值，可以写成只读 `get`（设置行循环会跳过它为 false 的主题） |
| 本版生效 | 原版设置里的「Menu Theme:」循环行：`DisplayName` 显示在按钮上，点击循环并落盘 |
| 本版生效 | `ModMenu.Select()` / `ModMenu.Deselect()`（作者侧切换，切完落盘） |
| 本版生效 | `PreDrawLogo` / `PostDrawLogo`（几何参数为 loader 估算；ref 改动传给 `PostDrawLogo` 与作者自绘） |
| 本版不生效 | 标题画面切换按钮（持久化与设置行已生效） |
