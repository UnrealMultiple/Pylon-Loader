# 内容管线：尘土（Dust）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| 安装期（`Static`） | 发号 + `SetStaticDefaults` |
| 生成（`DustPipeline.Spawn`） | `OnSpawn(dust)` |
| 每帧更新（`Dust.UpdateDust` 之后） | `Update(dust)`（false = 消失） |
| 每帧绘制（`Main.DrawDust` 之后） | `GetAlpha(color)` + 作者贴图 |

**不接**：进游戏图集（`TextureAssets.Dust` 是单张，做不到）、原版的尘土物理分支（`DustID` 那些特例）、碰撞与灯光、联网同步、`DustID.Sets` 的逐类型取值。

## 2. 我们的设计

### 2.1 为什么不走游戏那张图集

`TextureAssets.Dust` 是**一张图集**（`Asset<Texture2D>`，不是数组），原版按类型号在一张图里取帧。mod 类型号塞进去只有两种结果：越界崩，或者画出一块无关的像素。

参考实现能按类型取图，是因为它在原生层把 mod 贴图也纳入了那张图集的索引。托管层做不到这一点。

### 2.2 于是自己养一批

`Pipelines/Dust/DustTable.cs` 发号（从 `10000` 起，只在我们自己的绘制里用），`Pipelines/Dust/DustPipeline.cs` 维护一份 `Live` 列表：

```
Spawn：new Dust() → OnSpawn → 进 Live
UpdateDust 之后：每个 live 跑 Update + 最小物理（速度、重力、收缩）
DrawDust 之后：每个 live 用作者贴图画出来
```

**好处**：永远不会把 mod 号交给游戏去索引图集，越界的风险从根上没有了。
**代价**：mod 尘土不走原版的物理分支（气泡、火花、血雾那些特例），只有一套通用物理；作者要特殊行为就在 `Update` 里自己写。

### 2.3 最小物理给了什么

`position += velocity`、`velocity.Y += 0.1`（除非 `noGravity`）、`velocity *= 0.99`、`rotation += velocity.X * 0.05`、`scale *= 0.99`。尺寸小于 0.02 或掉出屏幕下方 200 像素就自动回收。

作者在 `Update` 里可以直接改 `dust.velocity` / `dust.scale` / `dust.color`——这些改动会先生效，然后才叠加我们的那一步。要完全接管就返回 false 自己重新 Spawn。

### 2.4 编号为什么从 10000 起

mod 尘土的类型号只在本管线内部用（`DustTable.Of` 反查），不写进任何游戏数组、不参与原版判定。用一个大而稳定的基址，是为了让作者打印出来的号一眼能看出是 mod 的。

## 3. 取舍理由

* **为什么不用 `Main.dust`？** 见 2.1：进了那个数组就会被原版绘制循环扫到，而它只认图集。
* **为什么不照参考实现走原生？** 那需要 Pylon 有对应的原生注册入口；托管层没有，硬凑出来会是"能生成、画不出来"。
* **为什么物理只给最小一套？** 原版分支是按 `DustID` 写死的几百个特例，复制过来等于把原版尘土逻辑重写一遍；通用物理 + 作者回调覆盖了绝大多数表现需求。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| `new Dust()` 失败 | 这次生成返回 null，不崩 |
| `OnSpawn` 抛异常 | 记一次，尘土照常存在 |
| `Update` 抛异常 | 记一次，这个尘土被回收 |
| `GetAlpha` 抛异常 | 记一次，用 `dust.color` 原样画 |
| 贴图没加载好 | 这一帧不画（其他尘土照画） |
| `Spawn` 传 null / 未发号的 `ModDust` | 返回 null |

## 5. 扩展方式

接原版物理：给 `ModDust` 加一个"借用的原版类型"，在 `Update` 里把 `dust.type` 临时设成它跑原版逻辑——但那需要改 `Main.dust` 的归属，属于另一套设计。
接联网：mod 尘土是纯表现，按原版的规矩本来也不该同步。

## 6. 与其它管线的边界

* **血腥粒子（19）**：那条是 `Gore`，有自己的数组与绘制，和尘土无关。
* **弹幕 / 物品**：它们生成尘土时如果要 mod 类型，得调 `DustPipeline.Spawn`，不能走 `Dust.NewDust`。
* **贴图设施**：本管线不扩表（见 2.1），只用作者自己的资源。
* **场景效果**：无关。

### 这一版没接

1. **进游戏图集**（`TextureAssets.Dust` 单张，托管层做不到）。
2. **原版尘土物理分支**（`DustID` 的几百个特例）。
3. **碰撞 / 灯光 / `DustID.Sets`**。
4. **联网同步**。
5. **`Dust.NewDust` 系列的 mod 类型直传**：mod 类型号交给原版入口会被拿去做图集索引，所以只能走 `Spawn`。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 新增 | `ModDust` 基类：`OnSpawn` / `Update` / `GetAlpha` |
| 新增 | `DustPipeline.Spawn(mod, position, velocity, alpha, color, scale)` |
| 语义明确 | mod 尘土不进游戏图集，由管线自己更新与绘制 |
| 本版不生效 | 原版物理分支、碰撞、联网、`Dust.NewDust` 直传 |
