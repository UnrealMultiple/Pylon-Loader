# 内容管线：界面层与系统绘制（Interface / SystemDraw）

## 1. 职责

| 作者入口（`ModSystem` 上的虚方法） | 生效时机 |
|---|---|
| `ModifyInterfaceLayers(List<GameInterfaceLayer> layers)` | 每帧画界面前一次；增删/换序/隐藏界面层 |
| `PostDrawInterface(SpriteBatch)` | 整块界面画完、鼠标提示之前 |
| `PostDrawTiles()` | 方块与投影画完、界面之前（画世界空间的额外内容） |

这三个钩子放在 `ModSystem` 上而不是单独的内容类型：系统时机本来就属于 Mod 本身，不必再多一层"系统对象"。

## 2. 我们的设计

### 2.1 文件

```
Pipelines/InterfaceLayer/
  InterfaceLayerPipeline.cs    界面层管线：抓取 → 交给作者 → 写回，作者层绘制转回托管
  SystemDrawPipeline.cs        绘制时机管线：PostDrawTiles / PostDrawInterface 的挂点
  SystemTable.cs               时机门：ModSystem 覆写位集决定挂不挂、跑不跑
Content/GameInterfaceLayer.cs  公开层对象：可继承、虚 DrawSelf；常规写法用 LegacyGameInterfaceLayer
```

### 2.2 游戏侧的形态

游戏每帧按 `Main._gameInterfaceLayers`（`List<GameInterfaceLayer>`）里的顺序逐层画界面，每层的 `DrawSelf()` 返回 false 就停在这一层（后面的层不画）。列表是游戏的私有字段，按内存布局生成的代理可以直接读写。

### 2.3 界面层：抓取 → 交给作者 → 写回

```
每帧（On.Main.DrawInterface 前置，orig 之前写回）：
  游戏要重建层（_needToSetupDrawInterfaceLayers）？ → 本帧不插手，下一帧重新抓
  列表对象换了，或层数 ≠ 上次写回的指针序列？ → 重抓：
      从当前原生列表导出原版层（按指针表跳过自家外壳），
      原版包装与写回序列都以这次导出为准
  layers = 原版包装（每帧 Active 复位 true）+ 加载器内部层
  各 ModSystem.ModifyInterfaceLayers(layers)
  final = Active 层的原生指针（作者层取外壳指针）
  final 与上次写回一致 → 不写回
  否则清空原生列表、按 final 重填，并把 final 记为新的写回序列
```

三个不变量：

1. **自己的写回永远不会触发重抓。** 写回把列表写成什么样，写回序列就同步成什么样：下一帧「层数 ≠ 序列长度」对上一次自己的写回恒不成立，能触发重抓的只剩真正的外部变化（列表对象被换、外部增删）。
2. **原版包装跨帧稳定，与原生列表的当前内容无关。** 包装只在重抓时重建；被隐藏的层（写回时被移出原生列表）包装仍在原版包装表里，每帧照常进 `layers`。作者的 `layers.Find(name)` 对任何原版层恒命中，隐藏→恢复可无限往返，跨帧缓存包装引用不因写回失效。
3. **外部改列表时重抓照常发生。** 游戏重建（换新列表对象）或外部增删改变了层数，指针/层数对比命中，重抓从当前原生列表补到新层。重抓会重建原版包装——游戏重建后原生层对象本身就是新的，旧包装引用失效、隐藏状态清零，与「层列表被游戏重建」的语义一致。

外壳不参与重抓：重抓按指针表跳过自家外壳，否则壳会被当原版层抓回来，`layers` 每帧净增、滚雪球。

mod 刷新（`ILoaderModuleRefresh`）时整份状态清空（原版包装、外壳、指针表、写回序列），下一帧按新装配重新抓。

### 2.4 作者层的绘制怎么转回托管

作者层不是游戏对象，所以在写回前给它造一个原生 `GameInterfaceLayer` 外壳（坐标系相同），并把「外壳指针 → 作者层」记进表。管线挂 `On.GameInterfaceLayer.DrawSelf`：

* 指针在表里 → 调 `layer.Draw()`（即 `!Active || DrawSelf()`；作者类可继承、虚 `DrawSelf`，常规写法 `LegacyGameInterfaceLayer(name, GameInterfaceDrawMethod, scaleType)`），返回值就是这一层的返回值；
* 不在表里 → 原版层，照常走 `orig`。

外壳按「名字 + 第几次出现」缓存并同步 `ScaleType`，作者每帧新建自己的层对象也只会复用一个外壳；外壳对象用强 GC handle 钉住，托管包装被回收也不影响游戏侧。

### 2.5 隐藏与换序

都是对列表的操作，不需要额外的钩子：

```csharp
public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
{
    layers.Find(l => l.Name == "Vanilla: Inventory")!.Active = false;   // 隐藏原版背包界面
    layers.Insert(10, new LegacyGameInterfaceLayer("MyMod: Panel", DrawPanel, InterfaceScaleType.UI));
}
```

`Active = false` 的层在写回时被跳过；顺序就是列表顺序。包装每帧 `Active` 复位为 true，要持续隐藏就每帧置一次；被隐藏层的包装不会消失，下一帧照常出现在 `layers` 里。

### 2.6 加载器自己的层

加载器的配置界面与面板走同一条链：内部登记一个 `Pylon: Loader UI` 层（`InterfaceScaleType.UI`），先于作者改动插在原版层之后；作者把层 Add 到末尾时会排到它的后面（真机验证：`…|Pylon: Loader UI|PipelineTest: Panel`）。作者能在列表里看到/移动它，但每帧都会重新加入。它的登记也参与安装门：有内部层、或有任何作者覆写了 `ModifyInterfaceLayers` 时才挂钩子。

### 2.7 系统绘制时机

| 钩子 | 挂在 | 为什么是这个点 |
|---|---|---|
| `PostDrawTiles` | `Main.DrawSuperSpecialProjectiles` | 方块与投影已经画完、界面还没开始，这时画的是世界空间内容 |
| `PostDrawInterface` | `Main.DrawInterface_33_MouseText` | 所有界面层画完、鼠标提示还没画，这时加的界面不会被原版界面盖住 |

挂载与分派都由覆写位集把门：没人覆写就不挂 On 点（位集在 Mod 装载时算好、刷新时重算）；有人覆写时每帧也只是一次位查询加 `Main.gameMenu` 判断，菜单里不派发。

## 3. 取舍理由

* **为什么重抓基准是「上次写回的指针序列」而不是「上次抓到的层数」？** 隐藏一个原版层 = 自己把它移出原生列表，列表层数从此比抓取那一刻少。拿抓取时的层数当基准，这次正常写回会被误判成外部改动，下一帧触发重抓；重抓又从当前原生列表整体重建包装，被隐藏的层连同包装一起从 `layers` 里消失——作者 `Find` 落空、NRE、界面卡死在隐藏态，且再也无法恢复。拿写回结果当基准后，自己的写回层数必然与基准一致，误判不复存在；被隐藏层的包装因此能跨帧存活。
* **为什么原版包装跨帧复用？** 作者的两种标准写法都依赖包装对象的身份：每帧 `Find` 依赖名字对应的包装始终在列表里；跨帧缓存引用依赖对象不被悄悄换掉。每帧（或每次写回后）重建等于把这两种写法都打断一次。
* **为什么「没变就不写回」？** 写回是清空再重填整份原生列表，每帧都做等于无意义的整表重建，还会把游戏对列表持有的运行状态一并清掉。
* **为什么外壳保留作者原名、按指针表排除？** 原生侧看到的层名就是作者层名，任何原生侧工具都能按原名检索；按指针排除自家外壳也不依赖名字约定，作者层叫什么名字都不会撞。
* **为什么加载器 UI 走同一条链？** 配置面板要与原版界面同受缩放分组与层序管理，另开绘制通道就得复刻一遍；代价是作者会看到一个 `Pylon: Loader UI` 层（见 2.6，有意设计）。
* **为什么逐 System try/catch、原版层绘制也兜住？** 一个坏钩子或一个坏层不能中断整帧界面；异常只影响它自己那一层/那一个 System。

## 4. 失败语义

| 情况 | 表现 |
|---|---|
| 某个 `ModifyInterfaceLayers` 抛异常 | 逐 System 捕获、记警告；该 System 的改动不生效，其余 System 与之前已做的改动照常写回 |
| 管线自身（抓取/写回）抛异常 | 记警告，本帧不写回，游戏按当前原生列表画 |
| 作者层绘制抛异常 | 记警告，该层返回 true（后面的层照画） |
| 原版层绘制抛异常 | 记警告，返回 true 继续画后续层 |
| 作者层没给绘制委托（子类未重写 DrawSelf） | 外壳照建，绘制返回 true |
| 列表被游戏重建 | 本帧不插手，下一帧重抓，作者改动照旧生效 |
| 没有作者覆写且无内部层 | 不安装钩子，界面层与绘制时机完全走原版 |

## 5. 扩展方式

* 新增一个绘制时机：`SystemDrawPipeline` 加一个 Gate（`Wanted` 查 `SystemTable` 位集）+ 对应 `On` 挂点，`ModSystem` 加虚方法、`SystemTable` 加位查询——不影响界面层管线。
* 新增加载器内部层：`InterfaceLayerPipeline.RegisterInternal` 登记即可，登记同时触发安装门。
* 原版层无需任何注册：重抓按指针自动补齐，游戏加了新的原版层，下一帧就出现在 `layers` 里。

## 6. 与其它管线的边界

* **菜单管线**：主菜单替换走独立的 `MenuPipeline`（`Content/ModMenu.cs`），不经 `_gameInterfaceLayers`；本管线两个绘制时机在 `Main.gameMenu` 时也不派发。
* **UI 管线**：加载器面板由 `ModUI` 经 `RegisterInternal` 进入本管线，只提供绘制委托；层序、缩放分组、写回都归本管线。
* **错误上报**：界面层与系统绘制的异常都走 `ContentErrors.Report`，由 `LoaderHost` 接到当前日志。
* **mod 刷新**：`SystemDrawPipeline.Refresh`（`ILoaderModuleRefresh`）负责调 `InterfaceLayerPipeline.Reset()`，界面层状态不跨刷新残留。
