# 内容管线：商店（Shop）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（`Static`） | 已 `Register()` 的 `NpcShop` 进表 |
| `InventoryStorage.SetupShop(type)` | mod 店的槽号 → 按 `Entries` 现填（条件、价格、货币都生效） |
| 作者 `NpcShop.Open()` | 走游戏自己的 `Main.instance.OpenShop(Index)` + 打开商店页 |

**不接**：原版商店的预置与改写、商店刷新/重进时的重建、`InstallTravel` 里的旅行商人逻辑。价格浮动按条可选（`happinessPrice`，见 4.6）。

**NPC 聊天里的商店按钮：本构建没有这个入口**（已核对，不是省略）——`refs/dump.cs` 全文搜 `DrawNPCChatButton` / `NPCChatButton` 均 0 命中，只有 `ChatButton` 这种**布局字段**（`TransactionButton_Layout ChatButton`）；`OnChatButtonClicked` 是 `ModPlayer` 侧的钩子、只在游戏已有的聊天按钮被点时触发。也就是说这一版没法「给 NPC 加一个商店按钮」，mod 店只能走 `NpcShop.Open()`（`Main.instance.OpenShop(Index)`）。

## 2. 我们的设计

### 2.1 两个文件

`Pipelines/Shop/ShopTable.cs`（槽号 ↔ 店）、`Pipelines/Shop/ShopPipeline.cs`（`SetupShop` 分派 + 填表）。

### 2.2 槽号已经在契约里，管线只管"摆"

`NpcShop.Register()` 已经从 99 往下发号并查重（同一 `NPC类型/店名` 重复注册直接抛）。管线只做两件事：**把已注册的店收进表**、**在游戏要给这个槽装商品时按作者的表填**。

发号逻辑留在契约里是有意的：作者 `Register()` 的返回值和 `Index` 是他自己也要用的（`Open()` 依赖它），管线不该成为"必须再调一次"的第二道关。

### 2.3 为什么挂在 `InventoryStorage.SetupShop`

游戏的商店是"每个槽一个 `InventoryStorage`，装货时调 `SetupShop(type)`"。挂这里的好处是：**只对 mod 槽号接管，其它槽原样交给游戏**——原版商店的复杂逻辑（旅行商人、条件商品、价格浮动）一行都不用碰，也就不会因为我们的介入而变形。

mod 槽号的分支里我们不调 `orig`：原版 `SetupShop` 是按它自己的 shopType 分支填货的，对我们的槽号它没有内容可填，调它只会白跑一遍分支。

### 2.4 每次装货都现填

`Entries` 里的 `Condition` 是"现在能不能摆"的判定，所以填货时逐条求值，而不是注册时求值一次再缓存。这与原版商店的行为一致（同一家店在不同时间可以有不同的货）。

反过来说：**条件里不要做开销大的事**——它每次开这家店都会被求值一遍。

### 2.5 与契约里 `Build()` 的关系

`NpcShop.Build()` 是"作者自己 `Open()` 时把货装进 `Main.instance.shop[Index]`"，本管线是"游戏给这个槽装货时把货填进它递过来的 storage"。两条路来源相同（同一份 `Entries`）、去向不同，所以填表逻辑各写一份；共同点是都按 3.4 现算条件。

## 3. 取舍理由

* **为什么不接管聊天按钮？** 聊天按钮要做三件事：在 NPC 对话里多画一个按钮、按钮点了要回传、回来还要认是哪家店。这套入口在 NPC 聊天界面里，属于界面侧的活；本版先把"店能开、货对"做实，按钮留成缺口（作者可以 `Open()` 显式开）。
* **为什么槽号从 99 往下？** 游戏自己的商店槽是低号段，从高位往下发不会撞上；`Main.instance.shop` 的长度足够容纳。
* **为什么不在注册时就把货装进 `Main.instance.shop`？** 那样条件就冻结在注册那一刻了（见 2.4），而且世界还没加载完时求值条件经常读到空数据。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 同一 `NPC类型/店名` 注册两次 | `Register()` 抛异常（作者声明的错，安装期就炸） |
| 商品的 `Condition` 抛异常 | 这一条不摆，记一次，继续摆后面的 |
| 商品 item 类型不存在（作者写错编号） | `SetDefaults` 由游戏处理（通常是空气物品），本管线不吞 |
| 槽号没注册（`Index < 0`） | 不接管，交给游戏 |
| `Entries` 为空 | 装一个空店（能开、什么都没有） |
| 店被打开两次 | 每次装货都重新填，不缓存 |
| `happinessPrice` 但找不到对话中的 NPC | 原价（不报错） |
| 好感度倍率 ≤ 0 | 原价 |
| 乘完 < 1 | 取 1（不出现 0 元商品） |

### 4.6 价格随好感度浮动（`happinessPrice`）

原版城镇商店的价格 = 基础价 × `CurrentShoppingSettings.PriceAdjustment`（0.75 最低、1.5 最高，由 NPC 对玩家的心情算出来）。mod 店的价格是我们写 `shopCustomPrice` 的，所以那条链路要不要参与，得我们自己决定：

```csharp
type.Register().Add(ModContent.ItemType<Foo>(), price: 500, happinessPrice: true);
```

* 开了开关：`Price` 先乘 `Main.ShopHelper.GetShoppingSettings(Main.LocalPlayer, 对话中的 NPC).PriceAdjustment`，四舍五入、不低于 1；
* 没开（默认）：`Price` 原样写进 `shopCustomPrice`。

**为什么默认关**：原版是否已经对 `shopCustomPrice` 乘过折扣，从 `refs/dump.cs` 的方法体看不出来（`ApplyTaxesToPrice` 那类调用都在方法体里，dump 没有方法体）。默认关 = 最多少打折，不会出现打两次折；作者按自己的观察打开即可。参考实现（参考实现）也完全没有碰这块。

**另一个用途**：作者想在 `Condition` 里按好感度改条件（比如心情好才卖稀有货），直接读 `Main.ShopHelper.GetShoppingSettings(player, npc).PriceAdjustment` 就行，不用我们提供 API。

## 5. 扩展方式

接聊天按钮：在 `ShopPipeline` 里挂 NPC 对话界面的按钮绘制/点击入口，把"这家 NPC 有哪些店"从表里查出来，点击后调 `Main.instance.OpenShop(Index)`。表与填货逻辑都不需要改。

接原版商店改写：需要按 shopType 反查原版店的商品表（本版没有这张表），属于另一个方向的扩展。

## 6. 与其它管线的边界

* **NPC 管线**：`NpcType` 开门的是哪个 NPC 归 NPC 管线；本管线只在表里用它做键。
* **物品管线**：商品的价格/货币写的是 `Item` 的 `shopCustomPrice` / `shopSpecialCurrency`，物品本身的定义归物品管线。
* **界面**：商店界面的绘制与 `OpenShop` 归游戏自己，本管线只触发。
* **条件系统**：`Condition` 是作者自己的 `Func<bool>`，本管线只求值。

### 这一版没接

1. **NPC 聊天里的商店按钮**：mod 店不会出现在对话按钮里，作者要自己 `Open()`。原因：这个构建里没有 `DrawNPCChatButtons` 这样的入口（`dump.cs` 查无），聊天这块只有 `RemadeChatMonitor.DrawChat(bool)`，它画的是聊天文本，不含商店按钮。
2. **原版商店的预置与改写**：本版只接管 mod 槽号（见 2.3）。
3. **商店刷新时机**：不挂"重进商店要重填"的钩子；游戏每次装货都会走到 `SetupShop`，所以行为上不缺，但没有额外的刷新通知。
4. ~~**价格浮动**（NPC 好感度、折扣）~~：**已接（显式开关）**——`Add(..., happinessPrice: true)` 时把 `Price` 乘 `Main.ShopHelper.GetShoppingSettings(player, npc).PriceAdjustment`（0.75~1.5），取整且不低于 1。默认关，理由见 4.6。
5. **多人同步**：商店内容逐端各自构建，本管线不加通道。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 语义明确 | `Register()` 发号（99 往下）+ 查重，重复注册抛异常 |
| 语义明确 | `Condition` 每次开这家店都会重新求值，别在里面做重活 |
| 本版不生效 | 聊天按钮入口；作者需要显式 `NpcShop.Open()` |
