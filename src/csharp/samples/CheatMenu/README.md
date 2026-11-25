# CheatMenu

把 Terraria-Imgui（本机另一仓库，C++ ImGui 修改器）的作弊菜单迁移到 Pylon 的**竖切骨架**。

不是一个完整移植，而是先验证五条最容易卡住的链路；跑通后，P2-P5 的功能按同一模式填充。

## 当前范围（P0）

| 功能 | 对应原工具 | 验证的链路 |
|---|---|---|
| 上帝模式 | `PlayerCheatsTool` → GodMode | `Player.Hurt` / `Player.KillMe` hook + 字段回满 |
| 物品生成（文字版） | `ItemBrowserTool` | `Lang.GetItemNameValue` 搜索 + `Item.NewItem` |
| ESP | `ESPTool` | 界面层 `InterfaceScaleType.Game` 常驻绘制，菜单关闭仍显示 |
| 时间锁定 | `WorldTool` → 时间 | `ModSystem.PostUpdateTime` + `Main.time/dayTime` |
| 抓包计数 | `NetworkTool` | `ModSystem.HijackGetData` + `On.Telepathy.Client.Send` |

所有开关默认关闭；关闭时 hook 直接走 `orig`，不改变游戏行为。

## 结构

```text
CheatMenuMod.cs          Mod 入口：只注册根 ImGui 页
CheatConfig.cs           ModConfig：所有开关的单一来源
CheatRuntime.cs          ModSystem：晚阶段装配、tick、界面层、收包分派
Features/
  CheatFeature.cs        模块基类
  CheatFeatureHost.cs    统一安装/分派/错误隔离/配置防抖
  GodModeFeature.cs
  TimeLockFeature.cs
  ItemBrowserFeature.cs
  EspFeature.cs
  NetworkLogFeature.cs
Infra/
  Game.cs                本地玩家与菜单态的安全入口
  ItemNames.cs           物品名缓存
  NetworkLog.cs          收发包计数
UI/
  CheatRootTab.cs        唯一顶层页
```

约定：

- **一个 Mod、一个程序集、一个根 Tab**。不要按功能拆多个 Mod：Pylon 每个 Mod 有独立
  `AssemblyLoadContext`，跨 Mod 共享状态很困难，侧栏也会被几十个 Tab 淹没。
- **常驻视觉效果不进 Tab**：ESP/HUD/弹道预测放 `ModifyInterfaceLayers` 的作者层
  （`InterfaceScaleType.Game` 用世界坐标），菜单收起后仍绘制。
- **Hook 只装一次、用开关门控**：`Install()` 在 `PostSetupContent`，回调第一行判断配置。
- **热路径不分配**：ESP 每帧不建 List、不拼字符串；物品搜索只在查询变化时重建结果。
- **Tab 内逐模块 try/catch**：Pylon 对 Tab 异常是「本会话永久禁用」，一个模块出错不能拖垮整页。

## 跑起来

```powershell
pwsh -File src/csharp/tools/pylon.ps1 push -Samples CheatMenu
pwsh -File src/csharp/tools/pylon.ps1 run
adb logcat -s PylonLoader | Select-String cheat
```

设备上打开 Pylon 悬浮球 →「Pylon Mod」→「作弊菜单」。

## P0 真机验收点

代码已编译通过，但以下行为必须在设备上确认（没有设备时无法替代码断言）：

1. **ESP 坐标系**：界面层外壳按 `ScaleType.Game` 交给游戏绘制，框应贴住实体。若框偏移，
   说明该层实际批次不是世界矩阵，改为在 UI 层用 `GameViewMatrix` 做世界→屏幕换算。
2. **ESP 菜单关闭后仍显示**：这是 P0 的核心验证；若消失，说明层被菜单态过滤或外壳未插入。
3. **上帝模式**：受击不掉血、死亡被拦、生命/魔力每帧回满；多人下只影响本地玩家。
4. **物品生成**：搜索中文名或 ID，点击后在脚下生成；数量输入生效。
5. **时间锁定**：拖动滑条后昼夜立即变化并保持；关闭后恢复自然流逝。
6. **抓包计数**：进多人/单人后数字增长；关闭开关后不再增长（hook 仍在，但只走 orig）。
7. **配置持久化**：改开关后重启游戏，值保留。

## 下一阶段（按原计划）

1. **P2 核心单人**：无限召唤/范围/魔力、自动攻击、WorldTool 事件、ItemModifier、NPC 生成/屠杀、Inspector ×5、成就/相机/重生。
2. **P3 Hook 型**：Ghost、FullBright（用 LightMap hook，不复制原生偏移）、Digging、LoadoutEffect、Fish、刷怪设置、附加弹幕。
3. **P4 视觉常驻**：PlayerHUD、弹道预测、刷子/剪贴板预览。
4. **P5 世界/联机**：WorldEditBrush、CopyPaste（先直写）、NetworkTool 包解析、PlayerButcher、SyncChest、NPCDamage。默认关闭，仅承诺单人/自建服。
5. **图标**：先保持文字版；单独做 `Texture2D → RenderTexture → ImGuiTextures` 回读 spike，成功再加网格。
6. **不迁移**：网易音乐、XD 房列、防录屏、JS ModScript 物品表、原视觉壳。

## 已知坑

- 游戏代理与游戏版本绑定：换版本必须重新生成 `interop/generated` 再编译。
- `Player.tileRangeX/Y` 在代理里被同名静态属性挤掉，取不到实例字段；后续做「无限范围」时
  需要走 `IL2CPP.il2cpp_class_get_field_from_name` + `il2cpp_field_set_value`。
- `On.*` 回调异常会被 hook stub 吞掉；本示例在 Host 层做一次性日志并停用出错模块。
- 多人服务端权威：包类工具可能被 TShock/服务端拒绝，不要承诺公共服可用。

## 许可

迁移来源 Terraria-Imgui 为 MIT；本示例随 Pylon Loader 以 GPL-3.0 修改版发布，保留来源署名。
不包含任何游戏资源。