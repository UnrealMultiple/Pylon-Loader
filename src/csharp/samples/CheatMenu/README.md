# CheatMenu

把 Terraria-Imgui（本机另一仓库，C++ ImGui 修改器）的作弊菜单迁移到 Pylon 的 Mod 示例。

**已按原版工具逐个补齐**：从早期的 5 条链路竖切，扩到覆盖原版全部页签（主要 / 世界 / 实体 /
物品 / 杂项 / 网络 / 设置 / 视觉 / 检查器）。原版的每个 `*Tool` 对应这里的一个 `*Feature`，
注册顺序与 `Tools.cpp` 一致。

## 绘制模型（与原版一致，改动过的地方）

原版修改器的常驻绘制走 **`ImGui::GetBackgroundDrawList()`**，游戏画面照常自己画；
Pylon 里对应新加的 **`Pylon.ModLoader.ImGuiHost.ModOverlay`**：

```csharp
RegisterOverlay("CheatMenu/esp", DrawEsp);   // 基类封装，卸载时自动 Unregister
```

- 每帧在 ImGui 帧内、`Render()` 之前、菜单窗口之外调用，**与菜单开合无关**。
- 坐标系是覆盖层**物理像素**；世界坐标用 `WorldToScreen`
  （`XNAUnityRunner.AdjustWorldForRealScreen`）换算后可直接当绘制坐标。
- 颜色：配置里存 `0xAARRGGBB`，`CheatFeature.ToImU32` 重排成 ImGui 的 `0xAABBGGRR`
  （即 `IM_COL32(r,g,b,a)` 的数值布局）。

早期版本把 ESP/HUD/弹道预测/世界编辑预览塞进 `ModifyInterfaceLayers` 的
`LegacyGameInterfaceLayer`（`InterfaceScaleType.Game` + `SpriteBatch`），**这是错的**：
物理像素坐标喂进逻辑分辨率的游戏批次，结果是错位或根本看不见。现已全部迁到 `ModOverlay`。

## 结构

```text
CheatMenuMod.cs          Mod 入口：只注册根 ImGui 页
CheatConfig.cs           ModConfig：所有开关的单一来源（字段名沿用原版 mod_config.json）
CheatRuntime.cs          ModSystem：晚阶段装配、tick、收包分派、卸载反注册
Features/
  CheatFeature.cs        模块基类 + 常驻绘制注册/反注册 + 颜色与向量换算
  CheatFeatureHost.cs    统一安装/分派/错误隔离/配置防抖
  CheatCategory.cs       原版 MenuTab 分类
  *.cs                   每个文件 = 原版一个 Tool（见下表）
Infra/
  Game.cs                本地玩家/图格位语义的安全入口
  WorldEdit.cs           世界编辑公共支撑：瞄准点、世界↔屏幕、预览色、ImGui 绘制小工具、发包
  IconStore.cs           图标库：直接读 assets/ 下的 PNG 上传纹理（LRU + 每帧上传预算）
  IconGrid.cs            图标网格控件（透明按钮 + 等比图标 + 自动换行）
  InspectorCommon.cs     检查器共用绘制
  RawPacket.cs           原始包构造（伪物品栏 / 伪造角色控制包）
UI/
  CheatRootTab.cs        唯一顶层页（分类 → 子页 → 卡片）
assets/                  MT2 图标：普通 PNG 文件，不打 zip
  items/Item_<id>.png  npcs/NPC_<id>.png
  projectiles/Projectile_<id>.png  buffs/Buff_<id>.png
```

约定：

- **一个 Mod、一个程序集、一个根 Tab**。Pylon 每个 Mod 有独立 `AssemblyLoadContext`，
  按功能拆多个 Mod 会让侧栏被几十个 Tab 淹没。
- **常驻视觉效果不进 Tab**：ESP/HUD/弹道预测/世界编辑预览注册到 `ModOverlay`，
  菜单收起后仍绘制。
- **Hook / 绘制只装一次、用开关门控**：`Install()` 在 `PostSetupContent`，
  回调第一行判断配置，关闭时零成本。
- **热路径不分配**：ESP 每帧不建容器、不拼字符串；物品/NPC 搜索只在查询变化时重建。
- **Tab 内逐模块 try/catch**：Pylon 对 Tab 异常是「本会话永久禁用」，一个模块出错不能拖垮整页。

## 功能与模块对照

| 分类 | 模块（原版 Tool） |
|---|---|
| 主要 | `PlayerCheatsFeature`(PlayerCheatsTool)、`FullBrightFeature`、`DiggingFeature`、`GhostFeature`、`RespawnFeature`、`LoadoutFeature`、`FishFeature`、`BuffGenFeature`、`CharacterFeature` |
| 世界 | `WorldFeature`(WorldTool)、`WorldEditBrushFeature`、`WorldEditCopyPasteFeature` |
| 实体 | `NpcSpawnFeature`、`NpcSpawnSettingsFeature`、`ProjectileFeature`、`ButcherNpcFeature`、`PlayerButcherFeature` |
| 物品 | `ItemBrowserFeature`、`ModItemsFeature`、`ItemModifierFeature` |
| 杂项 | `AchievementUnlockFeature`、`CameraScaleFeature`、`SyncChestSizeFeature`、`NpcImmortalFeature`、`GemStaffForm2Feature`(常驻后台) |
| 设置 | `SettingsFeature` |
| 视觉 | `EspFeature`、`PlayerHudFeature`、`ProjectilePredictionFeature` |
| 检查器 | `InspectorItemFeature`、`InspectorNpcFeature`、`InspectorPlayerFeature`、`InspectorProjectileFeature`、`InspectorTileFeature` |

## 列表来源（修复过的地方）

物品/NPC 列表**不再依赖图标是否存在**：

- 物品：全量扫描 `1..ItemID.Count`，名称取 `Lang.GetItemNameValue`，拿不到回退 `Item <id>`。
- NPC：全量扫描 `1..NPCID.Count`，名称取 `Lang.GetNPCNameValue`，拿不到回退 `NPC <id>`。
- 图标只是格子里的图画不画得出来；缺图不影响按钮和点击。

> 早期版本列表直接取「图标包里有的 id」，图标包又放在 Mod 根目录、`pylon.ps1` 只推
> `assets/` 目录，于是运行时一个图标都找不到 → 列表整片空白。

## 跑起来

```powershell
pwsh -File src/csharp/tools/pylon.ps1 push -Samples CheatMenu
pwsh -File src/csharp/tools/pylon.ps1 run
adb logcat -s PylonLoader | Select-String cheat
```

设备上打开 Pylon 悬浮球 →「Pylon Mod」→「作弊菜单」。

`assets/` 下是 8000 多个小 PNG，首次 push 会比单文件慢一些。

## 真机验收点

以下行为需要设备确认（没有设备时无法替代码断言）：

1. **ESP**：框贴住实体（贴合碰撞箱）；关菜单仍显示；本机/他人/NPC/弹幕颜色与连线开关生效。
2. **HUD**：头顶面板出现在玩家头顶，手持武器图标可见（图标来自 `assets/items/`）。
3. **弹道预测**：箭矢/子弹弧线与原版一致，关闭开关后不绘制。
4. **世界刷子**：预览圆跟着准心；圆形覆盖（不是斜带）；破坏/放置/替换/液体各自生效；
   全屏地图打开时只画地图那份预览。
5. **区域复制粘贴**：圈地红框 + 填充 → 抬手复制 → 绿框预览 → 粘贴完成；
   多格物件（树/工作台/箱子）不被拆散；墙粘贴后**没有规律性断缝**。
6. **物品/NPC 列表**：搜中文名或 ID 都能出结果，格子有图标，点击生效。
7. **配置持久化**：改开关后重启游戏，值保留。

## 已知坑

- 游戏代理与游戏版本绑定：换版本必须重新生成 `interop/generated` 再编译。
- `On.*` 回调异常会被 hook stub 吞掉；本示例在 Host 层做一次性日志并停用出错模块。
- 多人服务端权威：包类工具可能被 TShock/服务端拒绝，不要承诺公共服可用。
- 逐格预览（刷子详细模式 / 剪贴板色块）每格两次 `AdjustWorldForRealScreen` 原生调用；
  上限 5000 格时约 1 万次/帧，低端机若掉帧可减少预览面积。
- 原版「真实贴图预览」（在 `Main.DoDraw` 之后自己 Begin 一批 SpriteBatch）未移植：
  Pylon 没有等价的后置挂点，故只保留地图色块预览。

## 许可

迁移来源 Terraria-Imgui 为 MIT；本示例随 Pylon Loader 以 GPL-3.0 修改版发布，保留来源署名。
图标资源（原 `MT2.zip`）已解包为 `assets/` 下的普通 PNG 文件随 Mod 分发。不包含任何游戏二进制。