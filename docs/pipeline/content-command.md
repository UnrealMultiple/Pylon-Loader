# 内容管线：聊天命令（Command）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
| `/help`（原版列表跑完之后） | `Usage` / `Description` 逐条补在后面 |
|---|---|
| `ChatCommandProcessor.ProcessIncomingMessage(message, clientId)` | 文本以 `/` 开头且名字命中某个 `ModCommand` → 跑 `Action(caller, input, args)`，并**吃掉**这条消息 |
| 没命中 | 原样交给游戏（原版命令、普通聊天照旧） |
| 安装期（`Static`） | 收表 + `ModCommand.SetStaticDefaults` |

**不接**：`/help` 里的自动列表、`CommandType.Item` / `CommandType.Console` 这两个非聊天入口、命令的权限/分组、别名冲突的跨 mod 仲裁（同正文缺口）。

## 2. 我们的设计

### 2.1 两个文件

`Pipelines/Command/CommandTable.cs`（名字/别名 → 命令）、`Pipelines/Command/CommandPipeline.cs`（`ProcessIncomingMessage` 分派）。

### 2.2 只接管"名字命中"的命令

判定顺序：

```
文本不以 '/' 开头            → 原样交给游戏
名字在表里（按 IsCaseSensitive）→ 跑作者的命令，不调 orig
名字不在表里                  → 原样交给游戏
```

**不调 `orig` 是有意的**：游戏的 `ProcessIncomingMessage` 会继续按自己的命令表匹配，我们的命令名它不认识，最终会当成普通聊天广播出去——那等于玩家的命令被所有人看见。所以命中即吃掉。

### 2.3 参数切分

`"/名字 参数 参数"` → 名字后面的整段是 `input`，再按空格切成 `args`（连续空格忽略）。`input` 与 `args` 都给作者，是因为很多命令要原样回显（`/say` 那种），切碎了就拼不回来。

### 2.4 回话怎么走

`CommandCaller.Reply(text, color)`：

* 单机/本机（`clientId` 不是有效玩家号）→ `Main.NewText`
* 多人 → `NetMessage.SendChatMessageToClient(NetworkText.FromLiteral(text), color, clientId)`，只发给打命令的人

默认颜色用 `ModCommand.ResponseColor`（游戏 `/help` 那个黄）。

### 2.5 大小写

`IsCaseSensitive => false`（默认）的命令按不分大小写匹配；关掉之后按原样比。表里同时存两种键会互相打架，所以**表只存一份名字**，匹配时按命令自己的声明决定比较方式——查找是"逐条比对"而不是"查字典"，因为命令数量是几十条量级，而正确性比微秒重要。

### 2.6 失败边界

`Action` 里抛 `UsageException` → 把 `Message` 回给玩家（这是"你参数写错了"的正规通道）。
抛别的异常 → 记一次，玩家不回话（异常信息不回给玩家，避免把内部细节喷进聊天栏）。

## 3. 取舍理由

* **为什么挂在 `ProcessIncomingMessage` 而不是聊天输入框？** 输入框只覆盖"本机打字"，`ProcessIncomingMessage` 覆盖"任何玩家发来的命令"，包括将来接多人；而且它拿得到 `clientId`，回话能精准发给发起者。
* **为什么不把命令注册进游戏的命令表？** 游戏的命令表是按 `LocalizedText` 键注册的（`AddAlias` 那一套），注册进去还要处理本地化与卸载；本版只需要"能跑"，不需要出现在 `/help` 里。
* **为什么名字匹配不做成字典？** 见 2.5：`IsCaseSensitive` 是每条命令自己的声明，混在一张不分大小写的字典里会出现"两个命令只差大小写"时静默吞掉一个。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 命令名重复（两个 `ModCommand` 抢同一个名字） | 安装期抛异常（指名两个类） |
| `Command` 为空 | 安装期抛异常（作者没写名字） |
| `Action` 抛 `UsageException` | 回那句用法给玩家 |
| `Action` 抛其它异常 | 记一次；玩家没有回话；命令被吃掉（不会漏成普通聊天） |
| `args` 为空（只打了命令名） | 照样调 `Action`（作者自己决定是不是回 Usage） |
| `clientId` 无效 | 回话走 `Main.NewText`（本机） |

## 5. 扩展方式

接 `/help`：在 `ChatCommandProcessor` 或 `Main` 的 help 输出处加一个钩子，把表里的 `Command` + `Description` 拼进去。接 `CommandType.Console`：另挂服务器控制台的输入入口，用同一个 `Action` 与 `Reply`。

## 6. 与其它管线的边界

* **网络（7）**：`ProcessIncomingMessage` 是游戏自己的网络入口之一，本管线只读它的文本、不改包。
* **界面**：聊天栏的绘制与解析归界面侧，本管线不碰。
* **本地化**：`Usage` / `Description` 走 `ModLocalization`，键是 `Mods.<modid>.Commands.<类名>.<what>`。
* **权限**：没有权限系统，谁打的命令谁跑（本版不做管理员校验）。

### 这一版没接

1. **`CommandType.Console` / `CommandType.Item`**：只有 `Chat` 这一条入口生效；`Type` 声明本版读都不读。
2. **命令的补全与高亮**：聊天输入框不做 mod 命令的补全。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 新增 `virtual bool AllowedBy(Player player)` | 默认谁都能跑；返回 false 时管线回一句"你没有权限用 /xxx"，不调 `Action` |
| 生效 | `Description` 现在真的会出现在 `/help` 里（原版 `/help` 跑完后追加，只在第一页/无参数时补） |
| 告警 | 别名与别的命令撞车时在日志里告警（不抛，先注册的赢） |
| 语义明确 | `Command` 必须非空，重名在安装期抛 |
| 语义明确 | 命中即吃掉这条消息，不会再当普通聊天广播 |
| 本版不生效 | `Type`（只跑 Chat）、`Aliases` 冲突仲裁、`/help` 展示 |
