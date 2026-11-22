# 内容管线：配置（Config）

## 1. 职责

把作者配置类（`ModConfig` 子类）的 public 字段/属性变成**能读、能改、能落盘、能多人裁决**的一份配置。作者声明 `Mode` 决定归属：`ClientSide` 本机说了算，`ServerSide` 服务器说了算。

| 环节 | 做什么 |
|---|---|
| 注册 | 反射收 public 字段/属性成选项列表 → 默认值打底 → 读盘 → 克隆一份载荷期配置 |
| 编辑 | 草稿隔离：屏幕只写「待提交表」，真值一个字节不动 |
| 应用 | 按场景分流：本地应用落盘 / 需重载拒绝 / ServerSide 客户端发请求 |
| 撤销 / 恢复默认 | 丢待提交表 / 把默认值填进待提交表 |
| 多人同步 | 服务器推值、客户端改值走请求、服务器裁决后广播、离开世界写回本机值 |

**不接**：跨 mod 的配置依赖、配置界面之外的自动重载（作者直接改字段后自己调 `SaveChanges`）。

## 2. 我们的设计

### 2.1 文件

```
Pipelines/Config/
  ConfigPipeline.cs    模块：注册、读盘、草稿、应用、落盘
  ConfigNetPipeline.cs 模块：ServerSide 的推送、请求、裁决、离开世界回滚
  ConfigEntry.cs       注册记录：真值实例、选项列表、载荷期克隆、待提交表、存盘路径
  ModConfigLoader.cs   公开门面：作者与屏幕按静态方法调用，状态全在 ConfigPipeline
Content/Config/
  ModConfig.cs         作者基类：Mode / OnChanged / AcceptClientChanges / SaveChanges / Open / NeedsReload / Clone
  ConfigOption.cs      一个选项：Get/Set 即字段读写，Coerce 定合法值，特性收编
  ConfigAttributes.cs  Label / Tooltip / Header / Range / Increment / OptionStrings / Slider / ReloadRequired 等特性
  ConfigReflection.cs  按类型反射收选项（缓存）
  ConfigScope.cs       ClientSide / ServerSide
  ConfigSaveResult.cs  Success / NeedsReload / RequestSentToServer
```

### 2.2 选项：字段即真值

选项就是 public 字段/属性（`ModConfig`/`ModType` 基类自己的成员除外），`[JsonIgnore]` 的成员不收（`[ShowDespiteJsonIgnore]` 可例外放行），按继承深度与元数据序排定。类型决定类别：

| 字段类型 | 类别 | 说明 |
|---|---|---|
| `bool` | Toggle | |
| `Color` | Color | `[ColorNoAlpha]`、`[ColorHSLSlider]` 修饰编辑形态 |
| 枚举，或带 `[OptionStrings]` 的 `string` | Choice | 候选集即 `Choices` |
| 数值类型且带合法 `[Range]` | Range | `[Increment]` 定步长；没写 `[Range]` 或区间/步长不合法按 Data 处理 |
| 其余 | Data | 只读展示；不进网络载荷 |

其余特性（`[LabelKey]`/`[LabelArgs]`/`[TooltipKey]`/`[TooltipArgs]`/`[Header]`/`[Slider]`/`[ReloadRequired]`）只修饰展示与本地化，值的合法性一律走 `Coerce`：

* **Range**：夹进 `[Min, Max]`、按 `Step` 对齐（对齐值做六位规整，压掉 float 增量的显示污染），再换算回字段类型；
* **Choice**：不在 `Choices` 里就拒绝（返回 null，调用方当"没这回事"）；
* **Color**：`"R, G, B, A"` 或 `#RRGGBB(AA)`；
* **Data**：System.Text.Json 反序列化。

### 2.3 注册与读盘

`ModConfigLoader.Add`：反射收选项 → 默认值打底（`[DefaultValue]`，否则字段初始值；Range 过一次 `Coerce`）→ 读存盘文件逐项覆盖 → 克隆一份载荷期配置（`[ReloadRequired]` 的比对基准）→ `OnLoaded`。

读盘的失败隔离是**逐项**的：某一项的存盘值读不出来就丢那一项（退回默认值），文件整体坏了才整份退默认。存档目录取不到时配置只进内存（不读也不写盘）。

### 2.4 草稿隔离

`Begin()` 为每份配置建一张「选项名 → 值」待提交表；屏幕写值必经 `Set`（一定过 `Coerce`），读值（`Read`）草稿优先、真值兜底。三条不变量：

1. 真值只有 `Apply` 写；「改了又不想保存」的东西 `Discard` 一丢就干净；
2. `Apply()` 是唯一把草稿写回真值的入口；
3. 待提交表只装被写过的键，作者在游戏逻辑里并发改真值不受草稿状态影响。

### 2.5 落盘

一份配置一个文件：`{存档目录}/ModConfigs/{Mod名}_{类名}.json`，扁平对象，键=字段名。值序列化：Toggle=bool、Range=double、Choice=string、Color=`"R, G, B, A"`、Data=System.Text.Json。

**落盘时机只有应用与裁决**：屏幕「应用」、作者 `SaveChanges`、服务器接受客户端改动，这三处之外不写盘。配置界面里的滚动、翻页、关屏都是浏览/丢弃草稿，不落盘。在线客户端**不把 ServerSide 配置写本机文件**——它的真值此刻来自服务器，写盘会把服务器值渗进单机存档；`ModConfigLoader.Flush` 兜底入口同样跳过这种情况（服务器侧与单机不受影响，它们落的就是自己的值）。

写失败不抛给作者：记一条日志，内存里的值仍生效。

### 2.6 应用

屏幕「应用」与作者 `SaveChanges(pendingConfig, status, silent, broadcast)` 走同一套分流：

| 场景 | 行为 |
|---|---|
| 主菜单 | 草稿写回真值 → 有值真的变了才 `OnChanged` → 落盘 |
| 游戏内，`[ReloadRequired]` 命中 | 不落盘、真值回滚到存盘值；`SaveChanges` 路径给 status 文案说明原因 |
| 游戏内，ServerSide + 客户端 | 把待提交值当请求发给服务器（见 2.7），真值等裁决回信 |
| 游戏内，其余 | 应用 + 落盘；ServerSide 配置在服务器上应用后广播 |

「有值真的变了才动」同时约束 `OnChanged` 与落盘：逐项比较后没有任何变化就不发回调、不写文件。

### 2.7 服务端配置（`ConfigScope.ServerSide`）

作者的配置类声明 `public override ConfigScope Mode => ConfigScope.ServerSide;` 就归服务器管。管线做的事：

| 时机 | 做什么 |
|---|---|
| 服务器把世界数据发给某客户端（原版消息 7） | 把每份 ServerSide 配置的「键名 + 值」平铺推给这个客户端 |
| 客户端收到 | 记下本机原值（每份只记第一次的快照）→ 逐项 `Coerce` 写进真值 → `OnChanged` |
| 客户端改值 | 把草稿覆盖后的整份值（待提交表优先、真值兜底）当请求发给服务器 |
| 服务器收请求 | 键值逐项 `Coerce` 灌进真值的克隆，交给作者 `AcceptClientChanges(pendingConfig, whoAmI, ref message)`：过了就把请求里的键按裁决结果写回真值、存盘、回信（成功回信带服务器整份值）；没过只回信，原因给客户端看 |
| 客户端收回信 | 成功就把服务器值写进真值 → `OnChanged`，并派 `HandleAcceptClientChangesReply`；拒绝原因上聊天栏 |
| 服务器自己改值 | 落盘 + 把真值广播给所有客户端 |
| 客户端离开世界 | netMode 从 1 掉回 0 的那一帧把快照写回真值——否则「在服务器上被改过」会渗进单机存档 |

**为什么搭原版消息 7 的车**：新客户端连上时服务器必然会发世界数据，这是「这个客户端现在要开始收东西了」的唯一可靠信号；自己造一个握手包要在两条连接状态机上都加状态。

**为什么载荷是「键名 + 值」平铺**：值本来就都是标量，走网络管线现成的子通道与值序列化就够，不需要第二套打包格式；收到的每一项仍过一遍 `Coerce`（对端可能是别的版本）。Data 选项不进载荷。

**作者钩子**：`AcceptClientChanges` 默认全接受，返回 false 是全部拒绝，抛异常按全拒绝处理。`HandleAcceptClientChangesReply(success, player, message)` 在回信到达时（客户端侧）派给作者。

## 3. 取舍理由

* **为什么草稿用待提交表而不是克隆一份对象？** 屏幕的改动本来就是逐项产生的，逐项存即可；不克隆对象就没有注册期之外的 Activator 分配，「Read 草稿优先」一样能做出「未保存的改动可见」。
* **为什么客户端改 ServerSide 不先本地生效？** 服务器才是权威：请求发出后真值回落到存盘值、等裁决回信落最终值。提前生效会短暂显示一个服务器没接受过的状态，拒绝后还得再掰回来。
* **为什么落盘只挂在应用与裁决？** 配置界面里滚动、翻页只是浏览，不该有副作用；把「关屏兜底」挂到别的交互上，迟早把不该写盘的值（比如在线时服务器下发的值）刷进本机文件。
* **为什么应用/打包时再过一次 `Coerce`？** 屏幕写入时已经 `Coerce` 过，这里是防御：对合法值是恒等变换，对漏网的坏值宁可丢那一项也不让脏值上盘、上线。
* **为什么 `OnChanged` 要逐项比较后才发？** 作者的 `OnChanged` 经常做重活（重算 Buff 表、重发贴图）；没变的项也发一遍，「应用」就会明显卡顿。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| 存盘值类型不对 / 不合法 | 那一项退回默认值，记一条警告 |
| 存盘文件损坏 | 整份用默认值，记一条错误 |
| 写盘失败 | 记一条，内存里的值仍生效 |
| 存档目录取不到 | 配置只进内存（不读不写盘） |
| 数值字段没写 `[Range]` / 区间步长不合法 | 按 Data 处理（只读展示，不进网络载荷） |
| 同步/请求包里的键不存在或值不合法 | 那一项跳过（对端可能是别的版本） |
| 请求包里没有可辨认的键 | 服务器忽略，不回信 |
| `AcceptClientChanges` 抛错 | 按全部拒绝处理，回信说明 |
| `HandleAcceptClientChangesReply` / `OnChanged` / `OnLoaded` 抛错 | 各记一条，流程继续 |
| 客户端在线时尝试落盘 ServerSide 配置 | 跳过这份配置（值来自服务器，不能写进本机文件） |

## 5. 扩展方式

新增一种值类别：`ConfigOption` 的类别判定加分支 → `Coerce` / 读盘 / 存盘 / 网络打包各加一个分支 → 配置 UI 加一条绘制分支。新增一种修饰特性：`ConfigAttributes` 定义 → `ConfigOption` 构造收进 → UI 消费。存盘格式始终是扁平对象，旧配置文件兼容。

## 6. 与其它管线的边界

* **注册管线**：配置类随内容发现实例化，`Mod.LoadConfigs` 在加载期逐个 `Add`；选项收集与读盘都发生在注册期。
* **UI 管线**：屏幕、布局、控件绘制全在 `UI/ModConfigMenu*`（暂停菜单与主菜单设置页接入）；本管线只提供「值 + 事务」。
* **网络管线**：ServerSide 同步借网络管线的子通道，不新开消息号；长度上限、收包兜底都复用现成那套。
* **本地化**：标签/提示的键按 `Mods.{Mod}.Configs.{配置}.{选项}` 约定自注册，缺失时退回按字段名拆词的原文。
