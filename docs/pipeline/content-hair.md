# 内容管线：发型（Hair）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（`Static`） | `ModHair.SetStaticDefaults` |
| 安装期（`Surface`） | 主贴图写进扩长后的 `TextureAssets.PlayerHair` |
| 绘制 | 不挂钩子：游戏按 `player.hair` 取 `TextureAssets.PlayerHair[hair]` 画 |

**不接**：发型菜单（创建角色 / 理发师）里的可选列表与解锁判定、戴帽子版贴图（`PlayerHairAlt` 只扩表不写）、`GUIHairWindow` 的绘制、染色、多人同步。

## 2. 我们的设计

### 2.1 两个文件 + 两次表接入

`Pipelines/Hair/HairTable.cs`（模板表 + 编号）、`Pipelines/Hair/HairPipeline.cs`（发号 + 扩表 + 写贴图）。表接入复用现有设施：`TextureTable.PlayerHair` + `TypeTables` 的 `TableKind.Hair`。

### 2.2 编号从运行期表长接

游戏没有编译期的发型总数，所以起点在**扩表之前**按 `TextureAssets.PlayerHair.Length` 捕获一次（`TypeTables.HairVanilla`）。扩表用现成的 `TableGrower`：它只动"当前长度 == 原版长度"的静态数组，`PlayerHair` 与 `PlayerHairAlt` 长度相同，所以两张表会一起被扩到同一个目标长度——这正是我们要的（编号在两边的含义必须一致）。

### 2.3 主贴图与戴帽子版

`TextureAssets.PlayerHair[type]` 写作者给的主贴图；`PlayerHairAlt[type]` 写 `AltTexture` 指的那张（没有 `_Alt` 文件就退回主贴图）。两条路径各自加载、各自写槽，所以"戴帽子时发型消失"不会发生。

## 3. 取舍理由

* **为什么用 `TableGrower` 而不是自己扩数组？** 扩表不只是换数组：要处理"表还没建出来（null）"、"游戏重建表（切语言/重载资源）"、"补齐元素类型"这些情况，`TableGrower` 已经有这套逻辑且有日志。自己写一份等于复制一遍边界处理。
* **为什么菜单用缓存比对而不是每帧重算？** 见对应小节：`ListWarrantsRemake` 每帧都可能被问，只在解锁态变化时才要求重造列表，避免每帧重建。
* **为什么 alt 的加载与主贴图分开？** 见 2.3：alt 是第二条贴图路径（另加载文件、另写一张表），走同一套写槽设施但取值不同。
* **为什么编号不从 0 开始、也不用负号？** 负号在游戏里是"无发型/光头"这类特例的惯用表示，占用负区间会和原版语义打架；从原版表长之后接是最不容易误解的区间。
* **为什么私有字段能直读？** interop 的代理类型是按内存布局生成的，字段可见性不参与——游戏源码里的 private 字段在托管侧就是普通字段（编译期已验证）。这条结论对所有"私有字段读不到"的判断都适用。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 原版表长捕获不到（表还没建） | 不发号、不扩表，本轮什么都不做 |
| 作者贴图缺失 | 该编号的槽留空：画到它时什么都不显示（不崩） |
| 安装流程重入（`Build` 再跑一次） | 幂等：已发号的跳过，不会重复分配 |
| `player.hair` 被设成 mod 编号 | 正常显示（`PlayerHair` 已经扩长并写过槽） |
| 玩家戴帽子 | 这一版看不到 mod 发型（alt 表空着，见 2.3） |
| 游戏重建贴图表 | `TableGrower` 的补扩逻辑会重新扩一遍 |
| 编号超出扩表目标长度（作者拿去乱算） | 绘制时按游戏自己的行为走（越界由游戏报），本管线不做额外保护 |
| `SetStaticDefaults` 抛异常 | 抛给安装管线（安装失败） |

## 5. 扩展方式

接菜单：在 `HairPipeline` 里挂创建角色与理发师两个界面的入口，把 mod 编号追加进它们的可选列表，并用 `ModHair.IsUnlocked(isAtCharacterCreation, isAtStylist)` 过滤；接 alt：在安装期的 `Surface` 阶段按 `AltTexture` 再加载一次并写进 `PlayerHairAlt`。

两个扩展都不需要改 `HairTable` 与编号分配：编号一旦发出去就稳定，菜单与 alt 都只是"读同一个编号的另外两条数据流"。

另外，发型与云、gore 共用同一套扩表设施，所以"游戏重建贴图表"这类边界不需要各管线各写一遍——这是把 `TableGrower` 抽出来共用的主要收益。

## 6. 与其它管线的边界

* **玩家管线**：`player.hair` 这些字段归玩家管线；本管线只保证"编号对应的贴图存在"。
* **贴图设施**：扩表与写槽归 `TypeTables` / `TextureSlots`，本管线只调用。
* **界面**：发型菜单归界面侧的管线（本仓库暂无），本版不接。
* **条件系统**：`GetUnlockConditions` 用的 `Condition` 归条件/成就侧，本管线只调用它的 `IsMet`。
* **贴图表共用**：发型、云、gore 三条管线用的是同一个 `TableGrower`，所以"扩表"这件事在本仓库只有一处实现、一处日志格式。

### 这一版没接

1. **发型菜单的滚动与分页**：mod 发型已经会进创建角色与理发师的可选列表（按 `IsUnlocked` 判定，两段式：`ListWarrantsRemake` 算解锁态、`RebuildList` 塞进列表），但菜单自己的滚动/每页数量是游戏算的。
2. **`GUIHairWindow` 的绘制**：不挂。
3. **染色**（`player.hairDye`）：照游戏的染色走，本管线不参与。
4. **多人同步与存档迁移**：发型编号本身随玩家存档/同步走（原版通道），mod 编号没有额外通道；卸 mod 后用 mod 发型的存档会回退到默认。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 语义明确 | 编号由管线分配（原版表长起、注册序），作者不要自己写死 |
| 生效 | `AltTexture` 写进 `PlayerHairAlt`（没有 `_Alt` 就用主贴图） |
| 生效 | `IsUnlocked` / `AvailableDuringCharacterCreation` / `GetUnlockConditions` 在菜单两段式里被调用 |
| 本版不生效 | 菜单滚动分页、`GUIHairWindow` 绘制、染色、mod 发型的额外同步通道 |
