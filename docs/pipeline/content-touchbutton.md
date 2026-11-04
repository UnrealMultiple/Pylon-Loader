# 内容管线：触屏按钮（TouchButton）

## 1. 职责

让 mod 拥有一个**属于游戏虚拟控制器**的按钮：玩家在游戏自己的「按键布局」编辑器里放置它，mod 把它当 tModLoader 的 `ModKeybind` 一样读（`Current` / `JustPressed` / `JustReleased` / `Old` / `IsBound`）。

| 环节 | 做什么 |
|---|---|
| 注册（内容阶段） | 发一个注册序号；序号决定它的**动作编号**与**控件编号** |
| 布局注入（`TouchButtonLayouts.Apply`） | 把 mod 按钮插进当前虚拟控制布局，玩家可拖动/换位置 |
| 布局回收（`TouchButtonLayouts.Capture`） | 玩家改完布局后把位置存回来 |
| 每帧（`GUIVirtualInputController.Update`） | 读 `Pressed` 数组，算 `current/old/justPressed/justReleased` |
| 绘制与提示 | 图标（编辑器槽位 + 快捷动作条）、标签、可见性、可用性（作者函数回答） |

**不接**：新增控件类型（控件编号借用游戏预留的尾部空间，见 2.3）、按钮原生动画、多指快捷键冲突检测。

## 2. 我们的设计

### 2.1 文件

```
Pipelines/TouchButton/
  TouchButtonPipeline.cs    模块：注册发号、状态机、游戏侧接入（9 个钩子）、纹理与标签
  TouchButtonTable.cs       注册表与每按钮状态（密集 struct 数组，ref 取用）
  TouchButtonLayouts.cs     布局的注入/回收/剥离
  TouchButtonEntry.cs       公开：一条注册记录
  TouchButtonOptions.cs     公开：注册选项
  TouchButtonLoader.cs      公开门面：作者与屏幕用的静态入口
  TouchButtonHooks.cs       公开门面：UI 启动流程用的 Install 入口
```

### 2.2 状态：密集 struct 数组

按钮数量是几十级，但状态更新是**每帧每按钮**，所以状态按序号密集存放：

```csharp
private struct ButtonState
{
    public bool Current, Old, JustPressed, JustReleased, Latch;
}
```

`Tick` 里 `ref ButtonState state = ref _state[i]`，一次取用改完四个字段；公开的 `TouchButtonEntry` 只保留元数据（名字、函数、纹理缓存），不再逐帧被写。这样每帧的写操作集中在连续内存里，公开对象变成只读资产。

### 2.3 编号：三段区间

* **动作编号**：`FirstAction + index`——这是游戏布局里"这个位置绑了什么动作"的键。
* **快捷栏编号**：`QuickBase + index`——快捷动作按钮那套的独立空间。
* **控件编号**：`FirstControl + index`——`GUIVirtualInputController.Pressed` 的下标。

三段都是"基数 + 序号"，反查一减即可，不需要任何字典。

### 2.4 每帧状态机

```
held    = pressed[control]
current = !Main.gameMenu && (latch || held) && Enabled(button)
JustReleased = !current && Old
JustPressed  = current && !Old
Latch = false; Old = current; Current = current
```

三条不变量：

1. **门控靠 `Main.GameUpdateCount`**：作者在任意位置读 `Current` 都只触发一次实际计算，不会因为一帧里被读十次而跑十遍作者函数。
2. **`latch` 只活一帧**：算完立刻清，所以"程序触发"不会变成永久按住。
3. **`Enabled` 每帧问一次**：作者的 `Enabled` 通常是纯查表，但也不能替它缓存——它可能依赖当前装备。

### 2.5 布局：侧车文件，不动游戏的存档格式

游戏会保存"哪个槽位绑了哪个动作"，但它不认识 mod 的动作编号，直接存下去换个设备就读不懂。所以我们另存一份侧车：

```
{SavePath}/PylonLoader.touchbuttons.json
{ version: 1, profiles: { <配置文件 id>: [ { slot, index, action, group: [...] } ] } }
```

* `Capture`（游戏保存布局之前）：扫当前布局，把**我们的动作所在槽位**记成 `mod/Name` 这样的键，写侧车；
* `Apply`（游戏加载布局之后）：按槽位键找回绑定，**只在原来的值是"未绑定"时才写**——玩家自己改过的位置优先；
* `BeginStrip` / `EndStrip`（保存与导出分享码期间）：把我们的动作临时换成"未绑定"，跑完立刻换回来。这样存进游戏存档与分享码里的都是干净数据。

位置按 `mod/Name` 存，名字改了位置就丢——所以 `FullName` 的注释写死了"别改名"。

### 2.6 实际挂钩（10 个）

| 位置 | 做什么 |
|---|---|
| `GUIVirtualInputController.Update` | 扩容控制器数组 + 推进状态机 |
| `GUIVirtualInputController.Draw` | 扩容（画之前必须保证数组够长） |
| `HandleTouchCursorDown` | 触点落在我们槽位 → 置 `Pressed[control]`、latch、调 `OnPress` |
| `DetermineButtonVisible` | 作者 `Visible` 说不可见就不显示 |
| `GUIVirtualInputControllerEdit.GetControlName` | 编辑器里显示作者的标签 |
| `...Edit.GetControlTextureFrame` | 编辑器里显示作者的图标 |
| `...Edit.Draw` | 进编辑器前确保数组就绪 |
| `VirtualControlsStyles_Layout.SaveConfiguration` | `Capture` + 剥离，再让游戏保存 |
| `...LoadConfiguration` | 让游戏加载完，再 `Apply` |
| `...GetShareCode` | 导出分享码期间剥离 |
| `GUIQuickActionButton.GetIconFrame` | 快捷动作条上属于 mod 的号（`QuickBase + index`）换成作者图标 |

**快捷动作条的图标已接**：`GUIQuickActionButton.GetIconFrame` 里遇到 mod 快捷号就把作者贴图与整图矩形交出去（`On.GUIQuickActionButton.GetIconFrame` 在托管侧可挂，签名见 `interop/generated/on-signatures.txt`），**只换图标不换外框**——按钮外框与按下动画仍是游戏那一套，作者不用重画。同一族的 `Draw` / `DrawFrame` / `DrawAtPosition` 不动：图标由 `GetIconFrame` 提供后原版绘制自己会取。

**`VirtualControllerInputState` 的默认/重映射动作表**：**已接（2026-11）**——interop 代理把私有静态字段暴露成公开属性，`DefaultMode` / `RemappedMode` 直写、实例的 `RemappedBindings` 是公开字段；`Tick` 时把 mod 动作段补成自己的号并调 `RefreshAlternativeModeMappings`，游戏自己的重映射路径就把 mod 按钮当一等公民。之前判「私有字段接不了」是错的——代理层能写（参考实现 `TouchButtonHooks.Statics`/`InputState` 就是这么做的）。小组（`GroupActions`）的细分按下状态仍不接，见下面 2026-11 复核。

**2026-11 复核 `GroupActions`**：`GUIVirtualInputController` 与 `VirtualInputController_Layout` 里**没有"这一组现在按在第几格"的状态字段**，`GroupActions` 只是布局里"这一组包含哪些动作"的数组；按下与否是每帧按各动作的 `Pressed` 现算的（我们的 `PressedOf` 就是这么判的）。所以这一条不是"读不到私有状态"，是**没有状态可读**——作者要判组内哪格按着，直接读 `controller.Pressed[(int)controlType]`。

## 3. 取舍理由

* **为什么不新建控件类型？** 控件类型枚举由游戏与内核共同维护，加一个就要改内核；而"布局里能绑的动作"本来就是开放集合。合成动作编号是唯一不需要改内核的做法。
* **为什么状态要搬出公开对象？** `TouchButtonEntry` 是作者能拿到的对象，把它当状态容器等于让每帧写操作散落在堆上；状态进 struct 数组后，公开对象只装"注册时定死的东西"，读写边界清楚。
* **为什么 `Tick` 要能被属性调用？** `ModTouchButton.Current` 的语义是"读之前先刷新"；如果只靠 `Update` 钩子，作者在 `Update` 之前读会拿到上一帧的值，而这种差异极难查。
* **为什么 `Press` 只置 latch、不直接调 `OnPress`？** 直接调会让"程序触发"跳过 `Enabled` 与 `gameMenu` 检查，和手指按的行为分叉。latch 让两条路径汇合在同一个判定里。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| 作者 `Icon` 函数抛错 | 记一条，该按钮无图标（不崩） |
| 作者 `Label` 抛错 | 记一条，退回本地化显示名 |
| 作者 `Visible` / `Enabled` 抛错 | 记一条，按"可见 / 可用"处理 |
| 作者 `OnPress` 抛错 | 记一条，状态照常推进 |
| 纹理路径找不到 | 退回 `icon_small`/`icon`，都没有就无图标 |
| 布局里有未知动作编号 | 忽略这一项，不影响其它控件 |
| `Tick` 里取不到控制器 | 视为"没有按下"，但 `latch` 仍然生效 |

## 5. 扩展方式

新增一个作者可用的读取项（例如 `HeldTime`）：在 `ButtonState` 里加一个字段、在 `Tick` 里推进、公开属性直接读——不需要改任何编号或布局逻辑。新增一种选项（例如"按住重复触发"）：`TouchButtonOptions` 加字段、管线里读它。

## 6. 与其它管线的边界

* **键位管线**：`ModKeybind` 是键盘/手柄的动作，触屏按钮是它的触屏对偶；`Label`、`IsBound` 的语义要与键位管线对齐（同一个按钮既可能被键盘绑，也可能被触屏绑）。
* **UI 管线**：`ModUI` 里的虚拟控制皮肤会读本管线的按钮列表；皮肤不负责状态。
* **网络管线**：按钮状态是纯本地的，从不同步。
* **配置管线**：按钮布局由游戏自己保存，不进 mod 配置。
