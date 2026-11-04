# 内容管线：增益（Buff）

## 1. 职责

| 游戏时机 | 分派的作者钩子 |
|---|---|
| `Player.UpdateBuffs` / `NPC.UpdateNPC_BuffSetFlags` | `UpdatePlayer`（玩家身上的 mod buff）、`UpdateNPC`（NPC 身上）、`GlobalBuff.UpdatePlayer` / `UpdateNPC`（所有 buff，含原版） |
| `Player.AddBuff_ActuallyTryToAddTheBuff` | `ApplyPlayer`（原版真的加上之后） |
| `NPC.AddBuff` | `ApplyNPC`（只有这次真的新增才调） |
| `Player.AddBuff_TryUpdatingExistingBuffTime` | `ReApplyPlayer`（false = 不续时间）、`GlobalBuff.ReApplyPlayer`（true = 不续时间，与 ModBuff 相反） |
| `NPC.AddBuff` 同理的续时路径 | `ReApplyNPC`、`GlobalBuff.ReApplyNPC` |
| `Lang.GetBuffName` / `Main.GetBuffTooltip` | `ModifyBuffText`（名字 / 说明 / 稀有度）、`GlobalBuff.ModifyBuffText` |
| `Main.DrawBuffIcon` | `PreDraw`（false = 图标与时间都不画）、`PostDraw`、`GlobalBuff.PreDraw` / `PostDraw` |
| `Player.DelBuff` | `CanRemove`（false = 留着）、`OnRemove`、`GlobalBuff.RightClick` |

**不接**：buff 的存档与网络同步（归网络管线）、`ModBuff.HideFromModMenu` 这类只给菜单读的字段、`GlobalBuff.CustomBuffTipSize` / `DrawCustomBuffTip`（手机版没有可挂的 tip 尺寸入口）。

buff 与其它内容最大的不同：**它没有实体**。"一个 buff" 是"类型 + 剩余时间"两个整数，挂在玩家或 NPC 的数组里。所以这条管线没有默认值接管、没有实体绑定，只有一个类型表加一条分派链。

## 2. 我们的设计

### 2.1 一个文件

```
Pipelines/Buff/
  BuffPipeline.cs   模块：效果 / 施加 / 续时 / 文字 / 图标 / 取消
```

没有 `BuffDefaults`、没有 `BuffBinding`：buff 不参与 `SetDefaults` 分派（它的静态值由安装管线写表），也没有实体可以绑定。硬塞两个空壳文件只会让人以为漏了什么。

### 2.2 两层数据

| 层 | 生命周期 | 存哪 |
|---|---|---|
| 模板 | 进程 | `ContentRegistry` 槽表（`ContentKind.Buff` 槽数组，下标即 `type - 401`） |
| 覆写位集 | 类型 | `ModBuff.Hooks`，安装时按 `HookScan.Of(类型, typeof(ModBuff))` 算一次并挂在模板上 |

**为什么位集要按 `typeof(ModBuff)` 算？** 与 NPC 同理：以 `ModType` 为基类时，`ModBuff` 自己声明的空 `virtual` 会被算成覆写，门控就失效了——而 buff 的 `UpdatePlayer` 是每帧每个 buff 都走的路径。

### 2.3 分派规则

* **只对"游戏这次处理的槽位"分派**（`UpdateBuffs(i)` 的 `i`），不再在每次调用里遍历整张 buff 表。遍历全表会让同一 buff 在一帧里被跑 N 次（N = 身上 buff 数），作者的 `UpdatePlayer` 会成倍生效。
* `UpdateNPC` 没有槽位参数（游戏在方法内部循环），所以那时按整张表遍历一次——这次调用本身每帧每 NPC 只有一次。
* 顺序：**本 buff 先，Global 后**；`ReApplyPlayer` 反过来先问 Global（任一返回 true 就不续时间），因为"续不续"是全局策略。
* 门控：`mod.HookEnabled(HookId.X)` 是位运算；Global 用 `GlobalBuffRegistry.For(hook)` 取预先建好的订阅者表。

### 2.4 索引可写性

`GlobalBuff` 的 `buffIndex` 是 `ref int`，作者把它减 1 表示"这个 buff 被移掉了"；管线在遍历时按新索引继续（`k = index`），不会漏掉被前移过来的 buff。`ModBuff` 的签名没有 `ref`（作者 API 已定），所以它对索引的修改只能在当次调用内生效——这一点写进 API 注释，不让作者误以为能移掉 buff。

### 2.5 与安装管线的边界

buff 的 `SetStaticDefaults`（写 `Main.debuff[Type]`、`buffNoSave`、`BuffID.Sets`）由安装管线在 `Static` 阶段跑；名字、说明、`buffImmune` 扩容也都是安装期的事。本管线只做"游戏问到我时转给作者"，另外提供一个 `GrowImmuneArrays` 给安装步骤调用（`buffImmune` 的扩容要对所有实例生效，属于表操作）。

## 3. 取舍理由

* **为什么不像其它管线那样有 Binding？** buff 没有实体。给 buff 造"实例"只会多一层间接，而且没有生命周期可依附：`buffType[i]` 变成同一个类型的另一格时，作者看到的东西应该完全一样。
* **为什么只对当前槽位分派？** 这是本管线与旧实现唯一的行为差异：旧实现每次 `UpdateBuffs` 都遍历整张表，于是"3 个 buff 的玩家"每帧会收到 3 次 `UpdatePlayer`。按槽位分派后每个 buff 每帧恰好一次，且不引入任何状态。
* **为什么 `ApplyNPC` 要判断"是不是新增"？** `NPC.AddBuff` 返回 void，"加上了"和"本来就有"在签名上分不开。管线在调用前记下 `FindBuffIndex` 的结果，调用后仍在且之前没有，才认作新增；否则 `ApplyNPC` 会在每次续时间时重跑，作者写"播放音效/生成粒子"就会重复。
* **为什么保留 `GrowImmuneArrays`？** 它扩的是 `Player.buffImmune` / `NPC.buffImmune` 这两个**实例**数组，不是类型表，安装管线的"写表"阶段碰不到新生成的实例；放在管线里，安装步骤与新实例都可以复用它。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| `UpdatePlayer` / `UpdateNPC` 抛错 | 记一条；这一帧这个 buff 的效果跳过，buff 本身照常计时 |
| `GlobalBuff.UpdatePlayer` 抛错 | 只丢这一个订阅者 |
| `ApplyPlayer` / `ApplyNPC` 抛错 | 记一条；buff 已经加上，不回滚 |
| `ReApplyPlayer` 抛错 | 视为可续时间（按原版） |
| `ModifyBuffText` 抛错 | 用原版的名字与说明 |
| `PreDraw` 抛错 | 图标照画 |
| `CanRemove` 抛错 | 视为 null（交给游戏规则） |
| `OnRemove` 抛错 | 记一条；移除已经发生 |

## 5. 扩展方式

新增一个 buff 钩子：`HookId` 末尾追加 → `ModBuff` / `GlobalBuff` 加同名 `virtual` → 在 `BuffPipeline` 对应游戏钩子里分派 → `ModBuff.Hooks` 会自动带上它（`HookScan` 按名字匹配，无需改注册代码）。

## 6. 与其它管线的边界

* **玩家 / NPC 管线**：`ResetEffects` 归那两条管线；本管线只管 buff 自身的效果链。NPC 的 `UpdateNPC_BuffSetFlags` 两边都挂，但一个跑 `ResetEffects`、一个跑 buff 效果，互不读对方的字段。
* **物品管线**：物品加 buff 走 `Player.AddBuff`，钩子在这里；物品本身的使用链归物品管线。
* **网络管线**：`ModPacket` 与 buff 同步归网络管线；本管线不写 `NetWriter`。
* **Tooltip 管线**：buff 条的说明文本走 `ModifyBuffText`，不走物品 tooltip 那条链。
