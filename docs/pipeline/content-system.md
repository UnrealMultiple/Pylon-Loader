# 内容管线：系统（ModSystem）

## 1. 职责

`ModSystem : ModType`（`Content/ModSystem.cs` + `ModSystem.WorldGen.cs`）是"跟着世界走、跟着装载走"的作者面：一个 mod 可以有多个实例，实例之间互相独立。虚方法按生命周期分六组：装载 9（OnModLoad/OnModUnload/PostSetupContent/OnLocalizationsLoaded/ResizeArrays/AddRecipeGroups/AddRecipes/PostAddRecipes/PostSetupRecipes）、世界数据与拒绝 9（OnWorldLoad/OnWorldUnload/ClearWorld/PreSaveAndQuit/SaveWorldData/LoadWorldData/SaveWorldHeader/CanWorldBePlayed/WorldCanBePlayedRejectionMessage）、世界生成 5（PreWorldGen/ModifyWorldGenTasks×2/PostWorldGen/ModifyHardmodeTasks）、每帧 23（PreUpdateEntities、Pre/PostUpdatePlayers/NPCs/Gores/Projectiles/Items/Dusts/Time/World/Invasions、PostUpdateEverything、UpdateUI、PostUpdateInput、ModifyTimeRate）、绘制/环境 11（ResetNearbyTileEffects/TileCountsAvailable/ModifyScreenPosition/ModifyTransformMatrix/ModifySunLightColor/ModifyLightingBrightness/PostDrawTiles/ModifyInterfaceLayers/PostDrawInterface/PreDrawMapIconOverlay/PostDrawFullscreenMap）、网络 4（NetSend/NetReceive/HijackGetData/HijackSendData）。

| 游戏时机 | 分派的作者钩子 |
|---|---|
| mod 注册收尾（`IPylonMod.Load`） | `Mod.OnModLoad` → 该 mod 的 system 实例 `OnModLoad` |
| 安装管线 Static 段开头 | `OnLocalizationsLoaded`（先于一切内容静态默认值） |
| 安装管线 Mods 段 | `ResizeArrays`（表定型后）→ 逐实例 `SetStaticDefaults` → 逐 mod `PostSetupContent`（mod 虚方法 → 该 mod 的 system 实例）→ 逐 mod `AddRecipeGroups`（同形状）→ `GlobalNPC.AddRecipeGroups` / `GlobalNPC.SetStaticDefaults` / `GlobalNPC.AddRecipes` |
| 安装管线 Recipes 段 | 逐 mod `AddRecipes`（mod → system → ModItem → GlobalItem）→ 逐 mod `PostAddRecipes` → `PostSetupRecipes` |
| `WorldGen.clearWorld` 后置 / `WorldGen.Reset` 后置（生成链内）/ `WorldFile.LoadWorld` 前缀 | `ClearWorld` → `ResetNearbyTileEffects`（清空事件，三入口共享一个幂等门） |
| `WorldFile.LoadWorld` | `CanWorldBePlayed` 拒绝链（命中则世界不加载）→ `OnWorldLoad` → 按键读侧车派 `LoadWorldData` |
| `WorldGen.SaveAndQuit` 前缀 / `Main.set_Quitting` 置 true | `ResetNearbyTileEffects` → `PreSaveAndQuit`（退出事件，保存前） |
| `WorldFile.InternalSaveWorld` | 逐 system `SaveWorldData` / `SaveWorldHeader` → 写侧车 |
| `WorldGen.SaveAndQuitCallBack` 后置 / `Main.set_Quitting` 置 false | `OnWorldUnload`（卸载后） |
| `WorldGen` 生成任务链 | `PreWorldGen` / `ModifyWorldGenTasks` / `PostWorldGen`；困难模式把原版转换包成任务后派 `ModifyHardmodeTasks` |
| `WorldGen.UpdateWorld` / `Main.UpdateTime` / `Main.UpdateInvasion` / `Dust.UpdateDust` / `Main.DoUpdateInWorld` / `Main.DoUpdate` / `Main.DoUpdate_HandleInput` | Pre/PostUpdateWorld、Pre/PostUpdateTime、Pre/PostUpdateInvasions、Pre/PostUpdateDusts、九个中体桩扇出的实体更新组、UpdateUI（gameMenu 门）、PostUpdateInput |
| `Main.DoDraw_UpdateCameraPosition` / 绘制中体桩（写回 `Main.GameViewMatrix`）/ `Main.DrawSuperSpecialProjectiles` / `Main.DrawInterface_33_MouseText` / `Main.DrawInterface` | `ModifyScreenPosition`、`ModifyTransformMatrix`、`PostDrawTiles`、`PostDrawInterface`、`ModifyInterfaceLayers` |
| `MapIconOverlay.Draw` | `PreDrawMapIconOverlay` / `PostDrawFullscreenMap` |
| `Main.ApplyColorOfTheSkiesToTiles` / `Lighting.UpdateGlobalBrightness` | `ModifySunLightColor`、`ModifyLightingBrightness`（有限性/≥0/≠1 门） |
| `SceneMetrics.AggregateTileCounts` / `SceneMetrics.Reset` | `TileCountsAvailable`、`ResetNearbyTileEffects`（扫描路径） |
| `NetMessage.SendData` msg7 / 自有通道接收 / `MessageBuffer.ProcessData` / `SendData` 前缀 | `NetSend`、`NetReceive`、`HijackGetData`、`HijackSendData` |
| `Main.QuitGame` | `Mod.OnModUnload` → 逐实例 `OnModUnload` |

**退出与清空的双通道（本构建）**：世界退出链的三个 `WorldGen` 入口（`clearWorld` / `SaveAndQuit` / `SaveAndQuitCallBack`）的 detour 在真机上收不到调用（历史真机台账记录；该台账已不在仓内，无法复核当时的验证方式）。以 `refs/dump.cs` + `refs/lib/arm64-v8a/libil2cpp.so` 的全段 ARM64 直接调用扫描为准：`SaveAndQuit` 有三个直接调用点（暂停菜单、多人会话、主循环）、`clearWorld` 有十一个（读档、生成、客户端断开、旧格式读头），两者在本构建二进制里都仍会被进入；`SaveAndQuitCallBack` 无任何静态调用点与地址取用（保存线程的回调指针运行期解析，静态不可判定）。因此四个退出时机（`PreSaveAndQuit` / `ClearWorld` / `OnWorldUnload` / 退出路径 `ResetNearbyTileEffects`）走双通道：原钩保留，另观察退出状态位 `Main.Quitting` 与世界生成、读档入口作第二通道，幂等门保证同一事件只派一次（见 §2.4）。全屏地图两钩已接，需手动开图才触发。

## 2. 我们的设计

### 2.1 文件拆分

```
Content/ModSystem.cs(+WorldGen.cs)  作者面虚方法与 TML 形状的扩展属性
Content/Loaders/SystemLoader.cs     实例注册表、按钩过滤缓存、派发门面、世界数据键
Hooks/ModLifecycle.cs               世界期/每帧期/退出期入口接线 + 侧车读写 + 拒绝链
Hooks/ModEnvironmentHooks.cs        天空色/全局亮度/弹幕前更新/换语言重派
Hooks/ModWorldGenHooks.cs           世界生成任务链（抓任务、改任务、回滚、权重）
Pipelines/System/SystemStages.cs    每帧中体桩安装与派发（9 系统桩 + 2 玩家桩）
Pipelines/InterfaceLayer/*          界面层顺序、绘制钩、地图钩、SystemTable
Pipelines/Net/*                     NetSend/NetReceive 通道与 Hijack 落地
Install/InstallPlan.cs              装载序的唯一事实来源
```

派发入口只做"游戏时机 → `SystemLoader.*`"的转接；唯一的例外是退出/清空的幂等门（`ModLifecycle` 里四个布尔位，见 §2.4），其余状态与缓存都在 `SystemLoader` 与 `WorldSidecar`。

### 2.2 注册与门控

注册期 `RegistrationPipeline` 收集实例进 `SystemLoader`（顺序 = 类型全名序数），每个实例挂一张 `HookScan` 覆写位集。派发面两条路：逐 mod 的时机（`Of(mod)`，装载期批次）与逐钩的时机（`With(HookId)`，按位集过滤后缓存，实例增删时清缓存）。装载期 mod 级虚方法按覆写位集门控——没覆写的 mod 不进派发，也不进耗时统计；system 实例侧逐实例直派（装载期一次性的空虚方法调用不值得多一层过滤缓存）。

### 2.3 装载序

安装管线按 `InstallPlan` 的列表顺序逐步执行（`InstallStage` 只是分组标注，列表顺序即执行顺序）。mod 可见状态的次序：

1. **本地化加载完成**（Static 段第一步）：派 `OnLocalizationsLoaded`。它只依赖 system 实例已注册，不依赖任何表——放最前让所有内容的 `SetStaticDefaults` 都能读到最终文案。
2. 各内容静态默认值批与样例批（buff/坐骑/前缀/稀有度/尘土/云/…、物品/NPC 静态默认值与样例）。
3. `System.ResizeArrays`：表定型后给 system 一次扩回调。
4. 逐实例 `SetStaticDefaults`（「System.SetStaticDefaults」步）。
5. **`PostSetupContent` 逐 mod 嵌套**：mod A 的 mod 虚方法 → mod A 的 system 实例 → mod B 的 mod 虚方法 → …。每个 mod 全套跑完才轮到下一个 mod。
6. 渔夫任务鱼补池。
7. **`AddRecipeGroups` 逐 mod 嵌套**（与 5 同形状），随后 `GlobalNPC.AddRecipeGroups`。
8. 提示重建开关、`GlobalNPC.SetStaticDefaults` + `GlobalNPC.AddRecipes`。
9. Recipes 段：逐 mod `AddRecipes`（mod → system → ModItem → GlobalItem）→ 逐 mod `PostAddRecipes` → 配方收尾 → `PostSetupRecipes`。

换语言重派：`LanguageManager.SetLanguage_GameCulture` 后置钩，`LoaderPhase == Ready` 门内重派 `OnLocalizationsLoaded`（只覆盖 system 实例）。

### 2.4 世界期接线：读档、清空、退出

世界生命周期由两类入口共同观测，事件内用幂等门收敛成"恰好一次"：

* **清空事件**（派 `ClearWorld` → `ResetNearbyTileEffects`）：三个入口共享 `_clearWorldDone` 门——`WorldGen.clearWorld` 后置、`WorldGen.Reset` 后置（生成链里它紧跟 `clearWorld`、先于生成任务链）、`WorldFile.LoadWorld` 前缀，谁先命中谁派发。门在 `OnWorldLoad`（世界会话开始）复位；`LoadWorld` 拒绝路径派发后也复位，下一次尝试重新评估。`clearWorld` 的多人客户端分支保持不变：`netMode == 1` 时清空后即派 `OnWorldLoad`（客户端世界经网络到达，不走 `LoadWorld`）。
* **退出事件**：`WorldGen.SaveAndQuit` 前缀在保存前派 `ResetNearbyTileEffects` → `PreSaveAndQuit`；`SaveAndQuitCallBack` 后置派 `OnWorldUnload`。第二通道观察 `Main.Quitting`——本构建全二进制只有两处写入：`SaveAndQuit` 在启动保存线程前置 true，主菜单首帧（`GUIMainMenu.Draw`）置 false。置 true 时若会话进行中且保存前两钩未派过则补派；置 false 时若会话未派过 `OnWorldUnload` 则补派。
* **会话门**：`_worldSession` 在 `OnWorldLoad` 置位、卸载派发后清位；它只约束第二通道，三个 `WorldGen` 原钩只受各自事件的幂等门约束。
* **服务器**：手机端房主退出与单机走同一条 `SaveAndQuit` 链（多人会话界面、主循环、暂停菜单都有直接调用），四个时机的覆盖一致；关服收尾在保存回调的服务器分支内，`OnWorldUnload` 仍由退出事件的幂等门派发。

### 2.5 世界数据侧车

全部 mod 数据进一个 `<世界>.wld.pl`：GZip 压缩的 TagCompound 根（TagBinary），分节 banners / npcs / rooms / tiles / walls / tileentities / headers；system 世界数据以 `Mod.Uid/类型名` 为键平铺根节，世界头收 `headers` 节。保存走 tmp 原子替换；读取按键寻址，只把命中的 system 的数据转成托管 TagCompound。`WorldFileData.TryGetHeaderData` 提供免加载读头（每次读盘解压，无进程内缓存）。

### 2.6 每帧中体桩

九个中体桩（玩家/NPC/血粒/物品/弹幕的更新分段、绘制 `Transform`、时钟 `ClockAdvance`）在托管层实现：定位方法体 → BuildId + 16 字节指纹校验 → `MidBodyIntercept` 安装，原版指令照常执行，桩内按表扇出对应钩子（一个桩可扇多钩，如 NPC 收尾同时扇 `PostUpdateNPCs` 与 `PreUpdateGores`）。两个玩家侧时机（`GetDyeTraderReward` / `NaturalLifeRegen`）并入同一张阶段表管理，派发走 PlayerPipeline，空池补 0。ShadowHook 不可用时入口钩与 `UpdateTimeRate` 跳过、中体桥照常安装。

## 3. 取舍理由

* **为什么本地化派发放安装管线最前？** `OnLocalizationsLoaded` 的消费方是"读翻译写显示名"的静态默认值钩——buff/物品/NPC 的 `SetStaticDefaults` 都在它后面跑。派发本身只依赖实例已注册，前移没有任何表依赖要补。
* **为什么 `PostSetupContent` 先于 `AddRecipeGroups`？** `PostSetupContent` 消费的是"别的 mod 静态状态已就绪"（改默认值、登记缓存），配方组只被 `AddRecipes` 消费。组在配方注册（Recipes 段）前建好即可，不必早于 PostSetupContent；反过来，把组注册压到 PostSetupContent 之后才跑，会让 PostSetupContent 里读"全部 mod 状态"的写法踩到别的 mod 还没收尾的中间态。
* **为什么逐 mod 嵌套而不是先全体 mod 后全体 system？** 同一批钩子里 mod 们互相读对方刚写入的状态（配方组、收尾缓存）。按 mod 分批保证"mod A 全套 → mod B 全套"的次序可从单 mod 视角推演；平铺时 system 批读到的是所有 mod 叠加后的中间态，谁先谁后不再可预期。
* **为什么退出四时机要双通道？** 真机台账说退出链不经过三个 `WorldGen` 入口的 detour（验证方式已不可复核），静态扫描又说 `clearWorld`/`SaveAndQuit` 存在直接调用点、`SaveAndQuitCallBack` 无静态调用点——两份证据对"哪个入口真会执行"给不出一致答案。与其赌其一，不如让原钩与替代入口并存：detour 正常时第二通道全部落空，detour 失灵时第二通道补位，幂等门把两路收敛成"每事件恰好一次"，两个世界都正确。
* **为什么替代入口选 `Main.Quitting`？** 本构建没有 `Main.ExitToMenu`，也没有可读的"在世界中"状态位（PC 版的 `worldActive` 不存在）。`Quitting` 是全二进制只有两处写入的退出状态位：保存前置 true、主菜单置 false。这两个写入点天然把一次退出切成"保存前"与"回到主菜单"两段，正好对应 `PreSaveAndQuit`（保存前）与 `OnWorldUnload`（卸载后）的相对位置，且不需要每帧轮询。
* **为什么 `ClearWorld` 的第二通道挂在读档前缀与 `WorldGen.Reset`？** 清空的语义是"新世界数据到来之前重置 mod 静态状态"。读档前缀保证无论原钩死活，mod 状态都在 `LoadWorld` 应用世界数据之前重置；`WorldGen.Reset` 在生成链里紧跟 `clearWorld`、先于生成任务链，让"生成新世界"也满足"先生成前重置"。
* **为什么世界数据是单个二进制侧车？** 一个文件一次 IO，GZip 省盘，TagBinary 带类型不需要 schema 约定；JSON 文本侧车在 IL2CPP 侧序列化开销大、类型面窄。tmp 原子替换保证写一半不毁档。
* **为什么中体桩在托管层做？** ShadowHook + 指纹校验已能稳定命中方法体中段，换游戏构建只更新指纹表；逐架构手写汇编桥的维护成本换不来额外能力。
* **为什么拒绝链只挂在 `LoadWorld`？** 本构建没有可靠的世界选单期入口，`LoadWorld` 是唯一可保证命中的兜底；拒绝原因写日志，世界直接不加载。

## 4. 失败语义

| 情况 | 行为 |
|---|---|
| 装载期/世界期任一 system 钩子抛错 | 逐实例隔离（谁抛谁跳过，其余照常），同标签一次性上报 |
| 装载期 mod 级虚方法抛错 | 该 mod 该钩跳过，其余 mod 照常 |
| 安装步骤抛错 | `[install]` 记录一步，管线继续 |
| 中体桩指纹不匹配 / ShadowHook 不可用 | 对应桩或入口钩跳过并记日志，其余时机照常 |
| `WorldGen` 三入口 detour 收不到调用 | 退出/清空四时机由第二通道补派（幂等门保证每事件一次）；`OnWorldLoad`/`LoadWorldData` 不受影响 |
| `CanWorldBePlayed` 抛错 | 该 system 视为放行，继续问下一个 |
| `WorldCanBePlayedRejectionMessage` 抛错 | 回退到"<mod 显示名> does not allow this character in this world" |
| `SaveWorldData` / `LoadWorldData` 抛错 | 单 system 隔离；保存走 tmp 原子替换，写坏不毁档 |
| `HijackGetData` 抛错 | 该 system 复位读位不消费；命中者取最大消费位 |
| `NetSend` / `NetReceive` 抛错 | 单 system 隔离记一条，包链不断 |
| `ModifyTimeRate` / `TileCountsAvailable` 抛错 | 该帧该 system 跳过，下一帧重试 |

## 5. 扩展方式

新增一个 system 钩子：`HookId` 末尾追加 → `ModSystem` 加同名 `virtual` → 在对应入口文件（ModLifecycle / SystemStages / ModEnvironmentHooks / Net 两文件）的游戏时机处派 `SystemLoader.With(hook)` 或逐 mod `Of(mod)` → 位集与耗时统计自动生效。新增装载期批次：在 `InstallPlan` 列表里加一步，位置即次序；mod 级与实例级成对的批次用 `NestedSystemStep`（先 mod 虚方法、再该 mod 的 system 实例）。新增依赖退出/清空事件的时机时，挂进对应的事件块（清空门 / 退出门），不要另开通道——幂等门只收敛自己块内的派发。

## 6. 与其它管线的边界

* **Mod 所有者方法**：`Load` / `Unload` / `OnModLoad` / `OnModUnload` 是 `Mod` 的虚方法，不经 system 位集门控；`OnModLoad` 在注册收尾派发，先 mod 后该 mod 的 system 实例。
* **内容管线**：各内容类型的 `SetStaticDefaults`（buff/物品/NPC/弹幕/…）归各内容管线与安装批次；ModSystem 只负责 system 实例自己的装载钩。
* **玩家管线**：`GetDyeTraderReward` / `NaturalLifeRegen` 由系统阶段表的玩家桩承接，派发进 PlayerPipeline。
* **网络管线**：`NetSend`/`NetReceive` 走 msg7 之后按键寻址的自有通道；`HijackGetData`/`HijackSendData` 由 NetReceiveHooks/ItemSync 落地，键与包格式归网络管线。
* **世界生成管线**：`PreWorldGen` / `ModifyWorldGenTasks` / `PostWorldGen` / `ModifyHardmodeTasks` 由 ModWorldGenHooks 在任务链上派发，任务改写与回滚语义归那条管线。
* **语言**：换语言重派只覆盖 system 实例；内容类型的文案键由各自管线在重建时处理。
* **测试**：`system`（Ready）覆盖注册序、FullKey、装载钩计数；`system-w`（World）覆盖世界期全序、每帧钩子、侧车存读、网络往返与 Hijack 消费。
