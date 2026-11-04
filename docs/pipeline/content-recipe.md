# 内容管线：配方（Recipe）

## 1. 职责

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `Recipe.FindRecipes` | 条件不满足的配方从制作菜单里滤掉（`ModRecipe.Conditions` + `GlobalRecipe.RecipeAvailable`） |
| `GUICraftGuidePopup.FindRecipes` | 向导对话里同样滤掉被 `GlobalRecipe.RecipeAvailable` 否决的配方（条件不影响向导） |
| `Recipe.ProcessGroupsForText` | 自定义配方组的显示名（`ModRecipe.CreateRecipeGroup` 注册的名字） |
| `Recipe.GetIngredientCraftingDiscount` | `ModRecipe.AddConsumeIngredientCallback` + `GlobalRecipe.ConsumeItem`（改这次实际消耗几个） |
| `Main.CraftItem_GrantItem` | `ModRecipe.AddOnCraftCallback` + `GlobalRecipe.OnCraft`（做出来的物品进光标之前） |
| 安装期收尾 | 派生表重建：材料表、可制作表、必需材料快速索引、微光表、样例物品 |

**不接**：`Recipe.maxRecipes` 之后的配方（它是 `const 3700`，改不了，见第 2 节）、制作界面的排版、商店与微光本身的转换逻辑。

配方的特殊之处：**它不是一个"有 id 的内容"**。游戏里一条配方就是 `Main.recipe[i]` 里的一个 `Recipe` 对象，作者用 `new ModRecipe()` 抓住 `Recipe.currentRecipe` 再链式填。所以管线要做的不是发号，而是：

1. 把"这条配方属于哪个 mod、有什么条件与回调"记在一张侧表里；
2. 在游戏问"哪些配方现在能做"时，把不满足条件的滤掉；
3. 在所有配方都加完之后，把游戏那几张派生表重建一遍。

## 2. 我们的设计

### 2.1 两个文件

```
Pipelines/Recipe/
  RecipePipeline.cs   模块：五个游戏入口的分派
  RecipeTable.cs      侧表（密集 struct 数组）、配方组登记、派生表重建
```

### 2.2 侧表：密集 struct 数组 + `ref`（对应性能要求）

这是这条管线最该省内存的地方：配方数量可以上千，而每条配方的元数据只有**五个字段**。参考实现的做法是每条配方一个 `Extra` 对象（对象头 + 引用字段，且散落在堆上）。

我们的做法：

```csharp
private struct RecipeEntry
{
    public Mod? Mod;                                  // 归属
    public int Station;                               // 工作台（-1 = 无）
    public List<Condition>? Conditions;               // 绝大多数配方是 null
    public List<ModRecipe.ConsumeIngredientCallback>? Consume;
    public List<ModRecipe.OnCraftCallback>? Craft;
}
```

* **按配方下标索引的密集数组**（`RecipeEntry[]`），下标就是 `Main.recipe[i]` 的 `i`；
* 判定路径上取 `ref RecipeEntry entry = ref Entries[i]`，**不复制结构体、不装箱、不查字典**；
* 三个列表默认是 `null`：普通配方只占 8（引用）+ 4（int）+ 3×8（引用）字节，没有任何额外分配；
* 需要按 `Recipe` 对象反查下标的地方（制作完成、材料折扣、配方组文字）用 `Dictionary<nint,int>` 查一次——这三处都是"每次制作几次"，不在每帧路径上。

**为什么不用 `Dictionary<nint, struct>`？** 因为过滤路径每帧都要遍历所有可选配方：那条路径上手上有的是**下标**（`Main.availableRecipe[k]` 就是配方下标），数组直接索引一下就完事；用字典就变成了每帧上千次哈希。指针字典只在"拿到 Recipe 对象但不知道下标"的冷路径上用。

### 2.3 过滤：原位压缩

`FindRecipes` 之后遍历一次即可选数组：

1. 记下当前聚焦的是哪一条（`Main.availableRecipe[Main.focusRecipe]`）；
2. 从 `0..numAvailableRecipes` 逐条问 `Available(index)`；
3. 不满足的跳过，满足的写到"写指针"位置（`kept`）；
4. `kept == count` 就什么都不做（绝大多数帧是这样，早退）；
5. 否则改计数，并让游戏按原来那条重新聚焦。

`Available(index)` 的顺序是：**条件先**（纯托管、便宜），**GlobalRecipe 后**（要进作者代码）。

### 2.4 制作完成与消耗

* `CraftItem_GrantItem`：先跑 `ModRecipe.CraftCallbacks`，再跑每个 `GlobalRecipe.OnCraft`，最后交回原版发物品。**先回调后发物品**，因为作者要在物品进背包之前改它。
* `GetIngredientCraftingDiscount`：先取原版折扣（炼金桌那类），再让回调按"已扣过折扣的剩余数量"继续改，最后把结果夹回 `[0, 手上有的数量]`——不夹会出现负数消耗或超出持有量。

### 2.5 配方组

`ModRecipe.CreateRecipeGroup` 建组后登记到 `RecipeTable`：`名字 → RecipeGroup` 与我方的显示名函数。`ProcessGroupsForText` 里如果游戏给出的文字命中我们自己登记的组，就用作者的名字替换。游戏内建的组按名字反射 `Terraria.ID.RecipeGroups` 的静态属性——那批组没有强类型入口。

### 2.6 派生表重建

`Finish` 在"配方阶段"末尾按固定顺序重建五张表，**每张单独 try/catch**：前一张失败不应该让后一张也不建（否则制作菜单会整体空白）。`ModRecipe.Register` 自己也会在注册路径上重算"这条配方用到的物品是材料"，所以中途失败还有补救。

## 3. 取舍理由

* **为什么侧表不放进 `Recipe` 对象？** `Recipe` 是游戏类型，不能加字段；用 `ConditionalWeakTable` 或 `Dictionary<nint, object>` 都是"每条配方一个对象"，与上面的密集数组相比多一次堆分配与一次间接跳转。
* **为什么条件与全局否决分开问？** 条件在 `ModRecipe` 上、是纯数据；`GlobalRecipe` 要进作者代码。先问便宜的、再问贵的，能在"条件不满足"时省掉作者回调。
* **为什么向导对话只滤全局否决、不滤条件？** 作者的意图通常是"条件限制制作"，而向导是"这东西怎么做"的查询入口；条件在向导里藏掉会让玩家完全查不到这条配方。这与参考实现的语义一致。
* **为什么过滤要重新聚焦？** 原位压缩会把下标整体前移；不重新聚焦，玩家会看到光标跳到另一条配方上。
* **为什么 `Finish` 的每一步都要单独兜？** 这几张表是给 UI 读的，任何一张没建好都会让制作界面显示不全；分开兜能让"最坏情况只缺一项"。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| 条件判定抛错 | 记一条，按"不满足"处理（这条配方这次不出现） |
| `GlobalRecipe.RecipeAvailable` 抛错 | 记一条，按"不否决"处理（宁可显示也不静默吞掉配方） |
| 消耗回调抛错 | 记一条，用原版折扣 |
| 制作回调抛错 | 记一条，物品照常发 |
| 配方组名反射失败 | 该组退回游戏给的名字 |
| `Finish` 某一步失败 | 记一条，继续建后面的表 |
| 配方数超过 `Recipe.maxRecipes`（3700） | **不做伪造**：那是 `const`，扩容无意义。超出的配方仍在 `Main.recipe` 里（能被分解、能被向导查到），但制作菜单看不见。启动日志里报一条总数，让作者知道越界了 |

## 5. 扩展方式

新增配方相关时机：`HookId` 末尾追加 → `GlobalRecipe` 加同名 `virtual` → 在 `RecipePipeline` 对应入口分派。给 `RecipeEntry` 加字段时只要保持"默认 null / 0 表示没有"，密集数组的零成本特性不受影响。

## 6. 与其它管线的边界

* **物品管线**：`ItemID.Sets.IsAMaterial` 的写入在 `ModRecipe.AddIngredient` 与收尾重建里；物品本身的行为归物品管线。
* **安装管线**：配方阶段由安装计划编排（`AddRecipes` → `PostAddRecipes` → 本管线的收尾 → `PostSetupRecipes`）。
* **网络管线**：配方同步归网络管线。
* **Tooltip 管线**：配方界面的文字排版归 Tooltip / UI，不在这里。
