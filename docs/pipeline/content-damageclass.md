# 内容管线：伤害类别（DamageClass）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（`Static`） | 收"物品 → 类别"映射 + `SetStaticDefaults` |
| 每次算武器伤害 | `DamageMultiplier` |
| 每次算武器暴击 | `CritBonus` |
| 每次算武器击退 | `KnockbackMultiplier` |

**不接**：玩家侧类别加成（饰品 / buff / 药水的汇总乘区）、攻速与穿透、不同类别各自的伤害数字颜色、类别的玩家属性同步、`DamageClass` 之间的继承与组合（近战/远程那种"包含"关系）。

## 2. 我们的设计

### 2.1 三个文件 + 一处契约补充

`Content/ModDamageClass.cs`（作者基类）、`Pipelines/DamageClass/DamageClassTable.cs`（物品 → 类别）、`Pipelines/DamageClass/DamageClassPipeline.cs`（三个乘区），外加 `ModItem.DamageClass` 一个新成员。

### 2.2 为什么自建

这个构建的 `Item` 没有 `DamageType` 字段，也没有物品 → 伤害类别的映射（`dump.cs` 里 `DamageClass` 类型根本不存在）。所以不存在"接上"一说：类别体系本身就得由 loader 定义。

### 2.3 三个乘区怎么套

挂 `Player.GetWeaponDamage` / `GetWeaponCrit` / `GetWeaponKnockback` 三个入口，在游戏算完之后按类别改：

```
damage  = 原值 × DamageMultiplier（向下取整，大于 0 时至少留 1）
crit    = 原值 + CritBonus
knockback = 原值 × KnockbackMultiplier
```

**放在 `orig` 之后**：先让游戏把原版的武器数值算完（含它自己的加成），再乘类别系数。这样类别是"最后一道乘区"，不会和原版的加成顺序打架。

### 2.4 映射按物品编号

表是 `物品 type → 类别`，在安装期从所有 `ModItem` 的 `DamageClass` 声明里收。原版物品不参与（它们的 `DamageClass` 声明在 loader 之外），这也意味着**本版不能给原版武器挂自定义类别**。

作者要在 `SetDefaults` 之外声明类别，覆盖 `DamageClass` 属性即可：

```
public override ModDamageClass DamageClass => ModContent.GetInstance<MyClass>();
```

### 2.5 数值下限

伤害乘区之后如果算出 0 而原值大于 0，就留 1。理由：类别系数把人打死不该让武器变成"打不出伤害"，这在测试时极难定位。

## 3. 取舍理由

* **为什么不做玩家侧汇总？** 那需要拦 `Player` 的类别属性、饰品与 buff 的 `ModifyWeaponDamage` 系列，还要缓存排序；这是参考实现那 502 行的大头。本版先把"物品有类别、类别有系数、系数真的生效"这条链做实。
* **为什么不做继承/组合？** 组合要解决"近战包含近战无攻速"这种包含关系下的重复计数，属于另一层设计。
* **为什么乘区放在最后？** 见 2.3：避免和原版加成顺序互相覆盖。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| `DamageClass` 属性抛异常 | 记一次，这件武器不参与乘区 |
| `DamageMultiplier` 抛异常 | 记一次，用原值 |
| `CritBonus` / `KnockbackMultiplier` 抛异常 | 同上，各自回退到原值 |
| 乘区算出 0 而原值 > 0 | 留 1 |
| 物品没声明类别 | 原样返回，不介入 |

## 5. 扩展方式

接玩家侧加成：给 `ModPlayer` 加"类别加成"的收集口，在 `GetWeaponDamage` 里把玩家加成和类别系数一起算；需要一份每帧清空的累加器。
接攻速：改 `Item.useTime` / `useAnimation` 的读取入口，按类别系数缩放。

## 6. 与其它管线的边界

* **物品（3）**：`DamageClass` 是物品上的一个声明，物品本身的数值归物品管线。
* **玩家（27）**：玩家侧属性归玩家管线，本版不碰。
* **Tooltip（33）**：类别名字要不要显示在提示里，归提示管线。
* **场景效果**：无关。

### 这一版没接

1. **玩家侧类别加成**（饰品 / buff / 药水汇总）。
2. **攻速与穿透**。
3. **类别继承与组合**（近战 ⊃ 近战无攻速 那类关系）。
4. **原版武器挂自定义类别**（映射只收 mod 物品）。
5. **类别伤害数字的颜色与提示文本**。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 新增 | `ModDamageClass` 基类：`DamageMultiplier` / `CritBonus` / `KnockbackMultiplier` |
| 新增 | `ModItem.DamageClass`（默认 null = 不参与） |
| 语义明确 | 三个乘区都作用在游戏算完之后，是最后一道 |
| 本版不生效 | 玩家侧汇总、攻速穿透、继承组合、原版武器、提示文本 |
