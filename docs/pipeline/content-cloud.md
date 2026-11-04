# 内容管线：云（Cloud）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| `Cloud.addCloud` 之后 | `ModCloud.SpawnChance`（权重抽签）、`ModCloud.OnSpawn`（云已经生成，位置/大小都定了） |
| `Cloud.RollRareCloud` | 稀有云（`RareCloud => true`）的抽签，区间同构 |
| 安装期（`Static`） | `ModCloud.SetStaticDefaults` |
| 安装期（`Surface`） | 扩 `TextureAssets.Cloud` 并把作者贴图写进对应槽 |
| 绘制 | 不挂钩子：游戏自己按 `cloud.type` 取 `TextureAssets.Cloud[type]` 画 |

**不接**：绘制期的逐云回调、多人同步、云的生命周期（位置/速度/淡出）——那些仍然由游戏跑。**逐云着色已接**（见 2.4）。

## 2. 我们的设计

### 2.1 两个文件 + 三处表接入

`Pipelines/Cloud/CloudTable.cs`（模板表 + 权重抽签）、`Pipelines/Cloud/CloudPipeline.cs`（`addCloud` 分派）。贴图表接入复用现有设施：`TextureTable.Cloud` + `TypeTables` 的 `TableKind.Cloud`（和 gore 那条一模一样）。

### 2.2 编号从运行期表长接

原版云的类型数没有编译期常量，所以起点在**第一次扩表之前**按 `TextureAssets.Cloud.Length` 捕获（`TypeTables.CloudVanilla`），不写死 22，也不假设稀有云之后没有空档。

### 2.3 生成：`addCloud` 之后换编号

`Cloud.addCloud()` 是静态无参方法，游戏自己在里面抽类型、定位置、定大小、塞进 `Main.cloud`。我们在它跑完之后处理**刚生成的那一朵**（`Main.cloud[Main.numClouds - 1]`）：

```
权重 = Σ 每个 mod 云的 SpawnChance()      // 0 = 不参与
若 roll >= 权重 → 保持原版那朵（不动它）
否则 → 选中的那朵：type = mod 编号，width/height = 贴图尺寸，然后 OnSpawn
```

`roll` 的取值区间是 `[0, 权重 + 原版云数)`：把"每朵原版云"当成权重 1 的参与者，`SpawnChance` 的语义就是"我这朵顶得上几朵原版云"。这样 `SpawnChance() => 1` 的 mod 云不会因为"mod 云数量少"而几乎不出现，作者调的数直接对应"相对一朵普通云的出现率"。

### 2.4 逐云着色

游戏画每朵云之前会问这朵云"在今天的天色下该是什么颜色"——`Cloud.cloudColor(Cloud self, Color color)` 是**逐云实例**的方法，返回值就是最终颜色。管线挂这个入口，对 mod 云问一次作者的 `CloudColor`：

```csharp
public override Color CloudColor(Cloud cloud, Color color) => Color.Lerp(color, Color.Pink, 0.5f);
```

* 只在 mod 云的编号上问，原版云直接放行；
* 入参是游戏算好的原色（已含天色、高度、雾化），作者在它上面乘或插值，不用自己算光照；
* 每帧每朵一次，作者要"彩虹"这类效果就按 `cloud.position` 或 `Main.GlobalTimeWrappedHourly` 算；
* 抛异常按"不改色"处理。

### 2.5 为什么不挂逐云绘制钩子

游戏画云时读的是 `TextureAssets.Cloud[cloud.type]`，而这张表我们已经扩长并把作者贴图写进去了；`cloud.width` / `cloud.height` 也只被 `addCloud` 用来定尺寸，换编号时一起改掉即可。于是"mod 云怎么画"这件事不需要我们插手——**少一个每帧钩子，也少一处和原版绘制细节的耦合**。

### 2.6 稀有云（稀有一签）

`RareCloud => true` 的云不参与普通抽签，走 `Cloud.RollRareCloud` 这条独立路径——这条也挂上了：

```
RollRareCloud：先让游戏抽（拿到原版结果）→ 再按稀有云的权重抽一朵 mod 稀有云 → 抽到就返回它的编号
```

权重区间和普通云同构：每朵原版稀有云算权重 1，落在 mod 区间就换编号。**没贴图的稀有云不换**——换了就是一朵画不出来的云。

普通云的 `addCloud` 与稀有一签互不干扰：`addCloud` 里的抽签按 `RareCloud` 对齐当前这朵云是不是稀有云，所以两条路径各抽各的。

## 3. 取舍理由

* **为什么在 `addCloud` 之后换编号，而不是从零自己生成一朵云？** 位置、视差高度、缩放、淡出速度、`active`、`spriteDir` 这些字段全是游戏按图层/视差算出来的；自己拼一朵要么漏字段（云不动/不淡出），要么就得把 `addCloud` 整段抄一遍。
* **为什么权重按"原版云数"归一？** 见 2.3；如果我们用"mod 云权重之和"归一，那么加第二个 mod 云会让第一个的出现率直接减半，作者写的数字就没有稳定含义了。
* **为什么绘制不挂钩子、着色却挂钩子？** 绘制靠扩表就能接到原版三条绘制路径，是数据；着色是每朵云不同的取值，没有表可写，只能挂逐云入口。
* **为什么稀有云"先跑原版再换"？** 稀有云在游戏里还牵着手势/彩蛋逻辑（十周年世界等），先让原版抽完保证状态一致，再把编号换成 mod 的；直接跳过原版会漏掉那些副作用。
* **为什么扩表而不是自绘？** 扩表之后原版的所有云绘制路径（近/中/远三层、视差、雾化）自动对 mod 云生效；自绘等于把这三层重写。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 作者没给贴图 | 抽到了也不换编号（画不出来等于穿帮） |
| `SpawnChance` 抛异常 | 按 0 处理，记一次 |
| `SpawnChance` 之和为 0 | 整段跳过（不消耗随机数） |
| `OnSpawn` 抛异常 | 记一次，云已经是 mod 云了（不回滚） |
| `Main.numClouds` 为 0（还没生成） | 跳过 |
| 原版表长捕获不到（表没建好） | 不扩表、不发号，本轮什么都不做，下轮再试 |

## 5. 扩展方式

要接逐云绘制：挂 `NextHorizonRenderer.DrawCloud`，按 `cloud.type` 查表后把绘制权交给作者；需要保证作者的绘制参数与原版一致（缩放、透明度、层序）。

## 6. 与其它管线的边界

* **贴图表**：`TextureTable.Cloud` / `TypeTables` 的扩表与写槽归贴图设施，本管线只调用。
* **背景（30）**：天空/背景层的绘制顺序归背景管线；本管线只管云这一层的数据。
* **音效 / 场景效果**：无关。
* **安装管线**：扩表在 `Static`、贴图写入在 `Surface`，与其它内容一致。

### 这一版没接

1. **稀有云出现时的彩虹着色**：稀有一签已接（见 2.6）；彩虹效果现在可以用 `CloudColor` 自己写（见 2.4），管线不替作者决定配色。
2. **逐云绘制回调**：不挂钩子（见 2.5）。
3. **多人同步**：云本来就是本地生成的表现，mod 云也不会同步。
4. **云的消失/淡出时机**：仍由游戏跑，本管线不参与。
5. **`Cloud.resetClouds` 时的清理**：不挂；重置后 `addCloud` 会重新抽签，mod 云自然重新出现。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 语义明确 | `SpawnChance()` 的单位是"一朵普通云"；返回 0 = 永不出现 |
| 新增约束 | 贴图缺失（`TexturePath` 指的文件不存在）时这朵云不会被选中 |
| 生效 | `RareCloud => true` 参与 `RollRareCloud` 稀有一签（没贴图不换） |
