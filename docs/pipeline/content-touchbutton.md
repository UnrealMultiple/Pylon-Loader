# 内容管线：触屏按钮（TouchButton）

## 1. 职责

让 mod 拥有一个**属于游戏虚拟控制器**的按钮：玩家在游戏自己的「按键布局」编辑器里放置它，mod 把它当 tModLoader 的 `ModKeybind` 一样读（`Current` / `JustPressed` / `JustReleased` / `Old` / `IsBound`）。

| 环节 | 做什么 |
|---|---|
| 注册（内容阶段） | 发一个注册序号；序号决定它的**动作编号**与**控件编号** |
| 布局注入（`TouchButtonLayouts.Apply`） | 把 mod 按钮插进当前虚拟控制布局，玩家可拖动/换位置 |
| 布局回收（`TouchButtonLayouts.Capture`） | 玩家改完布局后把位置存回来 |
| 触点按下（`HandleTouchCursorDown`） | 反查槽位归属；接管 mod 按钮的触点，按压前沿派发按下事件 |
| 每帧（`GUIVirtualInputController.Update`） | 扩控制器数组、同步动作表、推进状态机 |
| 绘制与提示 | 图标（编辑器槽位 + 快捷动作条）、标签、可见性、可用性（作者函数回答） |

**不接**：新增控件类型（控件编号借用游戏预留的尾部空间，见 2.3）、按钮原生动画、多指快捷键冲突检测。

## 2. 我们的设计

### 2.1 文件

```
Pipelines/TouchButton/
  TouchButtonPipeline.cs    模块：注册发号、状态机、游戏侧接入（11 个钩子）、纹理与标签
  TouchButtonTable.cs       注册表与每按钮状态（密集 struct 数组，ref 取用）
  TouchButtonLayouts.cs     布局的注入/回收/剥离
  TouchButtonEntry.cs       注册记录（框架内部；管线与 ModTouchButton 共用）
  TouchButtonLoader.cs      公开门面：RegisterButton / Buttons / Count
  TouchButtonHooks.cs       公开门面：UI 启动流程用的安装入口
```

作者面的选项不走独立 Options 类型：`ModTouchButton` 上的 Icon/Label/Visible/Enabled/OnPress
直接写进这条注册记录，KeybindLoader 的重载也落到同一个 Register。

### 2.2 状态：密集 struct 数组

按钮数量是几十级，但状态更新是**每帧每按钮**，所以状态按序号密集存放：

```csharp
private struct ButtonState
{
    public bool Current, Old, JustPressed, JustReleased, Latch;
}
```

`Tick` 里 `ref ButtonState state = ref _state[i]`，一次取用改完几个字段；公开的 `TouchButtonEntry` 只保留元数据（名字、函数、纹理缓存），不再逐帧被写。这样每帧的写操作集中在连续内存里，公开对象变成只读资产。

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

`held` 直接读控制器自己的 `Pressed[control]`：手指按住期间游戏把它置真、抬手清掉，我们不改它的复位时机，状态机只搭便车。

### 2.5 布局：侧车文件，不动游戏的存档格式

游戏会保存"哪个槽位绑了哪个动作"，但它不认识 mod 的动作编号，直接存下去换个设备就读不懂。所以我们另存一份侧车：

```
{SavePath}/PylonLoader.touchbuttons.json
{ version: 1, profiles: { <配置文件 id>: [ { slot, index, action, group: [...] } ] } }
```

* `Capture`（游戏保存布局之前）：扫当前布局，把**我们的动作所在槽位**记成 `mod/Name` 这样的键，写侧车；
* `Apply`（游戏加载布局之后）：按槽位键找回绑定，**只在原来的值是"未绑定"时才写**——玩家自己改过的位置优先；
* `BeginStrip` / `EndStrip`（保存与导出分享码期间）：把我们的动作临时换成"未绑定"，跑完立刻换回来。这样存进游戏存档与分享码里的都是干净数据。

### 2.6 实际挂钩（11 个）

| 位置 | 做什么 |
|---|---|
| `GUIVirtualInputController.Update` | 扩控制器数组 + 推进状态机 + 同步动作表 |
| `GUIVirtualInputController.Draw` | 扩容（画之前必须保证数组够长） |
| `HandleTouchCursorDown` | 触点落在我们按钮上：接管、不放行原生；置 `Pressed[control]`，**按压前沿**（`WasPressed[control]` 为假）才经 `Press` 派发 |
| `DetermineButtonVisible` | 作者 `Visible` 说不可见就不显示 |
| `GUIVirtualInputControllerEdit.GetControlName` | 编辑器里显示作者的标签 |
| `...Edit.GetControlTextureFrame` | 编辑器里显示作者的图标 |
| `...Edit.Draw` | 进编辑器前确保数组就绪（平时 60 帧一次） |
| `VirtualControlsStyles_Layout.SaveConfiguration` | `Capture` + 剥离，再让游戏保存 |
| `...LoadConfiguration` | 让游戏加载完，再扩容 + `Apply` |
| `...GetShareCode` | 导出分享码期间剥离 |
| `GUIQuickActionButton.GetIconFrame` | 快捷动作条上属于 mod 的号（`QuickBase + index`）换成作者图标 |

**触点按下路径**：先按当前布局反查槽位（组槽取组内第一个 mod 动作）；不是我们的按钮就把触点交还原生。是我们的就接管——扩好控制器数组后置 `Pressed[control]`，再判前沿：控制器自己的 `WasPressed[control]` 在按住期间为真、抬手才清，所以只有第一次落下的触点走 `Press`，同一按住周期内后续触点（第二根手指、重复 down 事件）只置按下标记。去重的周期是"一次按压"而不是"一帧"，所以判据用游戏的 `WasPressed` 而不是我们每帧就清的 `Latch`。

**快捷动作条的图标已接**：`GUIQuickActionButton.GetIconFrame` 里遇到 mod 快捷号就把作者贴图与整图矩形交出去（`On.GUIQuickActionButton.GetIconFrame` 在托管侧可挂，签名见 `interop/generated/on-signatures.txt`），**只换图标不换外框**——按钮外框与按下动画仍是游戏那一套，作者不用重画。同一族的 `Draw` / `DrawFrame` / `DrawAtPosition` 不动：图标由 `GetIconFrame` 提供后原版绘制自己会取。

**`VirtualControllerInputState` 的默认/重映射动作表**：已接——interop 代理把私有静态字段暴露成公开属性，`DefaultMode` / `RemappedMode` 直写、实例的 `RemappedBindings` 是公开字段；`Tick` 时把 mod 动作段补成自己的号并调 `RefreshAlternativeModeMappings`，游戏自己的重映射路径就把 mod 按钮当一等公民（只在数量变化时写，平时每帧早退）。

**小组（`GroupActions`）**：组槽的触点反查取组内第一个 mod 动作，按下即按它派发；组内"按在第几格"的细分选中状态（游戏记在控制器的组状态里）暂未接入。

**安装时机**：注册只入表（按 `mod/Name` 幂等，重复注册返回已有对象）；钩子安装与首次布局套用由 UI 启动流程驱动——`ModUI.EnsureInitialized` 登记安装请求，之后每帧 `TryInstallPending`，等 `GUIInstance.Active` 就绪的帧挂齐钩子并 `Apply` 保存的布局。挂钩之前到来的 `LoadConfiguration` 会跳过套用，由 `TryInstallPending` 里的 `Apply` 兜底。

## 3. 取舍理由

* **为什么不新建控件类型？** 控件类型枚举由游戏与内核共同维护，加一个就要改内核；而"布局里能绑的动作"本来就是开放集合。合成动作编号是唯一不需要改内核的做法。
* **为什么状态要搬出公开对象？** `TouchButtonEntry` 是作者能拿到的对象，把它当状态容器等于让每帧写操作散落在堆上；状态进 struct 数组后，公开对象只装"注册时定死的东西"，读写边界清楚。
* **为什么 `Tick` 要能被属性调用？** `ModTouchButton.Current` 的语义是"读之前先刷新"；如果只靠 `Update` 钩子，作者在 `Update` 之前读会拿到上一帧的值，而这种差异极难查。
* **为什么按下统一走 `Press`？** 主菜单中不派发、禁用按钮不派发——这两个门只写在 `Press` 一处，触点与程序触发走同一判定，行为不分叉：被门挡下时 latch 与 OnPress 都不发生，"禁用"在状态与回调两条路上语义一致。
* **为什么触点派发只认按压前沿？** 一次按住期间可能落进多根手指、也可能收到重复的 down 事件；不去重的话，挂在 OnPress 上的冷却/扣资源/开关切换会被重复执行。前沿判据用控制器自己的 `WasPressed`（按住期间为真、抬手才清），不放在我们每帧就清的状态机里——去重的周期是"一次按压"，不是"一帧"。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| 作者 `Icon` 函数抛错 | 记一条，该按钮无图标（不崩） |
| 作者 `Label` 抛错 | 记一条，退回本地化显示名 |
| 作者 `Visible` / `Enabled` 抛错 | 记一条，按"可见 / 可用"处理 |
| 作者 `OnPress` 抛错 | 记一条，状态照常推进 |
| 纹理路径找不到 | 退回 `icon_small`/`icon`，都没有就无图标 |
| 布局里有未知动作编号 | 忽略这一项，不影响其它控件 |
| 触点槽位反查抛错 | 记一条，这次触点交还原生处理 |
| 触点按下处理抛错 | 记一条（按按钮去重），本次不派发 OnPress |
| 控制器数组扩容失败 | 记一条（去重），下一次触点/帧再试 |
| `Tick` 里取不到控制器 | 视为"没有按下"，但 `latch` 仍然生效 |

## 5. 扩展方式

新增一个作者可用的读取项（例如 `HeldTime`）：在 `ButtonState` 里加一个字段、在 `Tick` 里推进、公开属性直接读——不需要改任何编号或布局逻辑。新增一种按钮行为：`ModTouchButton` 加属性、写进 `TouchButtonEntry` 的对应槽、管线里读它。

## 6. 与其它管线的边界

* **键位管线**：`ModKeybind` 是键盘/手柄的动作，触屏按钮是它的触屏对偶；`KeybindLoader.RegisterKeybind` 也落成本管线的按钮。`Label`、`IsBound` 的语义要与键位管线对齐（同一个按钮既可能被键盘绑，也可能被触屏绑）。
* **UI 管线**：`ModUI` 的启动流程驱动本管线的钩子安装时机（首批按钮注册且游戏 UI 就绪后才挂，见 2.6）；ModUI 里的虚拟控制皮肤会读本管线的按钮列表，皮肤不负责状态。
* **网络管线**：按钮状态是纯本地的，从不同步。
* **配置管线**：按钮布局由游戏自己保存（侧车只记我们的部分，见 2.5），不进 mod 配置。
