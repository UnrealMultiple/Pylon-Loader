# 内容管线：前缀（Prefix）

## 1. 职责

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `Item.GetRollablePrefixes` | 把分类匹配的 mod 前缀并进"这件物品能滚出哪些前缀"的列表 |
| `Item.CanRollPrefix` | `CanRoll`（对 `Custom` 分类的 mod 前缀，游戏自己不认识） |
| `Item.RollAPrefix` | `ModItem.ChoosePrefix` / `GlobalItem.ChoosePrefix`（强制指定）、`RollChance`（加权随机） |
| `Item.TryGetPrefixStatMultipliersForItem` | `SetStats`、`ModifyValue`、`AllStatChangesHaveEffectOn` |
| `Item.Prefix_1` | `ModItem.PrefixChance`（否决 / 强制顶级）、`Apply` |
| `Player.GrantPrefixBenefits` | `ApplyAccessoryEffects`（饰品前缀的每帧效果） |
| `Lang.InitializeLegacyLocalization` | 换语言后重填 `Lang.prefix` 里的 mod 名字 |

**不接**：`ModItem.AllowPrefix` 的独立入口（合进 `GetRollablePrefixes` 的过滤）、前缀 tooltip 行（归 Tooltip 管线）。

前缀是"物品上的一个 byte"，没有实体，也没有每帧路径；难点全在**游戏的原版前缀表**上：分类、可滚列表、数值倍率三张表都是"原版内容写死的"，mod 前缀必须绕着它们接。

## 2. 我们的设计

### 2.1 两个文件

```
Pipelines/Prefix/
  PrefixPipeline.cs   模块：七个游戏入口的分派
  PrefixTable.cs      类型 ↔ 实例、分类索引、名字表、原版表读取（安装期也用它）
```

表拆出去的理由与稀有度相同：**安装期和运行时写的是同一张表**（`Lang.prefix`、`PrefixID.Sets.ReducedNaturalChance`），必须共用一份实现。

### 2.2 分类索引

分类只有七个值（`ModPrefix` 里的常量），所以：

* `Register` 时把实例塞进 `ByCategory[category]`，非法分类退成 `Custom` 并报一条——**不能抛**：注册是一次遍历，抛出去会带走同批的其它内容。
* 物品类型 → 分类数组按 `item.type` 缓存；判定来源两条：原版 `PrefixLegacy.ItemSets` 的布尔表、`ModItem` 的五个 `XxxPrefix` 问询。任一条命中就加入，`Accessory` 走 `IsAPrefixableAccessory`。
* 缓存是安全的：物品的分类不会在运行中改变，而"谁在问"永远是同一件物品。

### 2.3 可滚列表：合并语义

`GetRollablePrefixes` 是这条管线唯一的"改写游戏返回值"的地方，规则按顺序：

1. 取原版列表（`orig`）作为基底；
2. **mod 物品**再并进"它所属分类的原版前缀"——原版只给原版类型填了列表，mod 类型的列表是空的；
3. 再并进"分类匹配且 `CanRoll` 为真的 mod 前缀"；
4. 全程去重（`HashSet`），`AllowPrefix` 为 false 的剔除。

**为什么用合并而不是替换？** 替换会把原版前缀从 mod 物品上抹掉；而合并的代价只是一次 `HashSet` 去重，且"原版列表为空"时天然退化成"只有 mod 前缀"。

### 2.4 加权随机

`RollAPrefix` 的分工：

* **强制**（`ChoosePrefix > 0` 且 `CanRollPrefix` 通过）：直接写 `rolledPrefix` 返回 true；
* **权重**：列表里只要出现过 mod 前缀，就整轮由管线掷——原版前缀权重 1，mod 前缀用 `RollChance`；权重 `<= 0` 的剔除；
* 一个可选项都没有时返回 false（"滚不出前缀"），交回原版是因为此时多半是原版自己的逻辑要否决。

### 2.5 数值与"是否真有效"

`TryGetPrefixStatMultipliersForItem` 对 mod 前缀**不调原版**：先把六个 `out` 参数复位成"无变化"，再交给 `SetStats` 写，最后由管线算 `valueMult`（价格倍率从倍率推出来，作者可用 `ModifyValue` 覆写）。返回值是"这个前缀在这件物品上真的会改变数值"，由 `AllStatChangesHaveEffectOn` 与四个"会不会被取整吃掉"的检查共同决定。

### 2.6 名字表

`Lang.prefix` 与 `PrefixID.Sets.ReducedNaturalChance` 都要按前缀总数扩长。注册期**只记账不写表**（那时语言还没加载，写进去会让启动卡住），写表放在安装期与每次 `InitializeLegacyLocalization` 之后。

## 3. 取舍理由

* **为什么不再照旧实现那样放弃 `GetRollablePrefixes`？** 旧实现注释里写"on-signatures 无此目标"，但 `interop/generated/on-signatures.txt` 里 `On.Terraria.Item :: GetRollablePrefixes` 是存在的。缺了它，mod 前缀永远不会出现在重铸/自然掉落里，等于只能靠指令码挂上去——这是本次重写最重要的一处行为修复。
* **为什么分类判定要反射读 `PrefixLegacy`？** 那两个嵌套类在 interop 里是 internal，没有强类型入口。反射读一次、结果缓存，比"每个物品都猜一遍"便宜得多；读不到就记一条"缺原版条目"，让分类退化成只看 `ModItem` 的问询，而不是崩掉。
* **为什么 `valueMult` 由管线算而不是作者算？** 价格倍率必须与六个倍率一致，否则会出现"很弱的词条卖很贵"。作者只想改价格时用 `ModifyValue` 覆写，默认公式是唯一的。
* **为什么 `AllStatChangesHaveEffectOn` 要参与"能不能装"？** 原版的前缀都存在"对这件物品无效"的情况（例如纯法杖拿到近战体积前缀）。不判这一层，就会出现"挂上了但一点用没有"的前缀，比不给更糟。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| `Category` 越界 | 报一条，按 `Custom` 处理（不抛，避免带走同批内容） |
| 前缀号 > 255 | 报一条；`item.prefix` 是 byte，超过会串号 |
| `CanRoll` / `RollChance` 抛错 | 该前缀这次不参与 |
| `ChoosePrefix` 抛错 | 视为"不指定"，走加权随机 |
| `SetStats` / `ModifyValue` 抛错 | 该前缀视为无效果，`TryGet...` 返回 false |
| `Apply` 抛错 | 前缀已经装上，不回滚 |
| `ApplyAccessoryEffects` 抛错 | 记一条；这一帧这个饰品的效果跳过 |
| `PrefixLegacy` 表读不到 | 记一条；分类只剩 `ModItem` 的问询结果 |

## 5. 扩展方式

新增前缀相关时机：`HookId` 末尾追加 → `ModPrefix` / `ModItem` / `GlobalItem` 加同名 `virtual` → 在 `PrefixPipeline` 对应入口分派。新增一个分类：`ModPrefix` 加常量 → `PrefixTable` 的两处 switch（原版表映射、物品判定来源）各加一条。

## 6. 与其它管线的边界

* **物品管线**：物品的默认值、使用链归物品管线；本管线只写 `item.prefix` 与那六个倍率。
* **稀有度管线**：`Item.Prefix_1` 两边都挂，但一个在"前缀成立之后改 `item.rare`"，一个在"前缀成立之前问 `PrefixChance`、之后调 `Apply`"，互不读对方的中间值。
* **Tooltip 管线**：前缀对 tooltip 的影响归 Tooltip 管线。
* **安装管线**：写表、`SetStaticDefaults`、mod 物品的 `CanGetPrefixes` 标记都在安装期。
