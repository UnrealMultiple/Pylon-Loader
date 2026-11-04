# 内容管线：装备贴图（EquipTexture）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（`Static`，在作者 `SetDefaults` 之前） | 跑一次 `ModItem.Load`（作者在那里 `AddEquipTexture`）→ 收表 → 按部位扩表 → 发号 → 把贴图写进槽位 |
| 运行时 | 作者把 `item.headSlot/bodySlot/legSlot` 设成 `EquipTexture.Slot`，绘制走游戏自己的盔甲绘制 |
| 每帧（`Player.UpdateEquips` 之后） | 盔甲三部位：`FrameEffects` + 时装套（`IsVanitySet` → `PreUpdateVanitySet` → `UpdateVanitySet` → `ArmorSetShadows`） |
| 每帧（`ApplyEquipFunctional` / `ApplyEquipVanity`） | 15 个部位：`UpdateEquip` + `UpdateAccessory`（穿戴栏）、`UpdateVanity`（时装栏） |
| 换装判定（`Player.SetMatch`） | 盔甲三部位：`SetMatch(male, ref equipSlot, ref robes)` |
| 翅膀动画（`Player.WingFrame`） | 翅膀：`WingUpdate(player, wingFlap)`（返回 true 由作者接管帧） |
| 物品生成（`Item.SetDefaults` 之后） | 自动把槽位写进 `item.headSlot/backSlot/wingSlot/…`——作者不用自己写 |

部位覆盖 15 个：头盔 / 胸甲 / 护腿 / 手部前后 / 背 / 胸 / 鞋 / 腰 / 翅膀 / 盾 / 项链 / 面 / 气球 / 胡子；胸甲与手部还会同时写复合层与手臂层。

**已接**：`VerticalWingSpeeds`（`Player.WingMovement` 本构建存在，按原版公式重算 `velocity.Y`）、`HorizontalWingSpeeds`（`Player.WingAirLogicTweaks`）。

**不接**：`CanAccessoryBeEquippedWith` 的槽位判定入口。

## 2. 我们的设计

### 2.1 一条数据通路，不挂钩子

盔甲贴图在游戏里是**按槽位索引的表**（`TextureAssets.ArmorHead` / `ArmorBody` / `ArmorLeg`），物品上又有对应的 `headSlot` / `bodySlot` / `legSlot` 字段。把表扩长、把作者贴图写进新槽位、让作者的物品指向那个槽位——**绘制完全走游戏自己的路径**，本管线一个绘制钩子都不挂。

这条通路是这一版能交付的根本原因：它不需要"往游戏的装备绘制流程里插队"。

### 2.2 每个部位一个扩表种类

`TypeTables.TableGrower` 的判据是"当前长度等于该种类原版长度"，而各部位的表长互不相同（`AccBack`、`AccShield`、`Wings` 各是各的长度），所以**每个部位各有一个扩表种类**，各按自己的原版表长扩。

十五个部位之外还多四个"平行层"种类：`ArmorBodyComposite` / `ArmorArm`（胸甲的手与复合层）、`AccHandsOnComposite` / `AccHandsOffComposite`（手部前后复合层）。它们跟主表用同一张贴图写入——作者只提供一张图，不必分别为复合层准备资源。

作者声明了 0..14 之外的部位时：**报错并跳过**（不静默丢弃，也不发一个用不了的号）。

### 2.3 作者怎么拿到槽位

`AddEquipTexture` 返回 `EquipTexture` 实例，作者把它存在字段里，在 `SetDefaults` 里写：

```
protected override EquipTexture Helm = AddEquipTexture("Helm", EquipType.Head, "Textures/Helm.png");
public override void SetDefaults(Item item) { item.headSlot = Helm.Slot; ... }
```

所以顺序必须是**先发号、后 `SetDefaults`**：本管线的安装步骤放在 `Static` 阶段（作者 `SetDefaults` 在 `Samples` 阶段跑）。

### 2.4 `ModItem.Load` 现在真的会被调用

这个仓库此前没有任何地方调用 `ModItem.Load`（声明在、调用无）。本管线补上这个调用点：安装期对每个 `ModItem` 跑一次（用集合去重，`Refresh` 不会重复跑）。这也是 `AddEquipTexture` 唯一合法的调用位置。

### 2.5 效果回调覆盖全部 15 个部位

之前"饰品没有槽位字段"的判断是**错的**：`dump.cs` 里 `Item` 有 `handOnSlot` / `backSlot` / `frontSlot` / `shoeSlot` / `waistSlot` / `wingSlot` / `shieldSlot` / `neckSlot` / `faceSlot` / `balloonSlot` / `beardSlot` / `frontSlot`（0xC4–0xCF 一整段 sbyte）。所以：

* **槽位自动写入**：物品生成时管线把 `AddEquipTexture` 注册的槽写进对应字段，作者不用在 `SetDefaults` 里手写（写了也不冲突，会被覆盖成同一个值）。
* **效果回调**：挂 `ApplyEquipFunctional`（穿戴栏：`UpdateEquip` + `UpdateAccessory`）与 `ApplyEquipVanity`（时装栏：`UpdateVanity`），按物品编号反查它带的槽，逐槽派发。
* **`SetMatch`**：挂 `Player.SetMatch`，盔甲三部位按作者声明改画到哪个槽/是否长袍。
* **翅膀动画**：挂 `Player.WingFrame`，作者的 `WingUpdate` 返回 true 就接管帧。

### 2.5 扩表会连带扩同长度的数组

`TableGrower` 的判据是"长度等于原版长度"，不是字段名。所以某个部位扩表时，`TextureAssets` 里**恰好同长度**的别的数组也会被一起扩到同一容量（尾部补 null）。这是现有设施的既有行为（云/发型/水样式同理），这里显式记一笔。

## 3. 取舍理由

* **为什么不挂钩子画？** 见 2.1：数据通路已经够用，挂钩子等于把游戏的盔甲绘制重写一遍。
* **为什么只做三个部位？** 见 2.2：不是"想做少"，是每个部位要一个扩表种类。
* **为什么先发号再 `SetDefaults`？** 见 2.3：作者要在 `SetDefaults` 里读 `Slot`。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 没有装备贴图 | 管线不介入（不扩表、不写槽） |
| `ModItem.Load` 抛异常 | 记一次，该物品的贴图不注册 |
| 部位不在三部位内 | 报错并跳过这一张 |
| 贴图路径为空 / 资源为空 | 记警告，槽位留 null（穿着时不可见，不崩） |
| 槽位超出表长（理论上不会） | 记警告，不写 |
| 同一个物品 `Load` 跑两次 | 不会：集合去重 |

## 5. 扩展方式

接饰品部位：给该部位加一个 `TableKind`（classes 指向 `TextureAssets`）、一个 `TextureTable` 成员、一个 `Vanilla` 属性，然后在 `EquipTable.Parts` 里加一项——管线的其余部分不用改。接效果钩子：在游戏的对应位置（`FrameEffects` 每帧、`VerticalWingSpeeds` 振翅时）挂上，再派给 `EquipTexture` 的同名虚方法（契约里这些方法已经写好）。

## 6. 与其它管线的边界

* **物品（3）**：物品本体归物品管线；本管线只写 `*Slot` 指向的那张图。
* **玩家绘制层（33）**：那条管"玩家前后加画东西"，与装备贴图无关。
* **贴图设施**：扩表与写槽归 `TypeTables` / `TextureSlots`，本管线只调用。
* **发型 / 水样式 / 云**：同一套"扩表 + 写槽"模式，互不影响。

### 这一版没接

1. ~~**翅膀速度的"多段逻辑"**~~：**已接**——`EquipPipeline.SeedVanilla` 照着原版每个 `wingsLogic` 分支（26/37、8/11/24/27/22、21/12/20/23、44、45、29/32、30/31）填打底数值，作者的 `VerticalWingSpeeds` 在这个基础上改；悬停翼（44/45）另叠 `Hover`，悬停下压时 `wingTime` 只扣 0.5。
2. **`PickItemMovementAction` / `ArmorSwap` 那组拖拽入口**：饰品互斥已经挂到 `CanEquipBothAccessories`（拖进去和点着换都会问），但这组更细的移动动作没挂。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 新增 | `EquipType` 枚举（15 个部位：头盔…胡子） |
| 新增 | `ModItem.AddEquipTexture(name, type, texture)` → 返回 `EquipTexture`，在 `Load` 里调 |
| 变更 | `ModItem.Load` 现在真的会被调用一次（安装期 `Static` 阶段） |
| 语义明确 | `EquipTexture.Slot` 在 `SetDefaults` 之前就已定好，可以直接读 |
| 生效 | 15 个部位的效果回调（`UpdateEquip` / `UpdateAccessory` / `UpdateVanity`） |
| 生效 | 盔甲三部位：`FrameEffects` + 时装套 + `SetMatch` |
| 生效 | 翅膀：`WingUpdate` 接管动画帧 |
| 生效 | 槽位自动写入：物品生成时 `*Slot` 字段由管线填，作者不用手写 |
| 新增 | `ModItem.GetEquipSlot(part, name)`：按注册名查槽位 |
| 生效 | 翅膀速度：`VerticalWingSpeeds`（`Player.WingMovement`）+ `HorizontalWingSpeeds`（`Player.WingAirLogicTweaks`），按参考实现的公式重算 `velocity.Y` |
| 新增 | `ModItem.SetWingStats`（定值路线，和上面的逐帧回调二选一） |
