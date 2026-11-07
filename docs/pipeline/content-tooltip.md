# 内容管线：物品提示（Tooltip）

## 1. 职责

把「鼠标停在物品上时那一列文字」变成作者可读可写的数据：

| 环节 | 做什么 |
|---|---|
| 取行 | 拦截游戏算提示行的位置，把 `string[]` 变成 `List<TooltipLine>`（带 mod 名、行名、角色、是否词缀） |
| 改行 | 调 `ModItem.ModifyTooltips` 与所有 `GlobalItem.ModifyTooltips(item, lines)` |
| 隐藏 | `line.Hide()` 之后的行不再交给游戏绘制 |
| 写回 | 把改完的行按原顺序写回游戏的行数组（含 `yoyoLogo`/`research`/`materials`/`setBonus`/`shared` 这些位置标记） |
| 绘制期 | `PreDrawTooltip` / `PostDrawTooltip` 包住整次绘制；行级 `PreDrawTooltipLine` / `PostDrawTooltipLine` 回答「这一行怎么画」 |
| 名称 | 每行都要有稳定名字（`Tooltip0`、`Damage`、`PrefixDamage`…），作者靠它定位行 |

**不接**：`SpriteBatch.DrawString` 级别的逐字钩子（见 2.5）、`ItemTooltip` 的 processor 链（只能逐行改文字、不能加行，见 2.6）。`Item.RebuildTooltip` **已接**（图鉴、搜索这些非悬停路径，见 2.6b）。

## 2. 我们的设计

### 2.1 文件

```
Pipelines/Tooltip/
  TooltipPipeline.cs       模块：取行、改行、写回、绘制期钩子
  TooltipNames.cs          行命名：角色 → 固定名，文本 → 运行时比对
  TooltipLine.cs           公开：一行提示（参考实现同形）
  DrawableTooltipLine.cs   公开：绘制期的一行
```

### 2.2 取行：一次转换，不保留游戏对象

```
On.Main.MouseText_DrawItemTooltip_GetLinesInfo
  原版先跑（拿到它算好的 string[] / preFixLine / badPreFixLine）
  → 转成 List<TooltipLine>（名字由 TooltipNames 现场定）
  → 词缀行定位好插入点 → 调 ModifyTooltips → 过滤 Hide
  → 按原容量写回数组，重算五个位置标记

写回是**就地**的：复用游戏给的数组，不新建——那两次分配正好发生在每次悬停。

### 2.3 行命名：角色优先，文本兜底

1. `ItemName` 固定第一行；`logo`/`research`/`materials`/`setBonus`/`shared` 由位置标记直接给出；
2. 词缀行按「文本结尾是不是某个词缀单位」分类（`PrefixDamage`、`PrefixAccDefense`…）；
3. 剩下的按物品自己的 `ToolTip` 行顺序匹配 `Tooltip0..N`；
4. 再兜底到 `Damage`/`Defense`/`PickPower`/`HealLife` 这些常见名；
5. 都不中就是 `BestiaryNotes`（与原版对齐）。

同名冲突加数字后缀（`Tooltip0`、`Tooltip01`…），保证作者 `FirstOrDefault(l => l.Name == "Damage")` 拿到的是确定的那一行。

### 2.4 位置标记回填

游戏那五个 `ref int` 在写回时全部先置 -1，再按**行对象**重新指：

```
*yoyoLogo = *researchLine = *materialsLine = *setBonusLine = *sharedLine = -1;
foreach (行 in lines)
    写文本；顺带 if (行.Role == "research") *researchLine = 写入下标;
```

不这么做的话，作者在前面插一行，「研究已解锁」的图标就会留在旧下标上，指到别的文字上。

### 2.5 绘制期：托管钩子（D-013 / D-014 / D-015）

绘制期钩子（`PreDrawTooltip` / `PostDrawTooltip`）通过 `On.Terraria.Main.MouseText_DrawItemTooltip` 托管钩子实现。

`Main.MouseText_DrawItemTooltip` 的第二个参数是 48 字节的 `Main.MouseTextCache`，ARM64 上按"隐式引用"传
（寄存器槽里放的是调用者栈副本的指针，见 D-013）。引擎为此做了两件事：

1. **入桩**把原始数据指针记进槽位表，handler 拿到的是一份托管快照（box）；
2. **orig 桩**调用原函数时直接转发原始指针（零拷贝）——游戏在函数内部对结构体的写回
   （`GetLinesInfo` 写行数、写文本）必须落回调用者副本，否则游戏读到 0 行，整条 tooltip 不画（D-014）。

管线契约：**除非作者返回 false，否则一定调用 orig**——任何管线失败都不能让游戏自己的 tooltip 消失。

另外（D-015）：mod 物品 tooltip 整块不显示的另一半根因在名字——游戏悬停时调 `Lang.GetItemNameValue`，
原版实现对 mod 类型返回空串，空名字让 `MouseText` 直接跳过整个 tooltip。`ItemNamePatch` 接管该入口，
mod 类型返回 `ModItem.DisplayName`；介绍缓存走 `ItemTooltip.FromLanguageKey`（键先进游戏语言表，
游戏取文本时走官方 processor 链）。

### 2.6 与 1.4.4 `ItemTooltip` 的关系

游戏的 `ItemTooltip`（processor 链）管的是「提示缓存什么时候失效」，我们不动它。**这一版没接** `Item.RebuildTooltip` 的替换：那需要自己拼一份 `ItemTooltip` 交给缓存，风险（缓存与 `ContentSamples` 的时序）大于收益。因此图鉴、搜索这些**不经过悬停**的读取路径看不到作者在 `ModifyTooltips` 里加的行；要走那条路，得先补这个环节。

**已核对的接口（不是猜测）**：

* `Item.RebuildTooltip` **存在且可钩**（`refs/dump.cs:49395`；`on-signatures.txt` 里有 `On.Terraria.Item :: RebuildTooltip`）——上一版"本构建没有这个入口"的说法不成立。
* `Item.ToolTip` 是一个 `ItemTooltip` 对象（`refs/dump.cs:49232`，字段 `public ItemTooltip ToolTip`），有 `Lines` / `GetLine(i)` 读、`ItemTooltip.FromHardcodedText(string[])` 造。
* `ItemTooltip.AddGlobalProcessor(TooltipProcessor)` 是**逐行**处理器（`TooltipProcessor.Invoke(short tooltipId, string tooltip) → string`），只能改某一行的文字，**不能加行**——所以它替代不了 `RebuildTooltip`。

接这条要做的事：在 `RebuildTooltip` 之后读出原版那几行 → 跑一遍 `ModifyTooltips` → 用 `FromHardcodedText` 造一份新的 `ItemTooltip` 写回 `item.ToolTip`。卡点在调用频度：这个钩子远比悬停勤（物品一建出来就会调），所以见 2.6b 的三道闸。

### 2.6b `Item.RebuildTooltip`：已接，带三道闸

`On.Terraria.Item.RebuildTooltip` 后置：读 `item.ToolTip` 原有行 → `TooltipNames` 起名 → `ApplyOverrides` + `ModifyTooltips`（与悬停路径同一套，行为一致）→ 过滤 `Hide` → `ItemTooltip.FromHardcodedText` 写回。

三道闸（按开销从小到大）：

1. **内容没装载完直接放行**——作者的钩子依赖的东西还不存在；
2. **没有 GlobalItem 接 `ModifyTooltips`** 时按类型查一张 `bool[]`：原版物品直接放行（`Find(ContentKind.Item, i) is ModItem` 预填，懒增长），不给普通物品加任何字符串处理；
3. **有全局钩子**时才对每一件物品都跑（全局作者自己选了这个代价）。

**为什么后置而不是前置**：前置要自己重算词缀、稀有度、研究这些原版行——那是一整套原版逻辑的复刻；后置拿到的是原版算好的行，我们只做「改」。

**已知局限（写明不装懂）**：写回的是一份新 `ItemTooltip`，游戏缓存与 `_validatorKey` 的失效时序我们没碰；`yoyoLogo` 等位置标记在 `ItemTooltip` 里不存在（它只是 `string[]`），所以这些特殊行的图标依赖悬停路径补。

## 3. 取舍理由

* **为什么不建静态的原版文本表？** 那张表要跟着游戏版本走，每更新一次就是几百行要重对；运行时比对只依赖 `Lang.tip[]` 的下标语义，改一次匹配规则就够。代价是每次悬停要比几十次字符串——这个量级比一次 `SpriteBatch.Draw` 便宜得多。
* **为什么 `Hide()` 而不给 `Remove`？** 删除是不可逆的，「我把某行删了」和「我没加这行」在作者看来该是同一件事；`Hide` 让管线在最后一刻统一过滤，`GlobalItem` 之间也就不会互相踩。
* **为什么行级钩子允许慢一帧？** 拦绘制要挂 XNA，代价是全游戏的文字绘制都多一层间接；收益只是少数作者想逐字改。悬停状态下慢一帧没人看得出来，而挂框架是全局代价——这笔账怎么算都不划算。
* **为什么不缓存取行结果？** 参考实现按物品指针缓存 `Prepared`，因为它的绘制段会再问一次行。我们的绘制段直接复用上一次取行的 `_lines`，一次悬停只跑一遍 `ModifyTooltips`，不需要第二份缓存。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| `ModifyTooltips` 抛错 | 记一条，其它物品与其它作者的钩子照常 |
| 某行 `Text` 为 null | 当空串写回，不写 `null` 进游戏数组 |
| 行数超过游戏数组容量 | 截断（多余的行丢弃），并在诊断里说明 |
| 作者 `PostDrawTooltipLine` 抛错 | 记一条，该行按原样画 |
| `PreDrawTooltip` 返回 false | 原版不画，`PostDrawTooltip` 仍然调 |
| 取行阶段抛错 | 整条管线放弃这一次悬停，原版表现不变 |

## 5. 扩展方式

新增一种可识别的原版行：在 `TooltipNames` 加一条匹配，名字立刻对所有作者可见。新增一个行级字段：`DrawableTooltipLine` 加字段、绘制段读它——不影响命名与写回。新增一种内容类型的提示（Buff/图鉴）：另开一条管线，复用 `TooltipLine` 这一个数据结构。

## 6. 与其它管线的边界

* **物品管线**：`ModItem.Tooltip`/`applyTooltip` 决定 `Item.ToolTip` 的原始行；本管线只在其上增删改（悬停与非悬停两条路径都过 `ModifyTooltips`）。
* **词缀管线**：词缀行由游戏生成、本管线只负责命名与位置（`IsModifier`/`IsModifierBad`）；`PrefixLoader` 提供的额外行插在最后一个词缀行之后。
* **稀有度管线**：名字颜色由稀有度给；本管线不重算颜色，只在作者显式 `OverrideColor` 时覆盖。
* **UI 管线**：屏幕只读 `TooltipLine`，不参与命名与写回。