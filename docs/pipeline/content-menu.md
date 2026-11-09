# 内容管线：菜单主题（Menu）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（`Static`） | `ModMenu.SetStaticDefaults`（把 `Music` 之类的值定下来） |
| 主题生效 / 失效 | `OnSelected` / `OnDeselected` |
| 标题画面每帧 | `Update(isOnTitleScreen)` |
| 生效期间 | 画 logo / 日月前把作者给的 `Logo` / `SunTexture` / `MoonTexture` **每帧**换进 `TextureAssets`，画完立刻还原（不持久占用游戏的槽） |
| 生效期间 | `Music >= 0` 时写 `Main.newMusic` |

**已接**：选择持久化（`PylonLoader.menu.json`，见 2.7；切换入口是作者侧的 `ModMenu.Select()` / `Deselect()`，参考实现同样没有标题画面按钮）。

**不接**：`DisplayName` 在游戏原版设置界面里的展示。logo 绘制前后回调（`PreDrawLogo` / `PostDrawLogo`）已接，但几何参数是 loader 估算的（见对应小节）。

## 2. 我们的设计

### 2.1 两个文件

`Pipelines/Menu/MenuTable.cs`（主题表 + 当前选择）、`Pipelines/Menu/MenuPipeline.cs`（换贴图 / 每帧 / 音乐）。

### 2.2 每帧换入、画完还原（与参考一致）

游戏的标题画面从 `TextureAssets` 的**贴图槽**取图。参考实现**从不持久替换**这些槽（日月按帧换入换出；logo 靠绘制捕获重放，我们不建这套设施），我们的等价做法：

```
DrawLogo / DrawSunAndMoon 进来：把作者贴图写进槽（记录原值）
orig 画完（finally）：把原值写回
```

**移动版的 logo / 太阳是多槽的（真机踩过的坑）**：这套构建里 `TextureAssets` 有 `Logo`~ `Logo6` 和 `Sun`/`Sun2`/`Sun3`，`GUILogo.Draw` 用 `LogoA`/`LogoB` 索引选其中一套画，**不是只读 `Logo`**。第一版只换 `TextureAssets.Logo`，真机上日志显示槽里的 `Asset.Name` 已经是作者贴图，屏幕上却还是原版 logo——换了个寂寞。现在 `SwapIn`/`SwapOut` 把 `Logo`~ `Logo6` 与 `Sun`/`Sun2`/`Sun3` **全部**换入/还原；月亮照旧换 `Moon` 数组的全部月相槽（本构建的 `TextureAssets.Moon` 是 `Il2CppReferenceArray<Asset<Texture2D>>`，不是参考那边的 `GameObject` 数组，所以不需要参考的 `new GameObject(...)` + `moonPhase = 0` 写法）。

**为什么不能持久替换**（第一版踩过的坑）：`Build` 阶段选中主题时就把 `TextureAssets.Logo/Sun/Moon` 永久换成作者贴图，存档里的主题这次没装载或贴图加载失败时，游戏的 logo 槽落在坏值上——真机上表现为**标题画面整屏黑**。每帧换入 + finally 还原后，任何一帧失败都只影响那一帧，游戏的槽永远回到原值。

**日月 hook 无条件挂载，hook 体内按 `OverridesSunMoon` 判断要不要换**。参考是在每个 `ModMenu` 类型注册时（反射判 `get_SunTexture`/`get_MoonTexture` 的声明类型）才挂；我们的 `Install()` 跑在 `Build()`/`MenuTable.Rebuild()` 之前，那时 `MenuTable` 还是空的，按注册内容判断永远判不到——真机上表现为"logo 换了、日月没换"，日志的 `已挂（…）` 里也没有"日月"。现在无条件挂，进 hook 后 `_current is null || !_current.OverridesSunMoon || !Main.gameMenu` 就 `orig` 直通，没重写 getter 的主题仍然零开销。

hook 体里换入/换出都包了 `try/catch`：换入失败直接 `orig` 直通（这一帧用游戏原图），换出失败只记日志——**不能把异常从 hook 里漏出去**，否则同一帧后面挂着的托管 hook（包括 logo）会跟着断，真机上表现为"碰一下日月，logo 变回原版"。

**拖动日月不会退回原版**：原版拖动/重拾日月（`Main.alreadyGrabbingSunOrMoon`）最终仍走 `DrawSunAndMoon`，所以按帧换入对拖动路径同样生效；真机实测拖走后贴图仍是作者的。

### 2.4 默认主题

没有选择持久化时，生效的是**最后注册且 `IsAvailable` 的主题**（与文档里写的语义一致）。`IsAvailable` 每帧求值，所以"按日期开放"的写法（只读 `get`）也能生效：一旦它变 false，管线换回上一个可用主题。

### 2.5b logo 的前后回调（接得有保留）

`GUILogo.Draw()` **不带参数**（`refs/dump.cs:27007`，字段只有 `LogoA` / `LogoB` / `LogoT` 三个动画计数器）——原版画 logo 的真实位置、旋转、缩放只存在于它的绘制调用里，托管侧拿不到（参考实现是靠一个**原生绘制捕获**把每次 `Draw` 调用的参数截下来的，我们不建这套设施）。

所以回调接上了，但语义有保留：

* `PreDrawLogo(batch, ref center, ref rotation, ref scale, ref color)` 的四个 ref 是 **loader 估算**的值：屏幕水平中心、垂直五分之一处、旋转 0、缩放 1、白色——**不是**原版这一帧的真实几何；作者要精确对齐就在回调里改这些 ref，或干脆自己画。
* 返回 false = 这一帧不画原版 logo（`orig` 不调），作者整段自己来；`PostDrawLogo` 无论返回什么都调，收到的也是同一组 ref（作者在 Pre 里改过的值会带过来）。
* 只有主题生效时才介入；无主题时 `orig` 直通。

**为什么不学参考那样捕获**：捕获要挂 `SpriteBatch.Draw` 这一层（原生或每帧过滤全部绘制调用），代价是全游戏的绘制都过一遍滤网，只为了四个数字；估算值 + 作者可改的 ref 已经把「能画、能盖、能 veto」三件事全给了。

### 2.5 音乐

`Music >= 0` 时在标题画面里写 `Main.newMusic`。只写在标题画面（`menuMode == 0`），进世界之后不写——否则会把玩家进世界时的音乐顶掉。

### 2.7 选择持久化

* 落盘文件：`<存档根>/PylonLoader.menu.json`，内容是 `{"theme": "Mod/类名"}`；`Build` 时先读它，读不到或那个主题这次没装就回默认（最后注册且可用的）。
* 作者侧 API：`ModMenu.Select()` / `ModMenu.Deselect()`，切完立刻落盘。
* **标题画面不画切换按钮**（第一版试过"底部一行可点文字"，真机上 `SpriteBatch` 状态与字体就绪时机的问题让它在菜单画面反复抛 `NullReferenceException`；参考实现也没有这个按钮，切换靠作者代码/设置界面）——`DrawMenu` 钩子保留空转，将来接真正的 UI 控件时再用。

## 3. 取舍理由

* **为什么每帧换入而不是持久替换？** 见 2.2：持久替换把失败（贴图加载不到、主题没装载）固化进游戏的槽，真机上整屏黑；每帧换入 + finally 还原让失败最多影响一帧。
* **为什么 logo 回调的参数是估的？** 见 2.5b：原版 `GUILogo.Draw()` 不带参数，真实几何只在绘制调用里；给出估算值并让作者可改，比"假装接上但参数是编的"好——文档写明它是估算，作者就知道该自己校。
* **为什么主题选择只有默认值？** 切换按钮属于界面侧（要画按钮、要读写设置、要持久化），本版先保证"装了就生效、且能正确换回"。

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

## 5. 扩展方式

接切换按钮：在标题画面的 UI 入口加一个按钮，把 `MenuTable` 里的主题名列表喂给它，点选后调本管线的选择函数并落设置。接 logo 回调：需要在 logo 的绘制入口拿到中心/旋转/缩放/颜色（当前构建里没有这条入口）。

## 6. 与其它管线的边界

* **场景效果 / 背景**：世界的背景与水归它们；本管线只管**标题画面**。
* **音乐**：菜单音乐写 `Main.newMusic`，不占用场景效果的音乐优先级。
* **配置**：主题选择将来要落进设置文件（配置管线），本版不落盘。
* **资源加载**：作者的 `Logo` 等资源由 `Mod.LoadAsset` 提供，本管线只搬运。

### 这一版没接

1. ~~**`PreDrawLogo` / `PostDrawLogo`**~~：**已接（参数为估算值，见 2.5b）**。原版几何拿不到是事实，但回调的"能 veto / 能加盖"两件事是真给了。
2. ~~**选择持久化**~~：**已接**（见 2.7）——落盘 `PylonLoader.menu.json`；作者侧 `ModMenu.Select()` / `ModMenu.Deselect()`。标题画面按钮**不接**（见 2.7 的原因）。
3. **`DisplayName` 的界面展示**：切换行用的是 `DisplayName`（没写就用类名），但没有做游戏原版设置界面里的条目。
4. **`IsAvailable` 的轮换逻辑**：本版只把它当"能不能用"读，不做轮换/回退动画。
5. ~~**菜单背景风格**~~：**已接**——`ModMenu.MenuBackgroundStyle`（返回 `ModSurfaceBackgroundStyle`）在菜单里由 `Main.UpdateBGVisibility_BackLayer` 写 `Main.bgStyle`（与场景效果同一条路），离开菜单或主题没声明背景时换回接管前的值。之前判「查无」是错的：`ModSurfaceBackgroundStyle` 是加载器侧类型，不是游戏类型。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 语义明确 | 主题生效 = 贴图被换进 `TextureAssets`，不是"我们自己在绘制时选图" |
| 语义明确 | `IsAvailable` 每帧求值，可以写成只读 `get` |
| 本版生效 | `PreDrawLogo` / `PostDrawLogo`（几何参数为 loader 估算，可改） |
| 本版不生效 | `DisplayName`（无切换界面）；标题画面切换按钮（持久化已生效） |