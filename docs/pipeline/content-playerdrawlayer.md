# 内容管线：玩家绘制层（PlayerDrawLayer）

## 1. 职责

| 作者入口 | 生效时机 |
|---|---|
| `ModPlayerDrawLayer`（无 `Position`） | 整玩家画之前（`DrawOverPlayer == false`）或之后（true），每个玩家一次 |
| `ModPlayerDrawLayer`（有 `Position`） | 插在原版逐层绘制链的指定位置，夹在头盔与胸甲之间 |
| `ModPlayer.HideDrawLayer` | 原版某一层不调 `orig`（54 层逐层判定） |
| `ModPlayer.DrawEffects` / `ModifyDrawInfo` / `TransformDrawData` / `DrawPlayer` / `ModifyScreenPosition` | 玩家绘制数据链上的五个时机 |
| `ModPlayer.ModifyZoom` | 相机缩放，每帧一次（值变化才写回） |

## 2. 层的两种形状

### 2.1 两个层位（默认）

不写 `Position` 时，层只有"整玩家之前 / 之后"两个位置：

```csharp
public override bool DrawOverPlayer => true;   // 身前
public override void Draw(SpriteBatch spriteBatch, Player player, Vector2 position, float rotation, float scale) { }
```

管线挂 `LegacyPlayerRenderer.DrawPlayer`，参数（屏幕位置、旋转、缩放）直传给作者，不反算。

### 2.2 插进原版链（有位置）

写了 `Position` 的层由 `PlayerDrawLayerChain` 插进原版逐层绘制链：

```csharp
public override LayerPosition Position => LayerPosition.Between(VanillaDrawLayer.Torso, VanillaDrawLayer.OffhandAcc);
public override LayerTransform Transform => LayerTransform.Torso;
public override void Draw(ref PlayerDrawSet drawInfo) { }
```

可用的锚点：

| 工厂方法 | 语义 |
|---|---|
| `LayerPosition.Before(VanillaDrawLayer.X)` | 画在 X 之前 |
| `LayerPosition.After(VanillaDrawLayer.X)` | 画在 X 之后 |
| `LayerPosition.Between(a, b)` | 画在 a、b 之间（顺序写反会纠正并报一次） |
| `LayerPosition.AfterMod("名字")` / `BeforeMod("名字")` | 在另一个 mod 层之后/之前（同一槽位内再排依赖） |

`Transform` 决定这一层套哪种变换（`None` / `Torso` / `Mount`，可组合）：套了变换才会在画之前推 `DrawPlayer_extra_TorsoPlus` / `MountPlus`、画之后按相反顺序还原，否则层的内容不会跟身体一起位移。

## 3. 实现原理

### 3.1 为什么逐层挂钩子，而不是接管整条链

原版 `PlayerDraw_RenderAllLayers` 是一串硬编码的方法调用（`DrawPlayer_01_BackHair`、`DrawPlayer_02_MountBehindPlayer`……）。接管整条链要重写这串调用并复刻每个层的条件与变换，风险高。

这里用的是**锚点分桶**：

1. 每个原版层的方法本来就会被游戏调用一次，我们已经在这些方法上挂了钩子（隐藏层也用它）；
2. 钩子进入时先跑"这一层之前"槽位里的作者层，调 `orig`，再跑"这一层之后"槽位里的作者层；
3. 于是作者层天然落在两个原版层之间，原版链本身一个字节没改。

槽位是两张表：`_before[(int)VanillaDrawLayer]` 与 `_after[(int)VanillaDrawLayer]`，按 `VanillaDrawLayer` 枚举下标索引。

### 3.2 收表与排序

`PlayerDrawLayerTable.Rebuild` 把层分成三堆：`Behind`、`Front`（无位置）与 `Positioned`（有位置）。`Positioned` 交给 `PlayerDrawLayerChain.Build`：

* 锚点决定桶：`Before(V)` → `_before[V]`，`After(V)` → `_after[V]`，`Between(a,b)` → `_after[a]`；
* 同一桶里若有人写了 `AfterMod` / `BeforeMod`，在这个桶内做一次拓扑排序（没有依赖时保持注册序，零分配）；
* 依赖指向桶外的层、或依赖成环，报一次并按注册序兜底。

### 3.3 每帧的开销

`PlayerDrawLayerChain.Count == 0`（没人用位置）时，`Before` / `After` 第一次判断就返回，隐藏层的判定不变。有人用位置时，每个原版层多一次数组下标与空判断。

### 3.4 失败边界

每个层单独 `try/catch`：`Position` 抛异常按"没有位置"处理，`Draw` 抛异常记一次并继续下一个层，`Visible` 抛异常按不可见处理。玩家本体照画。

## 4. 绘制信息（`PlayerDrawSet`）

| 钩子 | 挂在哪 | 为什么是这个点 |
|---|---|---|
| `DrawEffects(player, info, ref r,g,b,a, ref fullBright)` | `PlayerDrawSet.BoringSetup` 之后 | 原版在这里把所有颜色字段算完，改倍率才有东西可改 |
| `ModifyDrawInfo(player, info)` | 同上，紧接 `DrawEffects` | 作者要改的是「颜色之后、各层建起来之前」的那份 drawInfo |
| `TransformDrawData(player, info)` | `PlayerDrawLayers.DrawPlayer_RenderAllLayers` 之前 | 这时 `DrawDataCache` 已经填满、还没进渲染缓存，改它才有效 |
| `DrawPlayer(player, camera)` | `LegacyPlayerRenderer.DrawPlayerFull` 之后 | 「整个玩家画完」的时机，含相机 |
| `ModifyScreenPosition(player)` | `Main.DoDraw_UpdateCameraPosition` 之后 | 相机已经跟到目标，这时挪屏幕才不会被下一帧覆盖 |

倍率落到颜色上：派发完若全是 1 且不满亮就一个字都不写；否则把十四个颜色字段（发型、眼白、瞳孔、皮肤、衣服、裤子、鞋、三件护甲、坐骑……）逐个乘上倍率并夹到 0..255。

`DrawPlayer` 拿不到 `PlayerDrawSet`：它是「整玩家画完」的时机，那时 drawInfo 已经出了作用域；要 drawInfo 就用 `TransformDrawData`。

## 5. 隐藏某一层

原版玩家的 54 个绘制层是硬编码的方法调用，所以隐藏某一层的办法是给每个层单独挂钩子：`HideLayerHooks` 一层一个钩子，作者返回 true 就不调 `orig`。

```csharp
public override bool HideDrawLayer(Player player, VanillaDrawLayer layer)
    => layer == VanillaDrawLayer.HeldItem && player.HeldItem.type == ItemID.Umbrella;
```

* 层名用 `VanillaDrawLayer` 枚举（按绘制顺序排列）；
* 判定按**玩家**问，不传 `PlayerDrawSet`——同一玩家一帧内可能被画多次（残影、镜像），层集合不应随之变化；
* 没人开 `HideDrawLayers` 时，每个层只多一次位查询。

隐藏判定与层插入共用同一批钩子：先跑"之前"槽位，再看是否隐藏，最后跑"之后"槽位——被隐藏的原版层不影响作者层的相对顺序。

## 6. 镜头缩放（`ModPlayer.ModifyZoom`）

```csharp
public override void ModifyZoom(Player player, ref Vector2 zoom) => zoom = new Vector2(1.2f, 1.2f);
```

派发点在 `Player.Update` 后置、只对本机玩家，读 `Main.GameViewMatrix.Zoom`、交作者改、**值真的变了才写回**。不挂在 `SpriteViewMatrix.Rebuild` 里：`Zoom` 的 setter 会置矩阵重建标记，在里面写等于递归触发重建。

作者给出 ≤0 或 NaN 的分量时保留原值——缩放退化会让视口矩阵算出黑屏。

## 7. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 层数量为 0 | 完全不介入（只调 `orig`） |
| 某个层抛异常 | 记一次，继续下一个层；玩家照画 |
| `spriteBatch` 为空 | 跳过这一帧的层 |
| 位置依赖成环 | 报一次，按注册序排 |
| 缩放值非法 | 保留原值 |
