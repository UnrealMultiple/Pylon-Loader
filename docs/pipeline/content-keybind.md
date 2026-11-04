# 内容管线：键位（Keybind）

## 1. 职责

给 mod 一个"按键"入口：`Mod.RegisterKeybind` / `KeybindLoader.RegisterKeybind` 拿到一个可读 `Current` / `JustPressed` / `JustReleased` / `Old` 的对象。

在手机上，这个"按键"**没有键**：它就是触屏按钮。所以本管线是**适配层**——把键盘语义映射到触屏按钮语义，并补上键盘端才有的判定（聊天/对话/牌子打开时不响应）。

| 环节 | 做什么 |
|---|---|
| `RegisterKeybind(mod, name, defaultKey)` | 转发成一次触屏按钮注册，返回同一个 `ModTouchButton` |
| `RetroCurrent` | `Current` 的"不在文本界面里"版本：聊天、NPC 对话、牌子打开时为 false |
| 诊断 | 记录注册数量与来源 mod，便于排查"我的按钮没出现" |

**不接**：键盘实体按键、手柄映射、快捷键冲突检测（手机端根本没有可绑的物理键；真机键盘走游戏的键盘输入通道）。

## 2. 我们的设计

### 2.1 文件

```
Pipelines/Keybind/
  KeybindPipeline.cs   模块：注册转发、RetroCurrent 判定、诊断
  KeybindLoader.cs     公开门面（作者已编译的静态 API，签名不变）
```

### 2.2 一个"按键"就是一个触屏按钮

`RegisterKeybind(mod, name, defaultKey)` 直接返回触屏按钮注册的结果：

* **没有独立的键位表**。手机端的"绑了什么"由游戏的虚拟控制布局保存，键位管线再存一份只会得到两个真相。
* `defaultKey` 接受但**不生效**：手机端没有可绑的键；保留参数是为了让同一份 mod 代码在两个平台都能编译。
* `Index`、动作编号、控件编号全部沿用触屏按钮那一套（见 `content-touchbutton.md` 3.3）。

### 2.3 `RetroCurrent` 的语义

```
RetroBlocked = Main.drawingPlayerChat || player.talkNPC != -1 || player.sign != -1
RetroCurrent = Current && !RetroBlocked
```

三个条件都是"玩家正在输入文本"的场景。`Current` 保持原义（手指确实按着），`RetroCurrent` 才是作者该用在"开技能/放技能"上的版本。

**为什么不让 `Current` 自己带这个判定？** 因为有的作者要"按住按钮边走边打字"这种体验（例如移动类按钮），把判定塞进 `Current` 就无法表达。

### 2.4 诊断

注册时记一条（`mod/name` + 序号），安装时打印总数。手机端"按钮没出现"最常见的原因是名字拼错导致布局里找不到已保存的位置——日志里给全名，比让作者猜快得多。

## 3. 取舍理由

* **为什么不照原版 做 `ModKeybind` 基类？** 我们的 `ModTouchButton` 已经是公开类型且不继承任何东西；为了一个基类去改它的继承链，会让已编译的 mod 出现 `MissingMethodException`（`new Mod`/`new string Name` 那套隐藏成员尤其脆）。**用组合而不是继承**：`ModTouchButton` 自己持有状态，键位管线只提供判定。
* **为什么 `RetroBlocked` 放在管线而不是属性里？** 判定要读 `Main.player[Main.myPlayer]` 的三个字段；放在属性里会让每个按钮的每次读取都重复一遍，而这是每帧路径。
* **为什么保留 `KeybindLoader` 这个静态门面？** 它是作者代码里已经写下的符号；改名或改成实例方法都会让旧 mod 编译/运行失败。门面只转发，状态不在里面。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| `mod` 为 null | 抛 `ArgumentNullException`（注册期错误，早失败） |
| `name` 为空 | 抛 `ArgumentException` |
| 同名重复注册 | 返回已有按钮（幂等），不新建 |
| 读 `RetroCurrent` 时玩家数组未就绪 | 视为未屏蔽，返回 `Current` |
| 触屏按钮管线未安装 | 注册仍成功（表在），状态永远 false；日志提示 |

## 5. 扩展方式

真出了可绑定的物理键（外接键盘/手柄），在 `KeybindPipeline` 里加一张"键位 → 动作"表并让 `Current` 取二者之或即可，`ModTouchButton` 不用改。

## 6. 与其它管线的边界

* **触屏按钮管线**：本管线是它的上游适配；按钮的注册、状态机、布局全在那里。
* **配置管线**：按键不写进 mod 配置（布局由游戏自己保存）。
* **UI 管线**：按键列表的展示归 UI，本管线只给判定。
