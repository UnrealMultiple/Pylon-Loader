# 内容管线：命令（Command）

## 1. 职责

把作者声明的 `ModCommand`（名字 + 运行范围 + `Action`）挂进游戏的聊天命令入口：

| 游戏时机 | 分派的作者数据 |
|---|---|
| 聊天消息以 `/` 开头且名字命中 | `Action(caller, input, args)` |
| 单人 / 联机服务端收消息 | `ChatCommandProcessor.ProcessIncomingMessage` 拦一次，命中即吃掉消息 |
| 联机客户端发送链 | `ChatHelper.SendChatMessageFromClient` 拦一次（本机命令不必发到服务器） |
| 原版 `/help` 拼消息 | 在 `HelpCommand.ComposeMessage` 的结果后面追加 mod 命令 |

**不接**：聊天输入框的补全与高亮；`Console` / `Item` 两种运行范围在本构建没有入口（声明保留）。

## 2. 我们的设计

### 2.1 文件

```
Content/ModCommand.cs     作者基类（Command / Type / Usage / Description / IsCaseSensitive / Action）
Content/CommandCaller.cs  调用方接口 + 本机/服务器侧两种实现
Content/CommandLoader.cs  命令表、名字匹配、Mod:命令、/help 列表
Pipelines/Command/CommandPipeline.cs  三个游戏入口的挂钩
```

### 2.2 命令表与匹配

`CommandLoader.Commands` 是按名字（`OrdinalIgnoreCase`）分组的多重表：同名命令来自不同 mod 时保留全部，调用方必须写成 `Mod:命令` 才能选中；只写名字时回一句"有多条同名命令"并列出 `Mod:命令`（绿色）。名字匹配按每条命令自己的 `IsCaseSensitive` 决定比较方式（`HandleCommand` 先按不分大小写找名字，再按声明决定是否把整段输入转小写）。

### 2.3 参数切分

`"/名字 参数 参数"` → 名字后面的整段是 `input`，再按空格切成 `args`（不去空项：与原版命令一致，`input.TrimEnd().Split(' ')` 后跳过第一段）。`input` 与 `args` 都给作者，因为很多命令要原样回显。

### 2.4 回话怎么走

`CommandCaller` 是接口（`CommandType` / `Player` / `Reply(text, color)`）：

* `ChatCommandCaller`：本机聊天框，`Reply` 走 `Main.NewText`（颜色缺省白）；`CommandType.Chat`
* `PlayerCommandCaller`：服务器代某位玩家执行，`Reply` 走 `ChatHelper.SendChatMessageToClient`；`CommandType.Server`

`CommandType.World` 在单人下映射为 Chat、在服务器上映射为 Server（`CommandLoader.Matches`）。

### 2.5 失败边界

`Action` 抛 `UsageException`：

* 带消息 → 用那条消息与颜色回给玩家（"你参数写错了"的正规通道）
* 不带消息 → 回 `"用法：" + Usage`，红色
* 其它异常 → 记一次日志，回 `"用法：" + Usage`，红色（内部细节不进聊天栏）

## 3. 取舍理由

* **为什么挂在 `ProcessIncomingMessage` 而不是聊天输入框？** 输入框只覆盖"本机打字"，`ProcessIncomingMessage` 覆盖"任何玩家发来的命令"；它拿得到 `clientId`，回话能精准发给发起者。
* **为什么也挂客户端发送链？** 联机时本机命令应在客户端就地执行，不该先发到服务器再被当成未知命令广播。
* **为什么 `/help` 用 `ComposeMessage` 而不是自己再发一条？** 追加进原版那条消息里，分页/排版仍由原版负责。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 同名命令（不同 mod） | 不抛；只写名字时回"有多条同名命令"，列出 `Mod:命令` |
| `Mod:命令` 里的 mod 不存在 / 该 mod 没有这条命令 | 回一句原因（红色），不执行 |
| `Command` 为空 | 注册期收表时跳过不了名字匹配（作者应保证非空；空名字不会命中任何输入） |
| `Action` 抛 `UsageException` | 回那句消息（或 Usage） |
| `Action` 抛其它异常 | 记一次；玩家收到 Usage |
| `args` 为空（只打了命令名） | 照样调 `Action` |
| 消息未命中任何 mod 命令 | 交回原版处理，不被吃掉 |

## 5. 扩展方式

接 `Console`：在服务器控制台的输入入口上调用 `CommandLoader.HandleCommand`，用 `CommandType.Console` 的 caller。接补全：在聊天输入框的候选来源里读 `CommandLoader.GetHelp`。

## 6. 与其它管线的边界

* **网络**：`ProcessIncomingMessage` / `SendChatMessageFromClient` 是游戏自己的网络入口，本管线只读文本、不造包。
* **界面**：聊天栏绘制归界面侧。
* **本地化**：`Usage` / `Description` 是作者可覆写的普通虚属性，默认 `/名字` 与空串；需要本地化时作者自己读语言表。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 生效 | `Type` 按 `Chat` / `Server` / `World` 参与匹配（`World` 随环境映射） |
| 生效 | `Description` 出现在 `/help` 里（原版消息之后追加） |
| 生效 | 同名命令可用 `Mod:命令` 消歧 |
| 语义明确 | 命中即吃掉这条消息，不会再当普通聊天广播 |
| 本版不生效 | `Console` / `Item` 运行范围（本构建无入口）；输入框补全 |
