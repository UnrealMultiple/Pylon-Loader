# 内容管线：稀有度（Rarity）

## 1. 职责

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `ItemRarity.Initialize` | 每次游戏重建颜色表之后的回填（`SetStaticDefaults` + 写 `ItemRarity._rarities`） |
| `Item.GetPopupRarityColor_1` | `RarityColor`（拾取时飘的那个颜色） |
| `Item.Prefix_1` | `GetPrefixedRarity`（前缀把稀有度升降之后重写 `item.rare`） |

**不接**：饰品栏边框、`ItemTooltipDraw` 的悬停文字重染（归 Tooltip 管线）、`Item.rare` 的排序与筛选（游戏自己读表，不需要钩子）。

稀有度是最小的一条管线：它没有实体、没有每帧路径，全部成本都集中在"颜色表被游戏重建时怎么补回去"。

## 2. 我们的设计

### 2.1 两个文件

```
Pipelines/Rarity/
  RarityPipeline.cs   模块：三个钩子的分派
  RarityTable.cs      类型 ↔ 实例、颜色、颜色表写回（安装期也用它）
```

**为什么把表拆出去？** 颜色表要在**两个完全不同的时机**被写：安装期一次（`ItemRarity` 还没初始化时先占位），以及每次 `Initialize` 之后（游戏把表重建了）。这两处写的是同一张表、算的是同一个颜色，必须共用一份实现；放在管线里会让"安装管线调管线"变成反向依赖。

### 2.2 表结构与发号

稀有度不占"槽数组"以外的任何游戏结构：`item.rare` 就是一个 int，游戏只拿它查 `ItemRarity._rarities`（颜色表）和排序。所以：

* 类型从 12 开始往后接（`ContentKindTable.BaseCount`）；
* `RarityTable.Of(type)` 先用 `type < 12` 一次比较挡掉全部原版调用；
* 颜色表写成 `_rarities[type] = ColorOf(type)`，`Initialize` 之后无条件重写一遍（游戏会清表）。

### 2.3 前缀升降

`Item.Prefix_1` 一次调用里做三件事，顺序固定：

1. 调原版前缀逻辑（它决定这次前缀是否成立）；
2. 原版返回 true 且不是 `-3`（"随便给一个"）时，用 `TryGetPrefixStatMultipliersForItem` 取战力倍率折成档位；
3. 把档位与原倍率交给 `GetPrefixedRarity`，结果 clamp 到 `[-1, Count-1]`。

**为什么要 clamp？** 作者可以返回任意 int；`item.rare` 越界会让颜色表查找抛异常，而这里在拾取/绘制路径上。clamp 之后最坏情况只是"颜色不对"，不会崩。

### 2.4 与安装管线的边界

`SetStaticDefaults`（作者写 `ItemRarity` 的初始值）与颜色表首次写入挂成安装步骤 `Static` 阶段；本管线不写表，只在游戏重建表之后补写。

## 3. 取舍理由

* **为什么不像参考实现那样挂裸方法地址？** 我们的 detour 入口统一走 `On.` 代理（`Il2CppInterop.RuntimeDetour` 生成），混合两套入口会让"哪个钩子在哪装"变得不可预测；而 `On.Terraria.Item.GetPopupRarityColor_1` 这类代理已经覆盖了需要的三个入口。
* **为什么颜色查询每次都 try/catch？** `RarityColor` 会被绘制路径每帧读（背包里几百个物品）。作者的实现里可能有 `Main.DiscoR/G/B` 闪动，也可能抛错；捕获一次记一条、返回白色，比让整帧绘制炸掉好。
* **为什么不用缓存颜色？** 作者可以实现"闪动"（`Main.DiscoR`），缓存会把闪动冻住。表里存的只是"初始颜色"，运行时读取仍然直接问作者。
* **为什么前缀档位用战力倍率而不是稀有度差值？** 原版前缀只改数值，稀有度是作者的概念；倍率是游戏自己算的、跨版本稳定的量，稀有度差值则是我们要改的东西——用它当输入会绕回自己。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| `RarityColor` 抛错 | 记一条，用白色；物品照常绘制 |
| `GetPrefixedRarity` 抛错 | 稀有度保持不变 |
| `SetStaticDefaults` 抛错 | 记一条，该稀有度的颜色表条目按白色写 |
| 颜色表还没建（`_rarities` 为 null） | 跳过写表，等 `Initialize` 之后那次补写 |

## 5. 扩展方式

新增一个稀有度相关时机：`HookId` 末尾追加 → `ModRarity` 加同名 `virtual` → 在 `RarityPipeline` 分派。新增一张"随类型变化的表"（例如边框色），加在 `RarityTable` 里，安装管线与运行时都调它。

## 6. 与其它管线的边界

* **物品管线**：`item.rare` 只是物品的一个字段，物品的使用链归物品管线。
* **前缀管线**：前缀"能不能挂/挂哪个"归前缀管线；本管线只在"前缀已经挂上之后"改稀有度。
* **Tooltip 管线**：悬停文字的颜色与排版归 Tooltip 管线；本管线只提供 `RarityColor`。
* **安装管线**：静态默认值与首次写表归安装管线。
