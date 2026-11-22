# 内容管线：键位（Keybind）

## 1. 职责

给 mod 一个"按键"入口：`KeybindLoader.RegisterKeybind(mod, name, Keys/string)` 拿到一个可读 `Current` / `JustPressed` / `JustReleased` / `Old` / `RetroCurrent` 的对象。

在手机上，这个"按键"**没有键**：它就是触屏按钮。所以本管线是**适配层**——把键盘语义映射到触屏按钮语义，并补上键盘端才有的判定（聊天/对话/牌子打开时不响应）。

| 环节 | 做什么 |
|---|---|
| `RegisterKeybind(mod, name, Keys)` / `(mod, name, string)` | 转发成一次触屏按钮注册，返回同一个 `ModTouchButton`（声明类型 `ModKeybind`） |
| `ModKeybind` 状态 | `Current` / `JustPressed` / `JustReleased` / `Old` 读触屏按钮记录，读之前按帧号刷一次 |
| 按下派发（`OnPress`） | 触点与程序触发共用一个带门控的按下入口；主菜单中或按钮禁用时不动作 |
| `RetroCurrent` | `Current` 的"不在文本界面里"版本：聊天、NPC 对话、牌子打开时为 false |
| 诊断 | 记录注册数量与来源 mod，便于排查"我的按钮没出现" |

**不接**：键盘实体按键、手柄映射、快捷键冲突检测（手机端根本没有可绑的物理键；真机键盘走游戏的键盘输入通道）。

## 2. 我们的设计

### 2.1 文件

```
Content/
  ModKeybind.cs        作者基类（状态、DisplayName、GetAssignedKeys、RetroCurrent）
  ModTouchButton.cs    触屏按钮（继承 ModKeybind；Icon/Label/Visible/Enabled/OnPress/IsBound）
Pipelines/Keybind/
  KeybindPipeline.cs   模块：注册转发、RetroCurrent 判定、诊断
  KeybindLoader.cs     公开门面：两种 RegisterKeybind 重载
```

### 2.2 一个"按键"就是一个触屏按钮

`RegisterKeybind` 返回 `ModKeybind`，运行时对象是 `ModTouchButton`：

* **没有独立的键位表**。手机端的"绑了什么"由游戏的虚拟控制布局保存，键位管线再存一份只会得到两个真相。
* `defaultKey` 接受但**不生效**：手机端没有可绑的键；保留参数是为了让同一份 mod 代码在两个平台都能编译。
* `Index`、动作编号、控件编号全部沿用触屏按钮那一套（见 `content-touchbutton.md` 2.3）。
* `GetAssignedKeys(InputMode)` 忽略 `mode` 参数：按钮在布局里就给一条（标签），否则空表。

### 2.3 按下派发：一个带门控的统一入口

按下只有一条路——`TouchButtonPipeline.Press`：

```
门控：Main.gameMenu || !Enabled(button) → 直接返回（不置 latch、不回调 OnPress）
通过：latch = true → OnPress(player)（异常记一条，不外抛）
```

* **触点路径**：触点落在 mod 按钮的槽位上时，本管线接管这次触点、**不放行原生触点处理**，把对应控件标成按下后走同一个入口；没落在 mod 按钮上的触点原样交给游戏。
* **程序触发**（内部 API）：同样走这个入口，和手指按在同一条判定里汇合。
* **门控不过时什么都不发生**：`latch` 不置、`OnPress` 不调；`Pressed[control]` 的按下标记仍照置（游戏自己的按下表现读它），但状态机里有同样的 `Enabled` / `gameMenu` 门，`Current` / `JustPressed` 不会变成 true。于是"禁用"在**状态**与**回调**两条路上语义一致：要么都发生，要么都不发生。

### 2.4 `RetroCurrent` 的语义

```
RetroBlocked = Main.drawingPlayerChat || player.talkNPC != -1 || player.sign != -1
RetroCurrent = Current && !RetroBlocked
```

三个条件都是"玩家正在输入文本"的场景。`Current` 保持原义（手指确实按着），`RetroCurrent` 才是作者该用在"开技能/放技能"上的版本。

**为什么不让 `Current` 自己带这个判定？** 因为有的作者要"按住按钮边走边打字"这种体验（例如移动类按钮），把判定塞进 `Current` 就无法表达。

### 2.5 诊断

注册时记一条（`mod/name` + 序号），安装时打印总数。手机端"按钮没出现"最常见的原因是名字拼错导致布局里找不到已保存的位置——日志里给全名，比让作者猜快得多。

## 3. 取舍理由

* **为什么 `ModTouchButton` 继承 `ModKeybind`？** 作者面就是 `ModKeybind`（`RegisterKeybind` 的声明返回类型、`Current` 系列、`DisplayName`）；继承后作者声明 `ModKeybind key` 的代码可以原样编译，`ModTouchButton` 的额外属性（`Icon`/`Label`/`Visible`/`Enabled`/`OnPress`/`IsBound`）也保留在按钮上。
* **状态放在哪？** 状态字段在触屏按钮记录（`TouchButtonEntry`）里，由 `TouchButtonPipeline.Tick` 每帧算一次；`ModKeybind` 的属性只做 `Tick()` + 读取，不重复计算。
* **为什么 `RetroBlocked` 放在管线而不是属性里？** 判定要读 `Main.player[Main.myPlayer]` 的三个字段；集中在一处便于容错（玩家数组未就绪时按未屏蔽处理）。
* **为什么按下要收进统一入口，而不是在触点钩子里直接派发？** 如果派发绕过门控，作者设了 `Enabled = () => false`（例如叠加 UI 打开时禁用技能键）的按钮仍会收到 `OnPress`——读 `Current` 的逻辑看到"没按"，挂 `OnPress` 的逻辑却在放技能，同一个"禁用"在两条路上语义分裂。统一入口让状态与回调共享同一组判定。
* **为什么 mod 槽位上的触点不放行原生处理？** 槽位绑的是 mod 动作号，游戏的原生触点逻辑不认识它，放行只会让原生路径对未知动作多做一次处理；mod 按钮的按下表现由我们写进控制器的 `Pressed` 数组驱动。原生按钮的槽位不受影响，照常走游戏自己的逻辑。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| `mod` 为 null | 抛 `ArgumentNullException`（注册期错误，早失败） |
| `name` 为空 | 抛 `ArgumentException` |
| 同名重复注册 | 返回已有按钮（幂等），不新建 |
| 主菜单中按下（触点或程序触发） | 不置 latch、不回调 `OnPress`；`Current` 仍为 false |
| 按钮 `Enabled` 为 false 时按下 | 同上——按下事件整体不发生 |
| 作者 `Enabled` 抛错 | 记一条，按"可用"处理（门控放行） |
| 作者 `OnPress` 抛错 | 记一条（每按钮一次），latch 已置、状态照常推进 |
| 读 `RetroCurrent` 时玩家数组未就绪 | 视为未屏蔽，返回 `Current` |
| 触屏按钮管线未安装 | 注册仍成功（表在），状态永远 false；日志提示 |

## 5. 扩展方式

真出了可绑定的物理键（外接键盘/手柄），在 `KeybindPipeline` 里加一张"键位 → 动作"表并让 `Current` 取二者之或即可，`ModKeybind` 的公开面不用改。给按下派发加新判定（例如"仅在世界里响应"扩展成更细的条件）时，往统一入口的门控里加一项，触点与程序触发自动共享。

## 6. 与其它管线的边界

* **触屏按钮管线**：本管线是它的上游适配；按钮的注册、编号、状态机、按下派发的机制全在那里，本文档只记键位侧的语义。
* **配置管线**：按键不写进 mod 配置（布局由游戏自己保存）。
* **UI 管线**：按键列表的展示归 UI，本管线只给判定。
