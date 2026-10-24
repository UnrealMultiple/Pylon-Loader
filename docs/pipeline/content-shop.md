# 内容管线：商店（Shop）

## 1. 职责

| 游戏时机 | 本管线做的事 |
|---|---|
| 安装期（`Static`） | `ModNpc.AddShops` 注册的店进注册表占号（99 往下发）；25 家原版店按槽 1..25 预置 |
| `InventoryStorage.SetupShop(type)` | 注册槽：原版店首开收编 + 装货，mod 店按条目装货；未注册槽：派发一轮 `ModifyActiveShop` 并补 `isAShopItem` |
| 作者 `NpcShop.Open()` / mod NPC 对话按钮 | `Main.instance.OpenShop(Index)` + 打开商店页 |
| `InventoryStorage.SetupTravelShop` | 原版旅行店装货后，把前半段交给 `GlobalNpc.SetupTravelShop` 续填 |
| 货架物品定价 | 代理属性 `Item.shopCustomPrice` / `Item.shopSpecialCurrency` 读写游戏字段（见 2.7） |
| 内容收尾 | 迟注册的店补 `ModifyShop` / 晶塔注入 / `FinishSetup` |

**不接**：商店界面的绘制与排版（归游戏）、商店刷新的额外通知（每次装货本来就会现填，见 2.4）、多人同步（逐端各自构建）、价格随好感度自动浮动（作者可自读 `Main.ShopHelper` 的 `PriceAdjustment` 自算）。

开店入口现状：**mod NPC 的对话按钮开店已通**——`ModNpc.SetChatButtons` 摆按钮、`OnChatButtonClicked(bool, ref shopName)` 回店名，`NpcShopDatabase.Find` 查到就 `Open()`（管线在 `NpcTown` 的对话按钮链里）；程序化开店走 `NpcShop.Open()`。**原版 NPC 的对话按钮入口未接**——`GlobalNpc.OnChatButtonClicked` 等全局钩收不到原版 NPC 的对话事件（见 5）。

## 2. 我们的设计

### 2.1 文件

```
Contracts/AbstractNpcShop.cs + Contracts/NpcShop.cs   条目模型：Entry、条件、排序、装货
Content/Registration/NpcShopDatabase.cs               注册表：店名 ↔ 店、槽号 ↔ 店、原版店预置
Pipelines/Shop/ShopPipeline.cs                        SetupShop 分派：收编、填货、改货钩、旅行店
```

### 2.2 注册表与槽号

店名是 `NPC类型全名/店名`（原版 NPC 前缀 `Terraria/`），重复注册直接抛。槽号从 99 往下发，落到 25 就抛（低号段是原版店自己的）。25 家原版店在 `Initialize` 里预置占号——`ByIndex` 查找、`NoPylons` 排除名单（旅商/骷髅商人/酒保/圣诞老人）、`GetShopName` 都在首次使用时懒触发，保证任何店槽查找前原版店已在表里。

`Initialize` 之后才 `Register()` 的店，在 `AddShop` 尾部就地补 `ModifyShop + AddShopEntries + Modified + FinishSetup`，迟注册的店不必等下一次装货。

### 2.3 条目模型

`AbstractNpcShop.Entry` 包一个游戏 `Item`（强 GCHandle 钉住防回收）+ `Condition[]`，带 `Disable` / `ConditionsMet`；`NpcShop.Entry` 补排序面（`SortBefore` / `SortAfter` / `OrderLast` / `ReserveSlot`）与 `AddShopOpenedCallback`。`FinishSetup` 对排序声明做拓扑排序，环直接抛。条目没有价格/货币字段——定价写在条目 `Item` 或货架物品上（见 2.7），条目模型只管"什么货、什么条件、什么顺序"。

`FillShop` 两个重载分别喂 `ICollection<Item>` 与定长 `Item[]`（溢出标志），条件与排序语义一致，作者自建店和管线装货共用。

### 2.4 挂在 `InventoryStorage.SetupShop`：每次装货现填

游戏的商店是"每个槽一个 `InventoryStorage`，装货时调 `SetupShop(type)`"。挂这里能拿到**货架装货这个权威时机**：条件逐条现算（`Condition` 每次开这家店都会重新求值，不在注册时冻结），mod 条目克隆进槽。反过来说：**条件里不要做开销大的事**——它每次开这家店都会被求值一遍。

原版店首开时先**收编**：把 native 货架条目克隆进条目表、跑 `GlobalNpc.ModifyShop`、注入晶塔货架（`ModPylon.GetNPCShopEntry`）、`FinishSetup`，然后才按条目表装货。收编只做一次（`Modified` 标记），且带 `Phase==Ready` 门——安装期预热触发的装货不收编（见 3）。

装货收尾统一两件事：把本槽物品交给 `ModNpc.ModifyActiveShop(shopName, items)` 与 `GlobalNpc.ModifyActiveShop(npc, shopName, items)` 改货，然后给 `type > 0` 的物品补 `isAShopItem = true`。`AddShopOpenedCallback` 在有对话 NPC 时逐条回调。

### 2.5 未注册槽

`SetupShop` 的槽号查不到注册店时（1..25、26..99 之外的特殊店槽），不接管装货，但对这槽货快照跑一轮 `ModifyActiveShop` 分派（shopName 同为 "Shop"，作者按 NPC 区分）并补 `isAShopItem`——特殊店的改货钩有门可入。

### 2.6 旅行商店

`SetupTravelShop` 的 orig 跑完后，扫描 `Main.travelShop` 找第一个空槽，把数组与续写位置交给覆写了 `SetupTravelShop` 的 `GlobalNpc`（只有声明了覆写才挂这个钩），改完写回。安装门因此是独立的。

### 2.7 货架定价：生成代理的 Nullable 属性（生成器级根治后）

游戏的定价字段是 `Item.shopCustomPrice`（`Nullable<int>`：`hasValue` + `value`）与 `Item.shopSpecialCurrency`（`int`），货架按它们显示价格。定价直接用生成代理属性——2026-11-21 起，Il2CppInterop 生成器对 `Nullable<T>` 类型的**字段属性 getter 改走 `RawValueBox.Wrap` 完整载荷造盒**（`il2cpp_value_box` 在本构建会抹掉 `Nullable` 的 `hasValue` 前缀，详见 `src/csharp/il2cppinterop/VENDOR.md` 补丁条目），代理属性读写全程可靠：

```csharp
item.shopCustomPrice = new Il2CppSystem.Nullable<int>(500);   // 写 hasValue + value
var price = item.shopCustomPrice;                             // Wrap 盒：HasValue/Value/hasValue/value 一致
item.shopSpecialCurrency = 3;                                 // 特殊货币（如 3 = defender medals 这类自定义货币号）
```

setter 保持生成的 unbox + cpblk 形状：入参盒只要载荷完整（显式构造、Wrap 盒、runtime_invoke 返回盒都完整），cpblk 落盘就是正确的。写值用显式构造 `new Il2CppSystem.Nullable<int>(500)`——包装类没有托管 `int?` 的隐式转换，这是生成代理的固有形状；清价用 `new Il2CppSystem.Nullable<int>()`（object_new 全零载荷，`HasValue=false`）。

曾经的 `ItemTml.ShopCustomPrice/ShopSpecialCurrency` 扩展属性（指针直读写）已删除：根治后代理属性是唯一可靠路径，不留双轨。

### 2.8 开店入口

`NpcShop.Open()` → `NpcShopDatabase.OpenShop(fullName)` → `Main.instance.OpenShop(Index)` + 商店页打开，程序化开店与按钮开店同一出口。mod NPC 的对话按钮链在 `NpcTown`：按钮文字来自 `SetChatButtons`，点击进 `OnChatButtonClicked(bool, ref shopName)`，回填店名后 `Find(npc.type, shopName)` 查表开店；店名对不上时一次性提示（不抛给玩家）。

## 3. 取舍理由

* **为什么挂在 `InventoryStorage.SetupShop`？** 货架装货是"这个槽现在要摆什么"的唯一权威时机：条件在这里现算才不会冻结，mod 店与原版店也在这里自然汇合。挂在更早的时机（注册时写死货架）会把条件变成快照。
* **为什么收编只做一次且带 `Phase==Ready` 门？** 收编时晶塔货架可能还没发完号——安装期的预热装货会把不完整的一次收编固化（`Modified=true` 后不再重跑），晶塔商品就永久缺席（探针实证过）。Ready 门让首收编发生在世界就绪之后。
* **为什么槽号从 99 往下？** 原版店槽在低号段，高位往下发不撞；发号留在 `Register()` 是因为 `Index` 是作者开店也要用的。
* **为什么定价直接用生成代理属性？** 生成器已对 `Nullable<T>` 字段属性的 getter 做完整载荷造盒（见 2.7），读写全程可靠；不再绕开（曾用的指针直读写扩展属性已随根治删除，双轨反而是负担）。
* **为什么条目模型不带价格字段？** 定价的入口已经存在——条目 `Item`（填货前写价，随克隆进货架）或 `ModifyActiveShop` 的货架物品（每次开店改价）；条目上再造一层价格只会出现两条真相。字段读写不可用的问题在 2.7 解决，不需要新 API 层。
* **为什么原版 NPC 的对话按钮没接？** 按钮链要"原版 NPC 也进自定义对话流程"，牵动对话分派的边界判定，与商店填货无关；mod 店的按钮开店已可用，先把这条留作入口扩展（见 5）。

## 4. 失败语义

| 情况 | 表现 |
|---|---|
| 同一 `NPC类型/店名` 注册两次 | `Register()` 抛异常（作者声明的错，安装期就炸） |
| mod 店槽耗尽（发号落到 25） | `Register()` 抛异常 |
| 注册的不是 `NpcShop`（手机端自定义店基类） | `NotSupportedException` |
| 条目 `Condition` 抛错 | 本次装货中止，记一条（含堆栈描述），货架维持原状；下次开店重试 |
| `GlobalNpc.ModifyShop` 抛错 | 记一条，跳过这个全局钩，其余继续 |
| `ModifyActiveShop` / `AddShopOpenedCallback` 抛错 | 记一条，本次装货中止（同条件抛错的处理） |
| 晶塔 `GetNPCShopEntry` 抛错 | 记一条，跳过这个晶塔，其余照常注入 |
| `OpenShop` 店名未注册 | 抛 `ArgumentException` |
| 聊天按钮要开的店没注册 | 一次性提示（含店名），不开店 |
| 货架条目超过槽容量 | 记 Warn，停止继续装（已装的保留） |

## 5. 扩展方式

* **原版 NPC 的对话按钮**：把对话按钮链放宽到"有全局钩的 NPC"，按钮/点击分派复用现有 `NPCInteraction` 面；注册表与填货零改动。
* **条目级价格/货币 API**：`NpcShop.Entry` 需要时可以加 `Price`/`Currency` 之类的便捷成员，底下就是 2.7 的代理属性读写，不新增真相层。
* **商店页图标**：按对话 NPC 换商店页图标属于界面侧接线（`GUIPageIcons`），与本管线的填货解耦。

## 6. 与其它管线的边界

* **NPC 管线**：店归属哪个 NPC 由 `NpcType` 决定，对话按钮开店链在 NPC 管线（`NpcTown`）；本管线只按类型查表。
* **物品管线**：货架物品本身的定义/克隆归物品管线；本管线只写价格与货币字段、补 `isAShopItem`。
* **晶塔（Pylon）管线**：`ModPylon.GetNPCShopEntry` 提供货架条目，注入时机与排除名单在本管线。
* **条件系统**：`Condition` 是作者自己的 `Func<bool>`，本管线只求值。
* **界面**：商店页绘制、`OpenShop` 的 UI 侧归游戏。

## 2026-11-21 批：货架定价字段直读写（SHOP-W3 改写）

- 旧结论作废：「生成代理写值丢失（ctor 对、写后读 0）→ 定价货架层失效，SKIP 归 interop 层」。机制已查清：生成代理的 `Item.shopCustomPrice`（`Il2CppSystem.Nullable<int>` 属性）getter 用 `il2cpp_value_box` 造盒、setter `unbox` 包装盒 + `il2cpp_class_value_size`（运行时膨胀类）+ `cpblk` 落盘，读写都锚在装箱对象上；本构建的装箱会抹 `Nullable` 的 `hasValue` 前缀（`Il2CppInterop.Runtime/RawValueBox.cs` 注记同款缺陷，HookGen 转换层已为此改走手工造盒）——run4 探针的「ctor 盒对（`object_new` + 原生 `.ctor` 双写）读回 True/50、属性写后读 0」与该机制吻合。
- 修法（当批）：`ItemTml` 扩展属性 `Item.ShopCustomPrice`（`int?`）/ `Item.ShopSpecialCurrency`（`int`）——按运行时解析的字段偏移（0x140：`hasValue`@+0、`value`@+4；0x13C）在对象指针上直读写，不经装箱链；偏移来自 `refs/dump.cs` 与 `refs/il2cpp.h` 的本构建布局，运行时再按元数据解析、失败降级提示。
- 验证（当批）：SHOP-W3 复测时把探针写读改走新属性（写价 → 即读一致 → 真开店看货架价签）；SHOP-R1..R3/W1/W2/W5/W6 的既有结论不受影响（收编/注入/按钮开店路径未动）。

## 2026-11-21 批（二）：生成器级根治，扩展属性删除（SHOP-W3 复活）

- 「归 interop 层」的欠条兑现：`Il2CppInterop.Generator` 对 `Nullable<T>` 类型的字段属性 getter 改发 `RawValueBox.Wrap`（完整载荷造盒），同分支连带修掉直调方法返回 `Nullable<T>` 的造盒（同形 emit 路径）；setter 维持 unbox + cpblk 不动——载荷在源头就完整，落盘天然正确。改动点与理由见 `src/csharp/il2cppinterop/VENDOR.md` 补丁条目 7。
- 收敛：`ItemTml.ShopCustomPrice` / `ShopSpecialCurrency` 扩展属性与整套字段偏移解析删除——代理属性已可靠，双轨只会留下「哪条是真」的分歧。作者入口回到代理属性：`item.shopCustomPrice = new Il2CppSystem.Nullable<int>(500)`，清价 `new Il2CppSystem.Nullable<int>()`。
- 验证：SHOP-W3 从 SKIP 复活为真实断言（显式构造写 → 代理属性读 → Wrap 盒小写字段读 → Marshal 原生字段直读，四路一致；Wrap 盒写回另一物品验证 setter 落盘；清价写法验证 HasValue=false）。运行：`pwsh -File src/csharp/tools/pylon.ps1 push -Samples PipelineTest` 后 `pylon.ps1 run -WaitFor 'SHOP-W3'`（真机）；SHOP-R1..R3/W1/W2/W5/W6 既有结论不受影响。
- 对齐结论：另一侧加载器以手写托管 struct 包装 `Nullable<T>` 字段读写绕开同一缺陷；本侧在生成器层根治后，两侧的 `shopCustomPrice` 写读都可用（D4 对齐）。
