# 内容管线：传送晶塔（Pylon）

## 1. 职责

| 游戏时机 | 分派的作者数据 |
|---|---|
| `TeleportPylonsSystem.HasPylonOfType` | `ModPylon.PylonType`（这个世界里已经放了这种晶塔没有） |
| `TeleportPylonsSystem.HasAnyPylon` | 同上（有没有任何 mod 晶塔） |
| 注册 | 给每个 `ModPylon` 分配 `PylonType`（从 `TeleportPylonType.Count` = 11 起） |
| 安装期 | `ModPylon.SetStaticDefaults` |

**不接**：地图图标与悬停、点图标传送、晶石的浮空绘制、传送有效性检查（`ValidTeleportCheck_*`）、放置/拆除时的系统通知、多人同步。

## 2. 我们的设计

### 2.1 两个文件

`Pipelines/Pylon/` 下：`PylonPipeline.cs`（两条系统查询 + 类型分配）、`PylonTable.cs`（类型号 ↔ `ModPylon` 表）。

### 2.2 只做"问得到、答得准"的那一半

游戏的晶塔系统有三层：**扫描（tile → TileEntity → TeleportPylonInfo）→ 列表/网络 → 界面与传送**。第三层（地图图标、点击、传送请求）要求 mod 晶塔以 `TeleportPylonInfo` 的形式进游戏的列表，而列表由 `TETeleportationPylon`（原版 tile entity）的扫描结果重建——我们的 `TEModdedPylon` 不在 `TileEntitiesManager` 里，塞不进去。

所以本版做前两层里**不需要动列表**的部分：

```
HasPylonOfType(t) : t 是 mod 类型 → 扫我们自己的 tile entity 列表回答
HasAnyPylon()     : 有任何 mod 晶塔 → true
```

这两句正是 `ModPylon.CanPlacePylon()` 依赖的判定，接上之后"一个世界只能放一个同类型 mod 晶塔"这条规则就成立了。

### 2.3 类型号

`PylonType` 从 `TeleportPylonType.Count`（11）往后发，注册序排列。注意 `TeleportPylonType` 是 **byte 底层的枚举**（`public byte value__`），所以 mod 晶塔类型上限是 255，超出直接抛（作者声明错误不降级成警告）。

### 2.4 数据来源

"世界里有没有这种晶塔"用我们自己的 `TileEntityBinding.All`：逐个看 `TEModdedPylon`、确认还活着（`IsLive`）、拿它位置上的方块问 `TryGetModPylon()`，再比 `PylonType`。不扫 `Main.tile`（整张地图扫一遍的代价远大于遍历几十个实体）。

## 3. 取舍理由

* **为什么不做地图图标与传送？** 那要求 mod 晶塔出现在 `TeleportPylonsSystem` 的列表里，而列表的构建源头是原版 tile entity 的扫描；本版没有"往游戏列表里塞条目"的设施（也拿不到它的私有列表字段）。做一半（能放不能传）比做全套更危险：玩家会以为能传。
* **为什么类型号从 11 起而不是复用原版号？** 复用会让 `HasPylonOfType(原版号)` 的答案被污染（原版判定会以为世界里已经有丛林晶塔）。
* **为什么用实体列表而不是扫方块？** 见 2.4；而且实体列表只有"真的存在"的晶塔，不会像扫方块那样把被半砖/墙挡住的残留帧当成晶塔。

## 4. 失败语义

| 情况 | 游戏侧表现 |
|---|---|
| 类型号超过 255 | 安装期抛异常（指名 `Mod/类名`） |
| 两个 `ModPylon` 抢同一个类型号 | 安装期抛异常 |
| 实体指向的方块已经不是晶塔 | 当作"没有"，不进统计 |
| `HasPylonOfType` 收到原版类型号 | 原样交给游戏 |
| `TileEntityBinding` 为空（本版没人放实体） | 一律答"没有" |
| 实体还在、方块已被拆掉 | `TryGetModPylon` 返回 null → 当作"没有"（实体本身会被方块实体管线清掉） |
| `HasPylonOfType` 收到负数/超范围类型 | 不当 mod 类型处理，原样交给游戏 |

## 5. 扩展方式

要接地图图标与传送：需要三步——(1) 在放置/拆除时把 mod 晶塔写成 `TeleportPylonInfo` 并塞进游戏列表（要新的 interop 设施：读写私有实例列表），(2) 挂地图图标的绘制入口，(3) 挂 `HandleTeleportRequest` 认 mod 类型。这三步都不改作者代码，作者那边的 `DrawMapIcon` / `ValidTeleportCheck_*` 声明已经在基类里等着。

另一个更省事的方向：等方块管线接上 `ModTileEntity` 的放置钩子（`Hook_AfterPlacement`）之后，"世界里有没有 mod 晶塔"这件事会自动变得可靠——本管线现在读的实体列表届时会被自动填满，不需要改动。

## 6. 与其它管线的边界

* **方块实体（18）**：`TEModdedPylon` 的实体登记、存活判定、存档归它；本管线只读列表。
* **方块（17）**：晶塔的 3×4 形状与放置规则归方块管线。
* **网络（7）**：晶塔列表同步归游戏自己的 `NetTeleportPylonModule`，本版不碰。
* **地图**：地图图标的绘制入口归界面/地图侧，本版不接。

### 这一版没接

1. **悬停文字与点击**：图标已经会画、传送已经能走（`TeleportPylonsMapLayer.Draw` + `HandleTeleportRequest`），但"手指点图标"这条收尾要走游戏的 `MapOverlayDrawContext` 点击判定；作者可以用 `DefaultMapClickHandle` 自己写。
2. **传送界面的列表**：mod 晶塔不会出现在游戏那个"选一个传送目标"的全屏列表里（那读的是 `TeleportPylonsSystem.Pylons`，我们没往里塞条目）；传送靠 `HandleTeleportRequest` 走。
3. **晶石浮空绘制**（`DefaultDrawPylonCrystal`）：`SpecialDraw` 的调用点在本版是私有方法（内容基类注释里已记），没有可挂入口。
4. **多人同步**：没有晶塔的网络通道。

**已经接上的两条**（原先列在这里，现已闭环）：

* **地图图标**：挂 `TeleportPylonsMapLayer.Draw`，原版画完自己的图标后按我们的 tile entity 列表逐个调 `ModPylon.DrawMapIcon`（近的用选中缩放）。
* **传送与有效性检查**：挂 `HandleTeleportRequest`，认 mod 类型后先问作者的 `ValidTeleportCheck_NPCCount` 与 `ValidTeleportCheck_BiomeRequirements`，都通过才 `player.Teleport`。另外 `IsPlayerNearAPylon` 也认 mod 晶塔（80 格内）。

### 对作者 API 的影响

| 变更 | 说明 |
|---|---|
| 语义明确 | `PylonType` 由管线分配（11 起、注册序），作者不要自己写死 |
| 新增约束 | `PylonType` 必须 ≤ 255（`TeleportPylonType` 是 byte 枚举） |
| 本版不调用 | `DrawMapIcon`、`DefaultDrawMapIcon`、`DefaultMapClickHandle`、`ValidTeleportCheck_*`、`DefaultDrawPylonCrystal` |
