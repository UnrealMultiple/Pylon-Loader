# Mod（作者入口基类）管线

> 基线：`ModLoader/Mod.cs`（141 行）；台账 `docs/audit/hook-verification.md` §24。

## 作者面（成员集 = 基线 + 所有者保留 + Pylon 内部必需）

- 属性：`Uid/Name/DisplayName/Version/Logger/Folder/Assets/ContentAutoloadingEnabled/BackgroundAutoloadingEnabled/CloudAutoloadingEnabled`。
- 虚方法（恰好 10 个）：基线 8（`Load/PostSetupContent/AddRecipeGroups/AddRecipes/PostAddRecipes/Unload/Call/HandlePacket`）+ 所有者指定保留的 `OnModLoad/OnModUnload`。
- 非虚 API：`GetPacket/AddBossHeadTexture/AddNPCHeadTexture/AddContent×2/GetContent×2/GetLocalizationKey/GetLocalization/Find/TryFind/FileExists/GetFileBytes/GetFileStream/GetFileNames/CreateRecipe`。
- Pylon 内部（非作者面）：`Directory/DataDirectory/Types/Plan/Index/RegisterImGuiTab(protected)/LoadAsset/LoadTexture/LoadConfigs`。

## 系统时机归位（§24 削减）

- 系统时机钩子（世界生命周期/每帧更新/绘制环境/世界生成/装载杂项）**全部只在 ModSystem 上**（见 content-system.md）；Mod 级同名虚方法已删，HookId 枚举项保留（位集编号安全）。
- `HijackGetData/HijackSendData` 归位 ModSystem（基线形状）；`ConsumeBait/GetDyeTraderReward` 归位 ModPlayer（基线 `ModPlayer.CanConsumeBait(Item)/GetDyeTraderReward(List<int>)`，Mod 级是旧发明面）。
- `PostWorldLoad`（基线全库无）与 Mod 级 `SetupContent`（基线 Mod 无）整体删除，无 ModSystem 对应。
- 每个时机入口只派 `SystemLoader.*`：世界生命周期/更新链/场景计数在 `Hooks/ModLifecycle.cs`；九个中体桥阶段与 Transform/TimeRate/DyeReward 在 `Pipelines/System/SystemStages.cs`；PreUpdateProjectiles/阳光/亮度/本地化在 `Hooks/ModEnvironmentHooks.cs`；世界生成在 `Hooks/ModWorldGenHooks.cs`；绘制三时机门在 `Pipelines/InterfaceLayer/SystemTable.cs`；网络两钩在 `Pipelines/Net/NetReceiveHooks.cs`+`NetPipeline.cs`；装载期步在 `Install/InstallPlan.cs`（仅保留 Mod.PostSetupContent/Mod.AddRecipeGroups/Mod.PostAddRecipes 三个 HookStep + RecipesRegister 的 mod.AddRecipes）。
- 入口收敛：`On_Main.DoUpdateInWorld` 只有 `ModLifecycle.PreEntitiesPostEverything` 一个订阅（原 SystemStages 双订阅在削减中一并移除）。

## 测试

- 受影响组真机验证：system 2/0/0、system-w 11 pass（单帧 20 钩全序/时率/TileCounts/存读往返/绘制链/网络往返）、interface+interface-w（菜单/世界两阶段层序列）、environment-w 3/0/0、net-w 3/0/0；全量回归对照 §23 的 439/31/6 逐类一致。
- `ConsumeBait/GetDyeTraderReward` 的 ModPlayer 面在 RegressionTest（RegTestPlayer）手动 ImGui 页验证。
