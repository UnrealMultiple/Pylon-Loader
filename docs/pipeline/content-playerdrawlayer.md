# 内容管线：玩家绘制层（PlayerDrawLayer）

## 1. 职责

| 作者入口 | 生效时机 |
|---|---|
| `PlayerDrawLayer` + `GetDefaultPosition()` | 插进原版逐层绘制链的指定位置（两次原版调用之间） |
| `PlayerDrawLayer.Hide()`（在 `ModPlayer.HideDrawLayers` 里调） | 这一帧不画该层 |
| `ModPlayer.ModifyDrawLayerOrdering(positions)` | 整表排序时改任意层的层位 |
| `ModPlayer.DrawEffects` / `ModifyDrawInfo` / `TransformDrawData` / `DrawPlayer` / `ModifyScreenPosition` | 绘制数据链上的五个时机 |
| `ModPlayer.ModifyZoom(ref float)` / `ModPlayer.ModifyZoom(ref Vector2)` | 相机缩放，每帧一次（值变化才写回） |

## 2. 层位

```csharp
public sealed class MyLayer : PlayerDrawLayer
{
    public override Position GetDefaultPosition() => new Between(PlayerDrawLayers.Torso, PlayerDrawLayers.OffhandAcc);

    protected override void Draw(ref PlayerDrawSet drawInfo) { }
}
```

| 位置类型 | 语义 |
|---|---|
| `new Between(a, b)` | 画在 a 之后、b 之前（任一端给 null = 链首/链尾） |
| `new BeforeParent(p)` / `new AfterParent(p)` | 挂成 p 的子层，跟 p 一起画、一起套变换 |
| `new Multiple { {between1, 条件1}, {between2, 条件2} }` | 同一层按每帧条件落在不同位置 |
| `PlayerDrawLayers.BeforeFirstVanillaLayer` / `AfterLastVanillaLayer` | 整条链的首尾 |

层实例都放在 `PlayerDrawLayers`（54 个原版层 + 首尾锚点）。**命名空间是 `Pylon.ModLoader`**：interop 里已经有一个 `Terraria.DataStructures.PlayerDrawLayers` 静态类，同名同命名空间会冲突，写代码时加一行别名即可：`using PlayerDrawLayers = Pylon.ModLoader.PlayerDrawLayers;`

## 3. 变换与可见性

```csharp
public override Transformation Transform => PlayerDrawLayers.TorsoGroup;   // 躯干组；MountGroup 是它的子变换
public override bool IsHeadLayer => false;                                 // 头部层（会被头盔/发型那套特殊处理）
```

可见性每帧重置：`GetDefaultVisibility(drawInfo)` 给默认值，`ModPlayer.HideDrawLayers(drawInfo)` 里可以对任意层调 `Hide()`。

## 4. 插链层怎么画

插链层**不要直接往 `Main.spriteBatch` 画**：这个阶段真正被提交渲染的是 `drawInfo.DrawDataCache`（原版的每一层都是往这个数组里塞一条 `DrawData`）。

```csharp
var texture = TextureAssets.Item[ItemID.Muramasa].Value;
ModPlayer.AddDrawData(drawInfo, new DrawData(texture, drawInfo.Position - Main.screenPosition, Color.White));
```

两个手机版专有的坑：`DrawData` 的构造函数**没有 sourceRect 参数**（PC 版有），三参构造即可；缓存是定长原生数组，写之前必须走 `ModPlayer.AddDrawData`（它检查下标），自己算下标会踩内存。

## 5. 实现原理

### 5.1 锚点分桶而不是接管整条链

原版 `DrawPlayer_UseNormalLayers` 是一串硬编码调用。我们不去接管它，而是在**每个原版层的方法上各挂一个钩子**（54 个），钩子进入时先跑"这一层之前"槽位里的作者层、再调 `orig`、最后跑"之后"的槽位。原版的调用顺序与条件判断一个字节不改，作者层天然落在两次原版调用之间。

### 5.2 排序

`PlayerDrawLayerLoader.Rebuild()`：收每个层的 `GetDefaultPosition()` → 让 `ModPlayer.ModifyDrawLayerOrdering` 改 → 把 `BeforeParent`/`AfterParent` 折成 `Between`（并登记成子层）、`Multiple` 展开成带条件的槽位 → 按 `Between` 约束做拓扑排序 → 每个作者层落在"下一个原版层之前"的槽位（没有下一个就落在最后一层之后）。成环时报一次并按注册序兜底。

### 5.3 每帧

第一个原版层的钩子里重置一次可见性并问 `ModPlayer.HideDrawLayers`；之后每个原版层：跑前槽位 → （没被 `Hide()` 才调 `orig`）→ 跑后槽位。作者层按"变换推入 → 前子层 → 自己 → 后子层 → 变换还原"执行。

## 6. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 没有作者层 | 完全不介入（每层只多一次数组判断） |
| 某层 `Draw` 抛异常 | 记一次，继续下一个层；玩家照画 |
| `GetDefaultPosition` 抛异常 | 这一层不参与排程，记一次 |
| `ModifyDrawLayerOrdering` 抛异常 | 记一次，用默认层位 |
| 层位成环 | 报一次，其余按注册序 |
| `ModifyZoom` 给出 ≤0 / NaN | 保留原值（缩放退化会黑屏） |
