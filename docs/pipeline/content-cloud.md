# 内容管线：云（Cloud）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| `Cloud.addCloud` 之后 | `ModCloud.SpawnChance`（权重抽签）、`ModCloud.OnSpawn`（云已经生成，位置/大小都定了） |
| `Cloud.RollRareCloud` | 稀有云（`RareCloud => true`）的抽签 |
| 安装期（`Static`） | 扩 `TextureAssets.Cloud` 与 `TextureMaskManager.CloudMasks`、逐朵 `SetStaticDefaults` |
| 安装期（`Surface`） | 作者贴图写进对应槽（安装管线按 `ModCloud => TextureTable.Cloud` 统一做） |
| `NextHorizonRenderer.DrawCloud` | `ModCloud.Draw`（覆写它的云逐云接管；其余云原版自己画） |

**不接**：多人同步、云的生命周期（位置/速度/淡出）——那些仍然由游戏跑。

## 2. 我们的设计

### 2.1 两个文件 + 贴图接入

`Pipelines/Cloud/CloudTable.cs`（模板表 + 权重抽签 + 原版基数）、`Pipelines/Cloud/CloudPipeline.cs`（`addCloud` / `RollRareCloud` / `DrawCloud` 分派、安装期扩表）。贴图写入复用安装管线：`InstallSteps` 的 `TableFor` 把 `ModCloud` 指到 `TextureTable.Cloud`，`Surface` 阶段统一写槽。

### 2.2 编号从原版云表长接

原版云的类型数没有编译期常量，所以起点在**第一次扩表之前**按表长捕获（`TextureSlots.Length(TextureTable.Cloud)`，只在读到正数时缓存）：这张表全长 ≈42（普通云与稀有云各占一段），mod 云的编号 = `Base + 注册序`，从原版表长之后顺延发号。发号在 `Refresh`（每轮 Mod 注册后）做，作者的 `SetStaticDefaults` 里拿得到 `Type`。

### 2.3 抽签基数：普通池与稀有池分开

抽签里"原版那一侧"的分母不是整张云表，而是**当前这条路径真正会摇的原版云数**：

* **普通池 = 22**：原版普通云（`CloudID` 的 Regular1..Cumulonimbus4，即 0–21）；
* **稀有池 = 当前世界类型的原版稀有云名单长度**：十周年世界读 `CloudID.Sets.RareClouds_Celebration`，平时读 `CloudID.Sets.RareClouds_Normal`（游戏侧维护的两张 `List<int>` 名单），取其 `Count`；名单为空或取不到时回退 15（十周年）/ 18（平时）。

`ModCloud.SpawnChance()` 的作者语义由此成立：普通云"我这朵顶得上几朵原版普通云"，稀有云"顶得上几朵原版稀有云"。两条路径若共用整张云表长做基数（把 20 个原版稀有类型也算进普通抽签的分母），mod 云的出现率会被系统性压低约一半——所以普通池与稀有池的基数必须分开取。

### 2.4 生成：`addCloud` 之后换编号

`Cloud.addCloud()` 是静态无参方法，游戏自己在里面抽类型、定位置、定大小、塞进 `Main.cloud`。我们在它跑完之后处理刚生成的这朵（`Main.cloud[Main.numClouds - 1]`，做越界与空引用检查）：

```
权重 = Σ 池内每朵 mod 云的 SpawnChance()   // 0 = 不参与；池按这朵云当前是不是稀有决定
原版基数 = 普通池 22 / 稀有池 名单长度
roll = Main.rand.Next((int)权重 + 原版基数)  // 整数格抽签：小数权重被量化到整数格
roll >= 权重 → 保持原版那朵（不动它）
否则 → 选中的那朵：type = mod 编号，width/height = 贴图尺寸 × scale，然后 OnSpawn
```

### 2.5 稀有云（稀有一签）

`RareCloud => true` 的云不参与普通抽签，走 `Cloud.RollRareCloud` 这条独立路径：

```
先让游戏抽（拿到原版结果）→ 按 VanillaRarePool 基数抽 mod 稀有池
→ 抽中且有贴图才把返回值换成 mod 编号；否则原样返回原版结果
```

没贴图的稀有云不换——换了就是一朵画不出来的云。

**当前实现的已知行为**（尚未处理，后续批次改；此处按现状如实记录）：

1. **mod 稀有云会被二次抽签、`OnSpawn` 大多漏调**：`RollRareCloud` 换出的 mod 稀有类型，回到 `addCloud` 后处理时又命中"这朵是稀有云"的判定、再进一次稀有池抽签；多数情况落回"原版区间"直接返回——这朵已经是 mod 稀有云的 `OnSpawn` 因此大多不触发（`OnSpawn` 只在换签路径里调），少数情况被二次抽成另一朵稀有云（分布被双重抽样扭曲）。
2. **原版稀有云会被普通 mod 云顶掉**：`addCloud` 后处理判"是不是稀有"只查 mod 表，原版稀有类型（`Base` 之前那段，鸟/BOSS 形/彩蛋云）被当普通云走普通池抽签，可能被普通 mod 云换号并误调 `OnSpawn`。

### 2.6 逐云绘制（`ModCloud.Draw`）

默认绘制靠扩表就能接上（见 2.7），但作者要"这张云画成别的形状/加特效"时需要逐云入口。挂的是 `On.Terraria.GameContent.Drawing.NextHorizonRenderer.DrawCloud(self, globalCloudAlpha, cloud, cloudPass, cY)`（接口实现的非虚方法，HookGen 全量生成后可用）：

```
某朵云的 ModCloud 覆写了 Draw（HookId.Draw 位集门控）
  → 按原版公式算 drawData：TextureAssets.Cloud[type]、cloudColor(Main.ColorOfTheSkies)
    （cloudPass==1 时 R×scale×0.8、G×(scale+1)/2×0.9）、Main.atmo<1 再乘、最后乘 globalCloudAlpha
  → 交给作者 Draw(cache, cloud, IndexOf(cloud), ref drawData)
  → cache 里每条无条件 Blit（Main.cloudBatch.Draw）
  → 返回 false = 原版不画；drawData 与原版一致 = 交给原版；改了 = 只 Blit 改后那张
作者异常 → 按原版画兜底（上报一条）。
```

注意 `DrawData` 在互操作层是装箱包装类：`drawData` 给作者的是**新建的一份**（不能与"原版那份"共享实例，否则改了也比对不出差异）。

### 2.7 没覆写 Draw 的云走什么

游戏画云时读的是 `TextureAssets.Cloud[cloud.type]`，而这张表我们已经扩长并把作者贴图写进去了；`cloud.width` / `cloud.height` 也只被 `addCloud` 用来定尺寸，换编号时一起改掉即可。所以**没覆写 Draw 的云零分派开销**（位集门控直通原版），绘制细节也不和原版耦合。

## 3. 取舍理由

* **为什么在 `addCloud` 之后换编号，而不是从零自己生成一朵云？** 位置、视差高度、缩放、淡出速度、`active`、`spriteDir` 这些字段全是游戏按图层/视差算出来的；自己拼一朵要么漏字段（云不动/不淡出），要么就得把 `addCloud` 整段抄一遍。
* **为什么普通池与稀有池用两张不同的基数？** 抽签的语义是"mod 云 vs 这条路径实际会摇出的原版云"：普通 `addCloud` 原版只摇普通云（22 朵），稀有路径只摇稀有云（当前世界类型的名单）。分母必须与玩家实际看到的"被替换对象"一致，作者写的数字才有稳定含义。
* **为什么权重按"原版基数"归一？** 如果用"mod 云权重之和"归一，那么加第二个 mod 云会让第一个的出现率直接减半，作者写的数字就没有稳定含义了。
* **为什么绘制默认不挂钩子、只对覆写了 Draw 的云接管？** 绘制靠扩表就能接到原版三条绘制路径，是数据；逐云回调只服务"要改画法"的作者，位集门控保证其余云零开销。
* **为什么稀有云"先跑原版再换"？** 稀有云在游戏里还牵着手势/彩蛋逻辑（十周年世界等），先让原版抽完保证状态一致，再把编号换成 mod 的；直接跳过原版会漏掉那些副作用。
* **为什么扩表而不是自绘？** 扩表之后原版的所有云绘制路径（近/中/远三层、视差、雾化）自动对 mod 云生效；自绘等于把这三层重写。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 作者没给贴图 | 抽到了也不换编号（画不出来等于穿帮）；稀有路径同理，原样返回原版结果 |
| `SpawnChance` 抛异常 | 按 0 处理，上报一条 |
| `SpawnChance` 之和为 0 | 整段跳过（不消耗随机数） |
| `OnSpawn` 抛异常 | 上报一条，云已经是 mod 云了（不回滚） |
| `OnSpawn` 没被调 | 只有换签成功才调；见 2.5 已知行为 1（mod 稀有云经二次抽签落回原版区间时不调） |
| `Main.numClouds` 为 0 / 目标下标越界 | 跳过 |
| 原版表长捕获不到（表没建好） | 不扩表、不发号，本轮什么都不做，下轮再试 |
| `CloudMasks` 扩容失败 | 上 Warn 一条并降级（游戏侧没有遮罩表时画云走老路径） |

## 5. 扩展方式

要接逐云绘制：挂 `NextHorizonRenderer.DrawCloud`，按 `cloud.type` 查表后把绘制权交给作者；需要保证作者的绘制参数与原版一致（缩放、透明度、层序）。

修 2.5 的两条已知行为：让 `RollRareCloud` 换签成功时自带 `OnSpawn`（或给 `addCloud` 后处理一个"这朵已是 mod 云"的短路标记，不再重抽、只补 `OnSpawn`）；"是不是稀有"的判定改用 `CloudID.Sets.RareClouds_*` 两张名单，原版稀有类型直接跳过普通池抽签。都在 `CloudPipeline.cs` / `CloudTable.cs` 两个文件内。

## 6. 与其它管线的边界

* **贴图表**：`TextureTable.Cloud` 的扩表与写槽归贴图/安装设施，本管线只调用（`Static` 扩表 + `Surface` 写槽）。
* **背景（30）**：天空/背景层的绘制顺序归背景管线；本管线只管云这一层的数据。
* **音效 / 场景效果**：无关。
* **安装管线**：扩表与 `SetStaticDefaults` 在 `Static`、贴图写入在 `Surface`，与其它内容一致。

### 这一版没接

1. **稀有云出现时的彩虹着色**：稀有一签已接（见 2.5）；原版对着色还有 `cloudPass==1` 的近层乘色，作者要"彩虹"这类效果可在 `Draw` 里自己算色（见 2.6），管线不替作者决定配色。
2. **多人同步**：云本来就是本地生成的表现，mod 云也不会同步。
3. **云的消失/淡出时机**：仍由游戏跑，本管线不参与。
4. **`Cloud.resetClouds` 时的清理**：不挂；重置后 `addCloud` 会重新抽签，mod 云自然重新出现。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 语义明确 | `SpawnChance()` 的单位：普通云 = "一朵原版普通云"（22 朵那一池），稀有云 = "一朵原版稀有云"（当前世界类型的稀有名单长度）；返回 0 = 永不出现 |
| 新增约束 | 贴图缺失（`TexturePath` 指的文件不存在）时这朵云不会被选中 |
| 生效 | `RareCloud => true` 参与 `RollRareCloud` 稀有一签（没贴图不换） |
| 当前注意 | mod 稀有云的 `OnSpawn` 大多不触发、原版稀有云可能被普通 mod 云顶掉（见 2.5 已知行为，后续批次处理） |
