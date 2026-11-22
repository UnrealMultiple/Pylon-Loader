# 内容管线：配方（Recipe）

## 1. 职责

| 游戏时机 | 分派的作者钩子 |
|---|---|
| 注册期（`RecipeTable.Added`） | 首条下标越过 `Recipe.maxRecipes`（3700）的配方登记时打开补扫门（第 2.7 节） |
| `Recipe.FindRecipes` | 条件不满足的配方从制作菜单里滤掉（`ModRecipe.Conditions` + `GlobalRecipe.RecipeAvailable`）；超过上限的配方在此补进可制作表 |
| `GUICraftGuidePopup.FindRecipes` | 向导对话里同样滤掉被 `GlobalRecipe.RecipeAvailable` 否决的配方（条件不影响向导）；超过上限的配方在此补进向导列表 |
| `Recipe.UpdateItemVariants` | 超过上限的配方的物品变体刷新（结果物品与材料逐个 `Refresh`） |
| `Recipe.ProcessGroupsForText` | 自定义配方组的显示名兜底（原版没答时查我们登记的显示名） |
| `Recipe.GetIngredientCraftingDiscount` | `ModRecipe.AddConsumeIngredientCallback` + `GlobalRecipe.ConsumeItem`（改这次实际消耗几个；材料是配方组时回调拿到的是占位物品的物品号） |
| `Main.CraftItem_GrantItem` | `ModRecipe.AddOnCraftCallback` + `GlobalRecipe.OnCraft`（做出来的物品进光标之前） |
| 安装期收尾 | 派生表重建：必需材料索引、材料表、材料标记、可制作表、微光表、样例物品 |

**不接**：制作界面的排版、商店与微光本身的转换逻辑。

配方的特殊之处：**它不是一个"有 id 的内容"**。游戏里一条配方就是 `Main.recipe[i]` 里的一个 `Recipe` 对象，作者 `new ModRecipe()`（内里包一个游戏 `Recipe`）链式填完，`Register` 时把它塞给 `Recipe.currentRecipe` 再调 `Recipe.AddRecipe()` 入表。所以管线要做的不是发号，而是：

1. 把"这条配方有什么条件与回调"记在一张侧表里；
2. 在游戏问"哪些配方现在能做"时，把不满足条件的滤掉；
3. 在所有配方都加完之后，把游戏那几张派生表重建一遍。

每个入口都带安装门：安装期没见到配方钩子（`AddRecipes` / `AddRecipeGroups` / 对应的 Global 钩）就不挂，普通玩家路径零开销。超上限补扫是唯一的例外：配方总数会不会越过 3700 只有注册期才知道，所以它的门是惰性门，由注册路径在首条超限配方登记时触发安装。

## 2. 我们的设计

### 2.1 两个文件

```
Pipelines/Recipe/
  RecipePipeline.cs   模块：六个游戏入口的分派 + 超上限补扫
  RecipeTable.cs      侧表（密集 struct 数组）、配方组登记、派生表重建、超限触发
```

### 2.2 侧表：密集 struct 数组 + `ref`

配方数量可以上千，而每条配方的元数据只有**五个字段**：

```csharp
internal struct RecipeEntry
{
    public int Station;                               // 工作台（-1 = 无）
    public bool Disabled;                             // 被移出制作菜单
    public List<Condition>? Conditions;               // 绝大多数配方是 null
    public List<ModRecipe.IngredientQuantityCallback>? Consume;
    public List<ModRecipe.OnCraftCallback>? Craft;
}
```

* **按配方下标索引的密集数组**（`RecipeEntry[]`），下标就是 `Main.recipe[i]` 的 `i`，`Register` 时同步写入；
* 判定路径上取 `ref`，**不复制结构体、不装箱、不查字典**；
* 五个字段默认是 null / 0：普通配方与数组默认值无异，没有任何额外分配；
* 需要按 `Recipe` 对象反查下标的地方（制作完成、材料折扣）用 `Dictionary<nint,int>`（键 = 对象指针）查一次——这两处都是"每次制作几次"，不在每帧路径上；
* 不是 mod 配方的对象反查不到，拿到的是一张共享空表（调用方改不到真表）。

**为什么不用 `Dictionary<nint, struct>`？** 因为过滤路径每帧都要遍历所有可选配方：那条路径上手上有的是**下标**（`Main.availableRecipe[k]` 就是配方下标），数组直接索引一下就完事；用字典就变成了每帧上千次哈希。指针字典只在"拿到 Recipe 对象但不知道下标"的冷路径上用。

### 2.3 过滤：原位压缩

`FindRecipes` 之后遍历一次即可选数组：

1. 记下当前聚焦的是哪一条（`Main.availableRecipe[Main.focusRecipe]`）；
2. 从 `0..numAvailableRecipes` 逐条问 `Available(index)`；
3. 不满足的跳过，满足的写到"写指针"位置（`kept`）；
4. `kept == count` 就什么都不做（绝大多数帧是这样，早退）；
5. 否则改计数，并让游戏按原来那条重新聚焦。

`Available(index)` 的顺序是：**Disabled 与条件先**（纯托管、便宜），**GlobalRecipe 后**（要进作者代码）。向导入口传的开关关掉前一半，只问 `GlobalRecipe.RecipeAvailable`。

### 2.4 制作完成与消耗

* `CraftItem_GrantItem`：先跑 `ModRecipe.CraftCallbacks`（顺带收集作者声明消耗掉的物品），再把结果物品派给 `ModItem.OnCreated` / `GlobalItem.OnCreated`（仅自定义物品），然后跑每个 `GlobalRecipe.OnCraft`，最后交回原版发物品。**先回调后发物品**，因为作者要在物品进背包之前改它。
* `GetIngredientCraftingDiscount`：先取原版折扣（炼金桌那类），再让回调按"已扣过折扣的剩余数量"继续改，最后把结果夹回 `[0, 材料需求量]`——回调只能少扣不能多扣，也不允许扣成负数。
* **传给消耗回调的 `type`**：普通材料就是物品号；条目是配方组时（`req.IsRecipeGroup`）解析成该组**占位物品的物品号**（`req.RecipeGroup.GetPlaceholderItemType()`）。作者在回调里按物品号 `switch`/比较，拿到的必须是物品号——组号是另一个 id 空间，对不上任何物品。解析抛错记一条、跳过全部消耗回调、按原版折扣返回，这次制作照常进行。消耗回调只在制作消耗时派发（`isDecrafting` 恒为 false）；微光分解不派消耗回调。

### 2.5 配方组

建组与登记：

* `ModRecipe.CreateRecipeGroup(name, itemTypes)`：重名复用已有组；新组用本地化键 `RecipeGroups.{name}` 当游戏侧名字，成员物品顺手标成材料，登记进 `RecipeTable`（名字 → 组）。
* `RecipeGroupTml.Register`：两个重载（本地化键 / `Func<string>` 显示名）；重名不是报错，而是把新物品并进已有组。显示名函数随登记存进 `RecipeTable`。
* 游戏内建的组（`'IronBar'`、`'Wood'`…）是 `Terraria.ID.RecipeGroups` 的静态属性，按名字反射取（getter 抛什么由游戏决定，整段兜住返回 null）。
* `ModRecipe.AddRecipeGroup`：把组挂进配方的 `acceptedGroups`；配方里已有同组材料时不再重复加占位物品，没有则按组的占位物品 `AddIngredient`。

显示名兜底：`ProcessGroupsForText` 里原版先答（游戏自己的组文字路径）；没答上时，管线遍历登记表按组的 `RegisteredId` 与传入值比对，命中且登记了显示名函数就用它替换。

### 2.6 派生表重建

`Finish` 在安装计划的"配方收尾"步（`PostAddRecipes` 之后、`PostSetupRecipes` 之前）按固定顺序重建六张表：必需材料索引、材料表、材料标记、可制作表、微光表、样例物品，**每张单独 try/catch**：前一张失败不应该让后一张也不建（否则制作菜单会整体空白）。总数越过 3700 时，收尾日志额外报一条，提示超出的部分由补扫接管。

### 2.7 超过原版上限的补扫

游戏给可制作表划了一条硬线：`Recipe.maxRecipes`（3700）。mod 配方把总数推过这条线之后，下标 3700 之后的配方还躺在 `Main.recipe` 里（微光分解、按物品查都走全表，所以它们本来就能被分解、能被查到），但可制作表与向导列表的常规扫描不会把它们收进去。补扫由四件套组成，全部纯托管：

1. **触发**：`RecipeTable.Added`（每条配方注册的必经点）发现本条下标 `>= 3700` 就置标记并触发惰性门；门里先把条件过滤的两个 FindRecipes 钩补齐（已装则跳过），再挂 `Recipe.UpdateItemVariants`。未超限的 mod 不触发任何东西，原版流程零变化。
2. **扩表**：`FindRecipes` 钩在 `!canDelayCheck` 且已超限的调用上，原版流程跑**之前**把 `Main.availableRecipe`（`int[]`）与 `Main.availableRecipeY`（`float[]`）扩到 `Recipe.numRecipes`（走仓库统一的 `ArrayGrow` 设施；两数组有一方还是 null 就不动，交给原版自建），并记下当前聚焦的配方下标。
3. **补进可制作表**：原版流程跑完之后，扫 `[3700, min(numRecipes, Main.recipe.Length))`：扫到空结果（`createItem.type == 0`）即停；已经在表里的（`present` 集合，含原版这次刚补的）跳过；其余按**工作台 → 环境 → 材料数量**三条件判定，全部用游戏自己的判定方法（`Recipe.PlayerMeetsTileRequirements` / `PlayerMeetsEnvironmentConditions` / `CollectedEnoughItemsToCraft`），依次短路，全过就 `Recipe.AddToAvailableRecipes` 收进去。扫描前先重收一遍玩家身边的材料（`CollectItemsToCraftWithFrom`）——原版刚跑完的扫描会把"持有量"扣减过，不重收判定就不准。收尾调 `TryRefocusingRecipe` 让界面按扫描前聚焦的那条重新聚焦。
4. **补进向导列表**：向导的 `FindRecipes` 原版流程跑完之后，同一段下标里把**用到向导物品**的配方追加进 `popup.availableGuideRecipe`（表长不够先扩到总数，`numAvailableGuideRecipes` 顺延写）。"用到"看的是 `requiredItemQuickLookup`：条目号 0 视为表尾，`Matches(type)` 命中即算——与可制作表不同，这里**不做**工作台/环境/条件判定，向导本来就只是"这东西怎么做"的查询。
5. **物品变体**：`Recipe.UpdateItemVariants` 原版跑完后，对超限段逐条刷新：结果物品 `createItem.Refresh(true)`，`requiredItem` 逐个 `Refresh(true)`（type 0 停）。没超限直接早退。

补进来的条目与其余条目走同一套条件过滤（`Disabled` / `Conditions` / `GlobalRecipe.RecipeAvailable` 照常约束它们），向导侧同样。

## 3. 取舍理由

* **为什么侧表不放进 `Recipe` 对象？** `Recipe` 是游戏类型，不能加字段；用 `ConditionalWeakTable` 或 `Dictionary<nint, object>` 都是"每条配方一个对象"，与密集数组相比多一次堆分配与一次间接跳转。
* **为什么条件与全局否决分开问？** 条件在 `ModRecipe` 上、是纯数据；`GlobalRecipe` 要进作者代码。先问便宜的、再问贵的，能在"条件不满足"时省掉作者回调。
* **为什么向导对话只滤全局否决、不滤条件？** 作者的意图通常是"条件限制制作"，而向导是"这东西怎么做"的查询入口；条件在向导里藏掉会让玩家完全查不到这条配方。
* **为什么消耗回调拿占位物品号而不是组号？** 配方条目是配方组时，游戏字段 `itemIdOrRecipeGroup` 存的是组号，但作者对组材料只会按物品写判断；占位物品号是这组材料在配方里的"代表"，把它给回调，作者代码在"普通材料"与"组材料"之间无需分支。
* **为什么解析占位物品号要兜异常？** 组条目指向越界的组号属于脏数据，不能把异常漏进原版的制作消耗流程；兜住后按原版折扣返回，这次制作不受影响。
* **为什么过滤要重新聚焦？** 原位压缩会把下标整体前移；不重新聚焦，玩家会看到光标跳到另一条配方上。
* **为什么 `Finish` 的每一步都要单独兜？** 这几张表是给 UI 读的，任何一张没建好都会让制作界面显示不全；分开兜能让"最坏情况只缺一项"。
* **为什么补扫挂在原版流程的后面、而不是自己重扫全表？** 原版那次扫描是所有制作相关界面的数据源，自己重扫等于另立一套判定，条目顺序、聚焦行为都会和原版分叉；补扫只把原版没机会扫到的那段下标补上，判定用同一批游戏侧方法、同样的短路顺序，`present` 集合保证与原版结果重合时一个条目都不会重复加。
* **为什么只在 `!canDelayCheck` 的调用上补扫？** 带这个标记的调用是原版说"这次不能推迟"的刷新；可以推迟的刷新走原版自己的延迟路径，跟着推迟能避免对半成品状态做判定，也让补扫的次数与原版刷新对齐。
* **为什么扩表要在原版流程之前？** 可制作表的两根数组是原版按上限长度建的；补扫与原版扫描都可能往 3700 之后写，数组不够长就越界。向导列表同理，只是它挂在向导自己的弹出层上，在原版流程之后扩也来得及。
* **为什么超限门是惰性门？** 安装期只能看到"哪个 mod 有 AddRecipes"，看不到"总数会不会过 3700"；把它做成计划期门要么误挂（小 mod 包也要背补扫检查）、要么漏挂。注册路径本身就是每条配方的必经点，在那里触发，超限前零开销。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| 条件判定抛错 | 记一条，按"不满足"处理（这条配方这次不出现） |
| `GlobalRecipe.RecipeAvailable` 抛错 | 记一条，按"不否决"处理（宁可显示也不静默吞掉配方） |
| 消耗回调抛错 | 记一条，跳过这一个回调，其余回调继续 |
| 占位物品号解析抛错 | 记一条，跳过全部消耗回调，按原版折扣返回 |
| 制作回调 / `OnCreated` / `OnCraft` 抛错 | 记一条，物品照常发 |
| 配方组显示名查询失败 | 交回原版流程（不替换） |
| 按名字找组失败 | `AddRecipeGroup` 原样返回（不加材料） |
| `Finish` 某一步失败 | 记一条，继续建后面的表 |
| 补扫的扩表 / 聚焦记录抛错 | 记一条，原版流程照常跑（这次可能缺超限条目，下次刷新再补） |
| 补进可制作表抛错 | 记一条，已补上的条目保留，聚焦不动 |
| 向导补扫抛错 | 记一条，已补上的条目保留 |
| 物品变体刷新抛错 | 记一条，本轮变体刷新中止，下一轮重试 |
| 配方总数超过 `Recipe.maxRecipes`（3700） | 不是失败：超出的部分由补扫纳入制作菜单与向导，收尾日志报一条总数 |

## 5. 扩展方式

新增配方相关时机：`HookId` 末尾追加 → `GlobalRecipe` 加同名 `virtual` → 在 `RecipePipeline` 对应入口分派。给 `RecipeEntry` 加字段时只要保持"默认 null / 0 表示没有"，密集数组的零成本特性不受影响。

## 6. 与其它管线的边界

* **物品管线**：`ItemID.Sets.IsAMaterial` 的写入在 `ModRecipe.AddIngredient`、`ModRecipe.CreateRecipeGroup` 与收尾重建里；物品本身的行为归物品管线。补扫里对结果与材料物品的 `Refresh` 只刷新变体显示，不碰物品注册。
* **安装管线**：配方阶段由安装计划编排（`AddRecipes` → `PostAddRecipes` → 本管线的收尾 → `PostSetupRecipes`）；超限门的触发发生在 `AddRecipes` 注册期，早于收尾。
* **网络管线**：配方同步归网络管线。
* **Tooltip 管线**：配方界面的文字排版归 Tooltip / UI，不在这里。
