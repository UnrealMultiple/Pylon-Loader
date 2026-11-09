# 内容管线：界面层与系统绘制（Interface / SystemDraw）

## 1. 职责

| 作者入口（`Mod` 上的虚方法） | 生效时机 |
|---|---|
| `ModifyInterfaceLayers(List<GameInterfaceLayer> layers)` | 每帧画界面前一次；增删/换序/隐藏界面层 |
| `PostDrawInterface(SpriteBatch)` | 整块界面画完、鼠标提示之前 |
| `PostDrawTiles()` | 方块与投影画完、界面之前（画世界空间的额外内容） |

这三个钩子放在 `Mod` 上而不是单独的内容类型：系统时机本来就属于 Mod 本身，不必再多一层"系统对象"。

## 2. 界面层管线

### 2.1 游戏侧的形态

游戏每帧按 `Main._gameInterfaceLayers`（`List<GameInterfaceLayer>`）里的顺序逐层画界面。每层的 `Draw()` 调自己的 `DrawSelf()`，返回 false 就停在这一层。列表是游戏的私有字段，但按内存布局生成的代理能直接读写。

### 2.2 插入方式：抓取 → 交给作者 → 写回

```
每帧（Main.DrawInterface 前置）：
  游戏要重建层？ → 本帧不插手，下一帧重新抓
  列表对象或层数变了？ → 重新抓原版层（包装成作者的 GameInterfaceLayer，记下原生指针）
  layers = 原版层 + 加载器自己的层
  各 Mod 的 ModifyInterfaceLayers(layers)
  final = layers 里 Active 的那些 → 原生指针（原版层）或外壳指针（作者层）
  final 与上一帧一致 → 不写回（写回会让游戏重建所有层的状态）
```

### 2.3 作者层的绘制怎么转回托管

作者层不是游戏对象，所以在写回前给它造一个原生 `GameInterfaceLayer` 外壳（名字相同、坐标系相同），并把"外壳指针 → 作者层"记进表。管线挂 `On.GameInterfaceLayer.DrawSelf`：

* 指针在表里 → 调作者的委托，返回值就是这一层的返回值；
* 不在表里 → 原版层，照常走 `orig`。

外壳按「名字 + 第几次出现」缓存，作者每帧新建自己的层对象也只会复用一个外壳。

### 2.4 隐藏与换序

都是对列表的操作，不需要额外的钩子：

```csharp
public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
{
    layers.Find(l => l.Name == "Vanilla: Inventory")!.Active = false;   // 隐藏原版背包界面
    layers.Insert(10, new GameInterfaceLayer("MyMod: Panel", DrawPanel, InterfaceScaleType.UI));
}
```

`Active = false` 的层在写回时被跳过；顺序就是列表顺序。

### 2.5 加载器自己的层

加载器的配置界面与面板走同一条链：内部登记一个 `Pylon: Loader UI` 层（`InterfaceScaleType.UI`），先于作者改动插在原版层之后；作者把层 Add 到末尾时会排到它的后面（真机验证：`…|Pylon: Loader UI|PipelineTest: Panel`）。它不参与作者的增删，之前占用的 `DrawInterface_15_InvasionProgressBars` 固定钩子已删除。

## 3. 系统绘制时机

| 钩子 | 挂在 | 为什么是这个点 |
|---|---|---|
| `PostDrawTiles` | `Main.DrawSuperSpecialProjectiles` | 方块与投影已经画完、界面还没开始，这时画的是世界空间内容 |
| `PostDrawInterface` | `Main.DrawInterface_33_MouseText` | 所有界面层画完、鼠标提示还画，这时加的界面不会被原版界面盖住 |

两者都只在「有作者覆写了这个方法」时才分派（`Mod.Plan` 位集），没人用时每次只是一次位查询。菜单界面（`Main.gameMenu`）里不派发。

## 4. 失败语义

| 情况 | 表现 |
|---|---|
| `ModifyInterfaceLayers` 抛异常 | 记一次，这一帧按"没改过"的列表画 |
| 作者层绘制抛异常 | 记一次，这一层返回 true（后面的层照画） |
| 作者层没给绘制委托 | 写回时跳过（避免一个永远返回 false 的空层把界面卡死） |
| 列表被游戏重建 | 下一帧重新抓，作者改动照旧生效 |
| 没有作者覆写 | 完全不介入，界面层与绘制时机都只走原版 |
