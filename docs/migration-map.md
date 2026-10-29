# Pylon.Loader 迁移地图（上游参考 → Pylon）

> 本文件是 Pylon.ModLoader 与上游参考实现的逐项对照表。Pylon 是独立实现的 C# Mod 框架，
> 只在行为语义上按上游的实测结论对齐，**不复制其代码**。
> 依据：对上游源码（app/src/main/cpp/script/js/mod）三个方向的精读盘点——
> A. Core 基建  B. 内容类管线（Item/NPC/Projectile/Buff/Mount/Prefix/Rarity/Player/Tile/Wall/Recipe/System/Global*）
> C. 战斗/世界/网络/玩家管线。
> 每个 Loader 标注：职责、hook 的对象、踩坑、"Pylon 现状"（有/无/半）。

## 0. 总纲：上游参考的两条铁律

1. **注册期装 detour、内容就绪后才动游戏状态/表格。**
   - 注册（Mod.Load 阶段）：类型占号、`Hooks.Once(key, install)` 登记 hook（key 全进程去重，首次注册立即装）。
   - 内容就绪（`bl.onContentReady` → Ready 八阶段）：`resize → setup → groups → mods → content → recipes → postRecipes → finish`，每阶段 `Safe.Run` 隔离。
   - 凡"表的 cloneResized / SetStaticDefaults / MountData 构造 / CanRightClick 冒烟 / 配方与掉落解析"都进 Ready，不在注册期做。
2. **护城河三件套**（性能 + 防崩）：
   - 原生过滤器：`{minType, on, arg, field, tile, tileAt, wall, marks, flag, whileIn}`——只有被标记/符合条件的类型才进 JS（每帧/每格 hook 靠它保性能）。
   - `hookMarks`（mark→type 集合）+ `hookFlags`（运行期布尔开关）。
   - `Safe.Run(label, fn)` 包每段 mod 代码 + `Safe.Once(key, ...)` 每 key 一条日志去重。

## 1. 注册顺序（ContentAutoload.#ORDER = 类型 ID 分配顺序，注释即依赖证明）

```
ModConfig → ModRarity → DamageClass → ModMount → ModBuff → ModPrefix → ModPlayer
→ ModSurfaceBackgroundStyle → ModUndergroundBackgroundStyle → ModWaterfallStyle → ModWaterStyle
→ ModBiome → ModSceneEffect → ModMenu → ModBossBar → ModNPC → ModProjectile → ModItem
→ ModTileEntity → ModPylon → ModTile → ModWall → ModTree → PlayerDrawLayer → Mod（世界/每帧时机，原 ModSystem）
→ ModScreenEffect → GlobalItem → GlobalNPC → GlobalProjectile → GlobalLoot → GlobalTile
→ GlobalBuff → GlobalBossBar → GlobalRecipe → ModCommand → ModHair → ModCloud
→ ModEmoteBubble → ModAchievement
```

依赖理由（迁移时必须保持）：
- config 先：内容注册时可读选项；rarity 先于 item（SetDefaults 要 RarityType）。
- mount 先于 buff/item（要 MountType，且 mount 的 SetStaticDefaults 延迟到 setup）。
- damageClass 先于 item/projectile；biome 先于 NPC（NPC 的 SetStaticDefaults 在注册期跑，Happiness 要 biome）。
- tileEntity 先于 tile（tile 的 SetStaticDefaults 要 TE）；cloud 自动装载在 Mod.Load 之后。

## 2. Core 基建 → Pylon 现状与对应

| 上游 | 职责 | Pylon 现状 | 对应/迁移 |
|---|---|---|---|
| Hooks.js | Once 幂等 / Overrides 覆写检测 | 有 | `Core/Hooks.cs`（`Once` 全局去重；`Overrides` 按 `DeclaringType` 判是否覆写）；分派侧用 `OverridePlan` |
| Hook 安装脚手架 | 各 Loader 的 Find/Patch 反射 | **统一** | `Core/GameHooks.cs`：声明表（类型/方法/参数或参数个数/前后置/处理器名）→ 找方法 → 幂等挂补丁。Buff/Mount/Npc/Projectile/Rarity/Item/Prefix/Recipe 八个 Loader 已迁移，各自的 `Target`/`Patch`/`Find` 全删；[GamePatch] 属性式（Patch/ 目录）与 TouchButtonHooks 的 label+Prefix/Postfix 式保留（自洽的另一套封装） |
| Safe.js | Run/Report/Once 错误隔离 | 有 | `Core/Safe.cs`（Run/Report/Once）；`ModLogger` 提供 Error/Info/Warn 出口 |
| Ready.js | 八阶段内容就绪队列 | 有 | `Core/Ready.cs` 八阶段，已接入 `ContentInstaller`（resize→setup→groups→mods→content→recipes→postRecipes→finish） |
| Templates.js | class→模板实例注册表 | 有 | `ModContent.ByClass`（等价 Templates.Get/All） |
| ContentLookup.js | Find/TypeOf/Request 查找 | 半 | `ModContent.Find/TryFind/Request` 已有；TypeOf→0、歧义 Once 警告缺 |
| Entities.js | 模板 vs 实体实例、绑定 | 部分 | Item / Projectile / NPC 已有（`Core/*Entities.cs`）；其余实体类型迁移时按同一模式补 |
| GlobalRegistry.js | 按实体 Global 表 + perEntity | 部分 | GlobalItem / GlobalProjectile / GlobalNPC / GlobalBuff 已有 registry；GlobalTile / GlobalLoot / GlobalBossBar 未接线 |
| ContentAutoload.js | 注册编排 + #ORDER | 半 | `Mod.DiscoverTypes` 按类名排序；要换成 #ORDER |
| ModRegistry.js | mod 注册表 + Call 上下文 | 有 | `ModLoader.Registry`（Pylon.ModLoader）/ `Pylon.Managed.ModLoader` |
| Lang.js / ModLocalization.js | 本地化 | 半 | ModLocalization 已有；LocalizationLoader（读 JSON→Language 表）缺 |
| ModFiles.js | 路径/纹理解析 | 半 | PylonTexture 有 LoadAsset；`Assets/` 前缀/legacy 回退缺 |
| ModNet.js | 自定义网络通道 | 有（`Loaders/ModNet.cs`） | 消息 82 模块 0xB17E；类型标记二进制载荷；ModPacket/世界/NPC/弹幕/Global；未接 HijackGetData |
| Ref / Safe 桥 | out/ref 包装 | **判定：C# 不需要** | `Ref<T>` 是上游为 JS 无 out/ref 语义造的 `{value}` 包装；C# 原生 `ref`/`out` 更地道。Patch 桥接层已在用原生 ref/out；框架公共 API（ModItem/GlobalItem 钩子签名）里的 `Ref<T>` 在 Phase 1 已统一迁移成原生 `ref`，反射验证完整。`Safe.Run/Once` 由 `Core/Safe.cs` 提供 |

## 3. 内容类管线

| 上游 Loader | 行数 | hook 对象 | 关键坑 | Pylon |
|---|---|---|---|---|
| ItemLoader | 589 | ItemCheck_* / Item.Clone / ApplyEquip* / ItemCheck_Shoot / Main.DrawItem / WorldItem.UpdateItem | Clone 复制 60 字段（无 CloneDefaults）；UseItem true 要手动 ApplyItemTime；1.4.5.8 地面物 WorldItem 包裹（inner.type）；CanRightClick→OpenableBag 表必须标；RegisterAnimation 要被 InitializeItemAnimations 清 | **有（`Loaders/ItemLoader.cs`；使用/装备/背包/射击；SetDefaults 由 Patch、tooltip 由 TooltipLoader）** |
| NPCLoader | 483 | NPC.GetChat/FindFrame/NPCLoot/AI（AIType 临时换 type）/NewNPC/UpdateNPC_BuffSetFlags/ScaleStats_ByPlayerCount/DrawNPCDirect×2 | netMode==1 客户端直接 original（掉落只服务端算）；AI 本地/全局用 hookFlags 互斥；DrawBatches.Restart 手机批次隔离 | **有（`Loaders/NpcLoader.cs`）** |
| ProjectileLoader | 737 | Projectile.AI/HandleMovement/GetCollisionParams/Damage/DrawProjDirect 等 | AI 无条件装；originalDamage 游戏只填自己的类型；grapple 全套 vanala 数值表硬编码；`__projectileMovement` 桥字段要先 Define；绘制 4 个 EntitySpriteDraw 过滤器按纹理分 extras/sprite | **有（`Loaders/ProjectileLoader.cs`）** |
| BuffLoader | 336 | Player.UpdateBuffs（i-- 补偿）/NPC.UpdateNPC_BuffSetFlags/AddBuff_*（重复判 `false=已有`）/Lang.GetBuffName+Main.GetBuffTooltip（#texting 重入锁）/GUIBuffs.RemoveBuff/ItemDraw | type===0 break；GUIBuffs 图标绘制用 SpriteBatch.Draw 拦截（buff.look flag） | **有（`Loaders/BuffLoader.cs`）** |
| MountLoader | 297 | Mount.SetMount/DoSpawnDust/Dismount/UpdateEffects/UpdateFrame/Draw | **表没扩崩：MountID.Count++ 但 mounts/mounts.Sets 要 setup 阶段 cloneResized**；Mount.JumpHeight/JumpSpeed 无 player 参数（255 遍历反查）；PlayerDrawSet.DrawDataCache 定长（AddDrawData 不查界） | **有（`Loaders/MountLoader.cs`）** |
| PrefixLoader | 402 | GetRollablePrefixes/CanRollPrefix/RollAPrefix/TryGetPrefixStatMultipliers/Prefix/GrantPrefixBenefits | PrefixID.Count 无编译期上限，扩 Lang.prefix+ReducedNaturalChance；**换语言重建 Lang.prefix→钩 InitializeLegacyLocalization**；手机 maxStack 全 9999→判定用 damage/ammo/consumable | **有（`Loaders/PrefixLoader.cs`）** |
| RarityLoader | 131 | ItemRarity.Initialize（再调一次立即生效）/GetPopupRarityColor/Item.Prefix（field:'rare'）/Main.MouseTextInner | 游戏把 raridade 钳 11，不接 GetPrefixedRarity 掉紫色 | **有（`Loaders/RarityLoader.cs`）** |
| PlayerLoader | 418 | Player 全周期 + InternalSavePlayerFile/LoadPlayer（.plr.bl.json） | 每玩家每类懒实例；GetModPlayer 跨 mod 重名 Once 警告 | **有（`Loaders/PlayerLoader.cs`）**：每玩家每类懒实例（Initialize）、ResetEffects+ModifyMaxStats、Pre/PostUpdate、Pre/PostUpdateBuffs、UpdateEquips/PostUpdateEquips、UpdateBadLifeRegen/UpdateLifeRegen、UpdateManaRegen、UpdateDead、UpdateMovement、OnEnterWorld、OnRespawn、CanUseItem、PreKill/Kill、PreModifyLuck/ModifyLuck、侧车存档（`.plr.bl.json`，`TagCompound` + `TagJson`，未知 Mod 的键保留）。**未接**：命中/受伤/射击/绘制/跳跃/网络那一整批（Combat/PlayerItem/Draw/Jump/Network Hooks），以及 `GetModPlayer` 跨 mod 重名告警。 |
| TileLoader | 464 | GetPickaxeDamage/TileFrameImportant/TileFrame/CanKillTile/KillTile×4 | MARKS 13 组懒装；**HasOutlines 纹理不存在必须 Sets.HasOutlines[type]=false（否则画不存在的贴图）**；房问 CountsAs*Types 是列表要 cloneResized；CraftingStationItemId 手机专属表 | **有（`Loaders/TileLoader.cs`）**：占号/贴图/表扩容（含 `Main`、`TileID.Sets` 及嵌套、`WorldGen`、`MapHelper`、`TileMaterials`，模式填充）、`tileMerge` 行扩容与 `SetupTileMerge` 重放、`TileObjectData._data` 补齐+解锁、`Player.adjTile`/`SceneMetrics._tileCounts`/`GUICrafting.oldAdjTile` 扩容、编译期 754 上限（内核 `PylonMod_PatchCompareLimit`）、SetStaticDefaults/PostSetDefaults、房问/CraftingStation/描边、挖/框/掉/尘/声/放/随机/线/锤/光/动画/绘制数据/右键/鼠标悬停。**未接**：PreDraw/PostDraw/SpecialDraw/EmitParticles（需单独绘制 pass）、地图颜色/图例、门（本版只有 TileID.OpenDoor/ClosedDoor 单类型）、坐/睡/锁箱、音乐盒。 |
| WallLoader | 270 | Lighting.LightTiles/Main.DoUpdateInWorld/Framing.WallFrame/Main.DoUpdate_AnimateWalls/KillWall×4 | LIGHT_PADDING=28 三片轮流摊帧；KillWall 链用 #drop 状态跨方法 | **有（`Loaders/WallLoader.cs`）**：WallID.Count 顶到 mod 槽位、wallBlend、静态默认值、挖/尘/声/掉/边框/随机/光/动画/GetItemDrop。**未接**：地图项（与 Tile 同一套 MapHelper 颜色表）。 |
| RecipeLoader | 280 | Recipe.FindRecipes（original 前后插条件过滤）/GetIngredientCraftingDiscount/CraftItem_GrantItem | **MAX_RECIPES 运行时读（1.4.5.8.6 是 3700，写死 3600 会双显）**；#beyond 补三路 + availableRecipe/Y cloneResized；Finish 顺序固定 | **有（`Loaders/RecipeLoader.cs`；含 GlobalRecipe 三入口）**。<br>⚠ **interop 把 dump 里的 private static（CreateRequiredItemQuickLookups / UpdateMaterialFieldForAllRecipes）生成为 public**：反射 `GetMethod(NonPublic|Static)` 反而找不到 → 派生表从不重建（真机：两条"找不到"警告、配方收尾残缺）。已改成直接调用。 |
| SystemLoader+4 Hooks（已并入 `Mod`） | 233+ | clearWorld/LoadWorld/InternalSaveWorld/SaveAndQuit/UpdateWorld/UpdateTime/SceneMetrics/GenerateWorld(GenPass)/DoUpdate_* 阶段注入/UpdateTimeRate(小数速率累积取整)/ModifyScreenPosition | 联机客户端世界走网络没有 LoadWorld；PlayerSceneMetrics 只在主角上生效；PassLegacy 用 WeakMap 包原生 | **有（`Loaders/ModLifecycle.cs`，API 直接在 `Mod` 上）**：`Mod` 现在同时是内容入口与世界/每帧入口（`OnModLoad`/`OnModUnload`/`ResizeArrays`/`PostAddRecipes`/`PostSetupRecipes`、`ClearWorld`/`LoadWorld`/`LoadWorldData`/`PostWorldLoad`/`SaveWorldData`/`PreSaveAndQuit`/`OnWorldUnload`/`CanWorldBePlayed`、`UpdateWorld`/`UpdateTime`/`PreUpdateEntities`/`PostUpdateEverything`/Dust/Invasion/UI/Input、`ResetNearbyTileEffects`/`TileCountsAvailable`）。**未接**：世界生成 GenPass/ModifyWorldGenTasks、ModifyTimeRate、ModifyScreenPosition/TransformMatrix、光照颜色、网络。 |
| GlobalItemLoader 等 5 个 | 各 40-360 | 复用各自 Loader | 无类型过滤（原版也进）；GlobalRegistry cached/Attach | **部分（GlobalItem/GlobalProjectile/GlobalNPC/GlobalBuff 已接线；GlobalRecipe 已接；GlobalTile 已接：CanKillTile/KillTile/NumDust/CreateDust/KillSound/CanDrop/Drop/CanPlace/PlaceInWorld/RandomUpdate/AdjTiles/HitWire/Slope/RightClick/MouseOver/MouseOverFar/AnimateTile + SetStaticDefaults/PostSetupTileMerge；GlobalBossBar 仅 PostSetupContent；GlobalLoot 未接）** |

## 4. 战斗/世界/网络管线

| 上游 Loader | 行数 | 职责 | 关键坑 | Pylon |
|---|---|---|---|---|
| CombatLoader | 631 | 谁打谁挨全管线（StrikeNPC/Hurt 核心） | **手机 StrikeNPC 收"扣防前伤害"，服务端 msg28 重算→必须反推 #RawDamage**；friendly 命中要暂存；#striking 嵌套（爆炸） | **缺（HitModifiers/HurtModifiers 已声明）** |
| ItemCombatHooks / ItemUseHooks / ItemHealingHooks / ItemPickupLoader / ItemDrawLoader | 133-199 | 战斗 hook 聚合 / 弹药 / 治疗 / 拾取 / 绘制 | PickAmmo 每格 2 次（arguments switch 展开省 40%）；`Filter={minType:0,on:0,marks}` | **缺** |
| TouchButton 三件 | 208-302 | 触屏按钮 | 游戏数组按动作索引，60 帧复查；保存/分享码绝不含 mod 动作 | **有（完整）** |
| SpawnLoader | 161 | 自然生成四段 Edit | spawnRate 保底 1（Next 0/负炸）；sniper/binoculars 范围加成复刻 | **缺** |
| TileUseLoader | 389 | 右键/门/椅床/锁箱/音乐盒 | **手机每帧调 TileInteractionsUse 三次→#FreshPress 首帧去重**；触摸需 mouseOver 图标否则不交互；锁箱 #ShiftChest | **部分（并进 `TileLoader`）**：RightClick/MouseOver/MouseOverFar + 手机光标图标。**未接**：门、椅床、锁箱、音乐盒开关。 |
| TileWorldLoader | 147 | HitWire/RandomUpdate/NearbyEffects/AdjTiles/PlaceInWorld/Slope | bl.tiles.find 空间索引（zone 标记） | **缺** |
| TileDrawLoader | 282 | 光照(3帧)/动画/绘制数据/PostDrawTiles 主线程 pass | 绘制多线程 native 保留；新引擎 Color/White 下 LightTiles 内 AddLight 不生效 | **缺** |
| TileEntityLoader + ItemHooks | 346+283 | TE 占号 0x40000000+/位置 Key/侧车 JSON/Net | 不新增 C# 类（游戏不认未知类）；AfterPlacement 客户端先 SendTileSquare 再 ModNet | **缺（ModTileEntity/TEModdedPylon 已声明）** |
| Tree / Wing / Equip / ArmorSet | 206-238 | 树/翅膀/外观/套装 | 表 Count 先于数组会崩；WingStatsInitializer Load 后抄回；SetMatch/robes Ref | **缺** |
| Gore / Hair / Cloud / Emote / Achievement / Angler / Banner / Bestiary / BiomeBestiary / BossBar | 66-412 | 各小型管线 | CloudMasks 越界 NullRef→#GrowMasks 每周；Achievement #Reload 防计数翻倍；Emote 用户设备不行 Enabled=false | **缺（Mod* 均声明）** |
| TownNPC / Biome / BiomeShopping / SceneEffect | 213-273 | 城镇/生态/场景 | UpdateBiomes 只本地调；SceneEffect 平手按 key 排序；MushroomBiome 代理 | **部分（`Loaders/TownNpcLoader.cs`）**：搬进空房（CanTownNPCSpawn）、房间条件、名字表、聊天按钮、OnChatButtonClicked + `NpcShop` 注册/装货/打开。**未接**：城镇头像/派对/微光材质、Happiness、ModifyActiveShop、AddShops 的自动调用（作者在 PostSetupContent 里自己 Register 即可）、城镇 NPC 攻击（AI 10/12/14）；Biome/SceneEffect 的激活分派仍未接。 |
| ChestNetwork / ItemNetwork / PlayerNetwork / PvpItem / ServerSync / ItemData | 74-286 | 网络与存档 | **数据包先于原生消息同一 TCP 流到达**；5=槽 21=世界 32=箱；持久 ID key；10s/64 上限；PvP 只传 type/prefix | **部分（`Loaders/ModNet.cs`）**：模块 id 0xB17E（消息 82）通道、`ModPacket` 收发、`Mod.NetSend/NetReceive`（原生消息 7 之后）、`ModNPC/ModProjectile.SendExtraAI/ReceiveExtraAI`（23/27 之后，弹幕按 key 匹配）、`GlobalNPC/GlobalProjectile.NetSend/NetReceive`、`HijackSendData`。**未接**：`HijackGetData`（MessageBuffer.ProcessData 重写）、物品/箱子/玩家/PvP/ServerSync/ItemData 的专用信封、断线重连清理。 |
| PlayerItemHooks / JumpHooks / UpdateHooks / WorldHooks / DrawHooks | 89-404 | 玩家侧 hook 分组 | ItemCheck 包围；ExtraJump 位掩码；DrawPlayer_UseNormalLayers 46 原生层 | **缺（ModPlayer 已声明）** |
| LocalizationLoader | 327 | Localization/<文化>.json → LanguageManager | {$引用} 相对解析；换语言 ApplyAll 回填；tooltip InvalidateTooltips | **半（ModLocalization 工具已有；JSON 文件装载未接）** |
| ConfigLoader + ModConfigMenu | 267+1149 | 配置 | draft 机制；损坏文件回退 | **有（ModConfigLoader + UI 完整）** |
| MusicLoader / SoundLoader / ModMusic / VanillaMusic | 80-227 | 音乐/音效 | 手机 MediaPlayer；声音 0 拒绝；slot 0 无效 | **部分（`Loaders/MusicLoader.cs`、`Loaders/ModMusic.cs`、`Loaders/SoundLoader.cs`、`Core/AndroidMedia.cs`）**：Assets/Music 与 Assets/Sounds 解析、MediaPlayer 播放/循环/音量、MusicLoader.GetMusicSlot/MusicExists/AddMusicBox/IsMusicPlaying、Mod 场景/NPC 优先级选曲与 0.005 淡入淡出、LegacySoundStyle(1000/Style) 拦截、SoundEngine 句柄/状态/停止。**未接**：菜单主题音乐（ModMenu.Music）、原版音乐阶梯比较（VanillaMusic.Tier）、音高的 `PlaybackParams`、音乐盒装备栏触发。 |
| MenuLoader / HostSettingsMenu / ScreenEffectsSetting / MenuThemeSetting | 76-400 | 菜单/设置 | 手机 GUI 特定入口点 | **缺（ModMenu 已声明）** |

## 5. 迁移路线图（建议顺序）

1. **Phase 0**：重构目录（**已完成**）——按 `Core/Content/Globals/Contracts/Loaders/Patch/Textures/Tooltips/UI` 归位，namespace 不变（`Pylon.ModLoader`），纯物理组织，编译已通过 + 真机验证。
2. **Phase 1**：Core 基建补全（**基本完成**）——
   - Ready 八阶段（`Core/Ready.cs`，已完成并接入 ContentInstaller，真机验证阶段序正确）；
   - Safe.Once/Run 错误隔离（`Core/Safe.cs` 已完成）、Hooks.Once/Overrides（`Core/Hooks.cs` 已完成）；
   - **删除 `Ref<T>`（已完成）**：公共 API 钩子签名迁移成原生 `ref`/`out`（TryGetMod→out），删 Ref.cs，反射验证完整；
   - ContentAutoload #ORDER 对齐（**待做**；目前 `Mod.DiscoverTypes` 按类名排序）；
   - per-entity 实例化与注册表（**Item / Projectile / NPC 已完成**：`Core/{Item,Projectile,Npc}Entities.cs` 与对应的 Global 注册表，已接入各自 SetDefaults 与使用链）。
3. **Phase 2**：内容类 Loader —— **ItemLoader 已完成使用/装备/背包/射击全链**（`Loaders/ItemLoader.cs`）：
   - CanUseItem/UseItem（+ApplyItemTime）/UseStyle/HoldStyle/装备/背包更新——真机验证；
   - **射击链**：`ItemCheck_Shoot` 门（CanShoot+上下文）→ `Projectile.NewProjectile`（**float 版 13 参**，ModifyShootStats+Shoot，ai0-2 不声明避免 Harmony 置零）——真机验证 ModifyShootStats/Shoot/Global 分派全触发；
   - **ProjectileLoader 已完成**（`Loaders/ProjectileLoader.cs`，真机验证整条生命周期）：
     AI（OnSpawn 首帧 → PreAI → 游戏 AI（AIType 期间换 type）→ AI → PostAI）、
     `ApplyStatsFromSource`（originalDamage）、移动三件套（HandleMovement + GetCollisionParams + Kill 延迟：
     TileCollideStyle / OnTileCollide / ShouldUpdatePosition 未接）、Kill（PreKill/OnKill）、
     Damage（CanDamage/MinionContactDamage/Colliding 的临时表位）、Colliding、Damage_GetHitbox（ModifyDamageHitbox）、
     CanCutTiles/CutTiles（#cutting 重入锁）、GetAlpha、DrawProjDirect（PreDrawExtras/PreDraw/PostDraw）、
     钩爪（FireGrapple 的 CanUseGrapple/UseGrapple、AI_007 落点、GrappleRange/RetreatSpeed/PullSpeed/TargetPoint）。
     配套：`Core/GlobalProjectileRegistry.cs`（模板/实体两级的 Global 分派）、`Core/ProjectileEntities.cs`（Rebind 防槽位复用）、
     `Core/ProjectileDefaults.cs`（出厂值重置 + 上游 finishModProjectile 三件收尾：active=1、按 scale 收尺寸、maxPenetrate=penetrate）。
     **未接**：NumGrappleHooks（覆写了会有一条一次性警告）、
     PreDraw false 时保留游戏附加物（见第 6 节"不要 patch SpriteBatch"）。
   - **NpcLoader 已完成**（`Loaders/NpcLoader.cs`，真机验证：史莱姆可见/受击/死亡/绘制）：
     AI（Global PreAI → Mod PreAI → 游戏 AI（AIType 换 type）→ Mod AI → Global AI → Mod PostAI → Global PostAI）、
     NewNPC（OnSpawn）、UpdateNPC_BuffSetFlags（ResetEffects）、checkDead/CheckActive/FindFrame（frame 宽高由框架先填，
     作者只管 frame.Y）、NPCLoot（PreKill/OnKill，联机客户端跳过）、HitEffect、ScaleStats_ByPlayerCount
     （ApplyDifficultyAndPlayerScaling，这版 difficulty 是 float）、GetChat/CanChat、GetAlpha、
     DrawEffects/PreDraw/PostDraw（Main.DrawNPCDirect ×2）、BossHead 三件、CacheNPCDraws（DrawBehind）。
     配套：`Core/GlobalNpcRegistry.cs`、`Core/NpcEntities.cs`、`Patch/NpcSetDefaultsPatch.cs`（原版先跑 + mod 默认值 +
     finishModNpc：active/netID/defDamage/defDefense/life/defLifeMax + ScaleStats）。

     **关键：上游的 `kLimitPatches`（原生改编译期 `NPCID.Count`/`BuffID.Count` 比较）在 C# 侧的等效做法**：
     · `Main.DrawNPCs` 里 `type > NPCID.Count-1` 会跳过 mod NPC → 我们在它的 prefix 里对每个 active 的 mod NPC
       直接调 `Main.DrawNPCDirect` 补画（两趟按 behindTiles 分层）；
     · `NPC.SetDefaults` 对未知类型提前 return（清字段/scale/color 都不做）→ `NpcSetDefaultsPatch.ResetDefaults`
       按原版尾部默认值补齐（scale=1、color=White、alpha=0、frame 清零、宽高留 0 交给 Finish 按贴图算）；
     · `GUIBuffs.Draw` 的 `BuffID.Count` 比较会跳过 mod buff → 见下面的 BuffLoader"宿主槽位"方案。
   - **BuffLoader 已完成**（`Loaders/BuffLoader.cs`，真机验证：图标+倒计时+每帧更新）：
     Player.UpdateBuffs / NPC.UpdateNPC_BuffSetFlags（UpdatePlayer/UpdateNPC，index 位移补偿）、
     AddBuff_ActuallyTryToAddTheBuff（ApplyPlayer）、AddBuff_TryUpdatingExistingBuffTime（ReApplyPlayer）、
     NPC.AddBuff（ApplyNPC/ReApplyNPC）、Lang.GetBuffName + Main.GetBuffTooltip（ModifyBuffText，#texting 重入锁）、
     GUIBuffs.RemoveBuff（CanRemove/OnRemove + Global RightClick）、GUIBuffs.ItemDraw（PreDraw/PostDraw）。
     配套：`Core/GlobalBuffRegistry.cs`、`ContentInstaller` 里的 **buffImmune 实例数组扩容**（上游 growInstanceTable：
     Player/NPC 的 buffImmune 是每实例 bool[BuffID.Count]，不扩长就加不上 mod buff）与 buff 名字/说明写 Lang 缓存。

     **buff 图标显示（编译期上限的等效）**：`GUIBuffs.Draw` 里有 `BuffID.Count` 比较，mod buff 不会被画。
     做法与上游在 FindFrame 里换 type/贴图同源：在 `GUIBuffs.Draw` / `UpdateBuffNavigation` 执行期间，把
     mod buff 的 buffType 临时换成"当前没占用的原版 buff"，同时把该原版槽位的 `TextureAssets.Buff`、
     `Lang._buffNameCache/_buffDescriptionCache`、`Main.debuff` 换成 mod 的；调用结束立即还原。
     图标/时间/点击仍按 buff 索引走，所以位置和计时正确。
   - **MountLoader 已完成**（`Loaders/MountLoader.cs`，真机验证：骑上/跳跃/AI/绘制全链、玩家与 UI 正常）：
     SetMount/DoSpawnDust/Dismount（skipDust 语义 + SpecificData 清理）、UpdateEffects、UpdateFrame（false 跳过原版动画）、
     JumpHeight/JumpSpeed（手机版没有 Player 参数 → 用 mount 地址反查骑它的玩家）、UseAbility/AimAbility、
     Draw（每层一次：算出原版默认值 → mod 的 ref 参数可改 → 没改交回原版，改了塞进 DrawDataCache 并跳过原版）。
     配套：`ModMount.Get/SetSpecificData`、`ContentKind.Mount` + `ModContent.MountType<T>()`、MountData 安装
     （`_Back/_BackGlow/_BackExtra/_BackExtraGlow/_Front/...` 八层贴图直接写 MountData 的 Asset 字段）。

     真机踩到的四个点：
     ① **MountID.Count 要在注册时就 +1**（上游 `Add()` 同款），不能等 setup；
     ② `Mount.mounts` / `MountID.Sets` 的 12 张 bool 表要跟着变长，且**必须用托管数组一次性隐式转换赋值**
        （在新 il2cpp 数组上逐元素写 = SIGSEGV 0x7cf774）；
     ③ **`Mount.Initialize()` 会重建 Mount.mounts**（游戏内容初始化/换语言），mod 槽位会变 null
        → 骑上后 `_data == null`，`Mount.get_BlockExtraJumps`/`get_PlayerXOFfset` 每帧 NRE（黑屏/玩家与 UI 消失）。
        所以 `Mount.Initialize` 挂了 postfix 重新填表（`Reapply`，可重复调）；
     ④ 新建的 `MountData.playerYOffsets` 是 **null**，原版 `Mount.get_PlayerOffset` 直接索引它
        → 每帧 NRE。`InitFrameDefaults` 里按 totalFrames 建 int[]。
     ⑤ 名字/说明的注册**不能挂在"贴图存在"分支里**：`TestMountBuff` 没贴图时连名字都没写进 Lang，
        buff 条就显示宿主（黑曜石皮肤药剂）的贴图与名字。现在 `WriteTextures` 先注册文本再写贴图；
        且 BuffLoader 的"宿主槽位"映射遇到没贴图的 mod buff 直接跳过（宁可不显示，也不冒用原版图标）。
   - **PrefixLoader 已完成**（`Loaders/PrefixLoader.cs`，真机验证：安装/表扩容/无异常）：
     占号（`PrefixID.Count` 注册时 +1）、`Lang.prefix` + `PrefixID.Sets.ReducedNaturalChance` 扩容（托管数组赋值）、
     名字写 Lang + 语言键表；钩子：`Item.GetRollablePrefixes`（postfix 合并 mod 前缀）、`Item.CanRollPrefix`
     （Custom 类走 AllowPrefix+CanRoll）、`Item.RollAPrefix`（ChoosePrefix → 权重 RollChance → 加权抽）、
     `Item.TryGetPrefixStatMultipliersForItem`（ModPrefix.SetStats/ModifyValue/AllStatChangesHaveEffectOn）、
     `Item.Prefix`（-1/-2/-3 语义、PrefixChance、应用后 Apply/ModItem.ApplyPrefix/GlobalItem.ApplyPrefix）、
     `Player.GrantPrefixBenefits`（ApplyAccessoryEffects）。
     物品分类（上游 Categories）：`PrefixLegacy.ItemSets` 是 **internal 嵌套类**，用缓存的 FieldInfo 反射读；
     mod 物品按 ModItem 的 MeleePrefix/RangedPrefix/MagicPrefix/SummonPrefix/WeaponPrefix + 是否可取前缀判定。
   - **RarityLoader 已完成**（`Loaders/RarityLoader.cs`）：占号（原版 12 之后）、`ItemRarity.Initialize` postfix
     往 `_rarities` 补颜色、`Item.GetPopupRarityColor` 接管 mod 稀有度、`Item.Prefix` 前后用
     `GetPrefixedRarity(offset, valueMult)` 重算（游戏会把结果夹到 11 紫色）。
     **未接**：地上物品名（`Main.MouseTextInner` 自绘那一段），要复刻整段绘制，之后再做。
   - **RecipeLoader 已完成**（`Loaders/RecipeLoader.cs`）：FindRecipes 的条件过滤与超出 maxRecipes 的补位、
     GetIngredientCraftingDiscount、CraftItem_GrantItem（OnCraft + GlobalRecipe）；派生表重建与 Finish 顺序按上游。
   - **Tile/Wall 已完成**（`Loaders/TileLoader.cs`、`Loaders/WallLoader.cs`）：表扩容（含 `Fill::Mode`）、
     `tileMerge`/`TileObjectData`/实例表、编译期 754 上限（内核立即数补丁）、静态默认值、
     挖掘/边框/破坏/掉落/尘土/声音/放置/随机更新/电线/锤子/光照/动画/绘制数据/右键交互。
   - **网络已接**（`Loaders/ModNet.cs`）：ModPacket、Mod.NetSend/NetReceive、ModNPC/ModProjectile 的 ExtraAI、Global 的 NetSend/NetReceive；专用 ItemNetwork/Pvp/ServerSync 未做。
   - **城镇 NPC 已接**（`Loaders/TownNpcLoader.cs`）：搬房、房间条件、名字、聊天按钮、商店；头像/幸福度/攻击未做。
   - **音乐音效已接**（`Loaders/MusicLoader.cs`、`Loaders/ModMusic.cs`、`Loaders/SoundLoader.cs`）：MediaPlayer 播放、场景/NPC 选曲与淡入淡出、LegacySoundStyle 拦截；菜单主题/原版阶梯/音高未做。
   - **未接**：BuffDrawParams 的改贴图/改坐标（不 patch SpriteBatch 的取舍，见第 6 节）、NumGrappleHooks、Tile 自定义绘制 pass/地图颜色/门/坐睡/锁箱。
4. **Phase 3**：战斗/世界管线（**未开始**：Combat→Spawn→Biome/SceneEffect→TileDraw 自定义 pass→小型十件）。
5. **Phase 4**：网络/存档（**部分完成**：ModNet 主通道；ItemNetwork/PlayerNetwork/Pvp/ServerSync/ItemData/TileEntity 专用信封未做）。
6. **Phase 5**：全量真机回归（**持续进行**：FrameMod 已在真机覆盖物品/工具提示/配置/按钮/菜单；每接一条新管线补回归项）。

## 6. 迁移时务必记住的坑（上游注释证实的错误模式）

- 表没扩会崩：Mount.mounts、MountID.Sets、Lang.prefix、Main.availableRecipe/Y、PlayerDrawSet.DrawDataCache、Terraria ID.Sets.*、BannerSystem.killCount 等——**Count 长了数组没长就访问越界**。
- 字典 KeyNotFound：ContentSamples、ItemRarity._rarities、**ContentLookup.#textures/ItemLoot.#byType 全 CheckThenSet**；`Find` 系列返回 undefined/null 不抛，只有显式 Get* 抛。
- 手机特有：SoundID 是旧整数；音乐 MediaPlayer；商店 OpenUI(2,4)；无键盘→ModTouchButton；玩家无可变击退；TileInteractionsUse 每帧 3 次；PvP 只传 type/prefix；NPC.Spawner 是手机 1.4.5 类。
- hook 顺序：Hooks.Once 幂等；SharedHooks before 按注册序、after 逆序、filter 与首个一致；cancel 型（HijackSend）返回 true 必须中断。
- Ref 语义：上游的 JS 用 `Ref<T>{value}` 包装 out/ref；Pylon 是 C#，公共 API 已统一用原生 `ref`/`out`，不要再引入包装类型。
- 实例 vs 模板：模板注册一次；实体实例 NewInstance（构造重跑+只叠加注册后改动）；InstancePerEntity=false 的 Global 直接复用模板。
- StatModifier JS 可变 vs C# struct：必须显式回写（get→改→set），否则增伤全失效。
- 一次告警：Safe.Once(key) 防刷屏；bl.error（开错误面板）vs bl.log。
- **手机版武器样式分流（Pylon 实踩，上游 docs/05-itens.md 证实）**：上游的"能射击的武器"是**远程**（`Item.ranged = true` + `shoot` + `noMelee` + `SetDefaultWeaponStyle` → useStyle=Shoot）→ 游戏走 `ItemCheck_Shoot` → ItemLoader 接管（CanShoot→ModifyShootStats→Shoot）；`useStyle=Swing`（近战）时游戏走近战剑气路径、**不调 ItemCheck_Shoot**、弹幕依赖 ai 数组。`Item.shoot` 是 **int**（不是 byte，弹幕 1000+，`(byte)` 溢出成错误 ID）。`SetDefaultWeaponStyle` **必须先设 `item.shoot` 再调用**（它读 shoot 决定样式，顺序反了 shoot=0 → 误判 Swing → 弹幕永不发射）。
- **数组扩容的坑（真机 SIGSEGV）**：不能 `new Il2CppStructArray<T>(len)` 然后逐元素写——interop 的 setref 在 ARM64 上直接崩（PC=`0x7cf774`，就是 `il2cpp_array_length` 读到坏指针）。同理**别用 interop 的数组包装去扩 `Nullable<T>[]` 表**：生成代理把它写成 `Il2CppReferenceArray<Nullable<T>>`，元素步长按指针算，`ItemID.Sets.CanBeQuickusedOnGamepad` / `ProjectileID.Sets.WindPhysicsImmunity` / `NPCID.Sets.HunterPotionFriendlyOverride` 这些会整批抛 `TargetInvocationException`，表没扩成、mod 类型一查就越界。现在的做法见 `TypeTables`：字段枚举、静态读写、`il2cpp_array_new`、`memcpy` 全走原生（`TypeTables::growArray` 的 C# 版），值类型数组按 `il2cpp_class_value_size` 一次整块拷、引用数组拷指针，尾部按 `[0]` 填；判"是不是数组"必须看 `il2cpp_type_get_type == SZARRAY`（这版 `il2cpp_class_get_element_class` 不做数组检查，会把 int 静态字段当数组指针崩在同一个 `0x7cf774`）。普通值类型数组的"托管数组 + 隐式转换"（`TouchButtonHooks.Sized`）仍然可用。
- **原版对 mod 类型有编译期上限**：`Main.DrawNPCs`/`GUIBuffs.Draw`/`NPC.SetDefaults`/`NPC.NPCLoot`/`Lang.GetNPCName` 里写死了 `NPCID.Count`/`BuffID.Count` 比较。上游用原生 `kLimitPatches` 改比较常量；IL2CPP 上 Harmony transpiler 无 IL 可改，Pylon 的等效做法是：绘制入口自己补画、SetDefaults 自己补原版尾部默认值、buff 图标临时映射到空的原版槽位。
  方块/墙壁另有一批直接编进 ARM64 指令的 `type > 753` / `type >= 754`（`WorldGen.PlaceTile/KillTile/TileFrameImportant`、`Collision.IsWorldPointSolid`、`TileData.GetTileDefinition` 等，名单见上游 `ModTiles.cpp:266-293`）。Pylon 在内核里实现了同一套立即数补丁：`PylonMod_PatchCompareLimit`（`src/core/src/mod/LimitPatch.cpp`），由 `Core/LimitPatch.cs` 在方块表定型后调用；方法体结尾按同类下一个方法定位，只改 `cmp` 后跟顺序条件跳转的指令。补丁失败只记日志，不静默。
- **方块表扩容要填"模式值"不是 0**：`TypeTables` 对 Tile 用 `Fill::Mode`（上游同款）。填 0 会让新方块默认 `tileSolid=true`（泥土的值），挖不动、挡光、边框全错。墙壁仍用 0。
- **`Main.tileMerge` 是 `bool[754][]` 的锯齿表**：外层长大不够，每一行也得是独立 `bool[total]`；`Main.SetupTileMerge` 会在进世界时整表重建，必须 prefix 存 mod 行/列、postfix 扩容后写回（`TileLoader.MergePrefix/MergePostfix`）。
- **`TileObjectData._data` 是 List 不是数组**：SetStaticDefaults 前要补到 `total`（null = 1x1），并临时把 `readOnlyData` 放开；静态默认值结束后关回去。
- **`Player.adjTile` / `SceneMetrics._tileCounts` / `GUICrafting.oldAdjTile` 是每实例 `[754]`**：不扩长时，第一次数到 mod 方块就写越界；`TileLoader.GrowInstances` 在安装时扩现有实例，`Maintain` 每 60 帧补新实例。

- **Harmony 在 IL2CPP 下**：补丁参数用**参数名绑定**（`__N` 位置绑定易错位）；NewProjectile 挂 **float 版 13 参**（游戏内部发射走它）；prefix 里**声明了 ref 但没赋值的参数（如 ai0/ai1/ai2）会被置零**破坏弹幕——不碰的参数别声明。
- **不要 patch Unity 的绘制入口（`SpriteBatch.Draw` / `Main.EntitySpriteDraw`）**：上游为了"PreDraw false 但保住鱼线/链条"在手机版用四过滤器拆绘；Pylon 照抄后**真机实测渲染全乱**——NPC/树/UI 图标出现"倒影"式重绘（SpriteEffects 与坐标被串）。结论：`Main.DrawProjDirect` 可以挂（普通游戏方法），Unity 引擎的 SpriteBatch 入口不要碰；`PreDraw=false` 就整只不画，需要附加物的 mod 自己在 PostDraw 里画。
- **`SceneMetrics.Scan(SceneMetricsScanSettings)` 不能挂 Harmony prefix（真机背景变海洋）**：`settings` 是 `ValueType`，方法本身按指针收（反汇编 `178BD94: mov x20, x1`），托管 prefix 声明成按值就等于让 Harmony 再解一层按值 struct。真机现象：出生点 `Main.bgStyle=4`（SurfaceBackgroundID.Ocean）、`Player.ZoneBeach=True`，地形却正常——`SceneMetrics.CalculateZones` 里 `ZoneBeach = WorldGen.oceanDepths(扫描中心x, 扫描中心y)`（`178DAB8` 调用点），扫描中心一脏整片背景变海滩。现在：不挂 `Scan`，`ModTile.NearbyEffects` 改到 `Main.DoUpdateInWorld` 之后按玩家周围自己扫（`TileLoader.NearbyTick`）；不自作主张改 `ZoneBeach`，扫描中心对不对由 `[world]` 那行日志（`sceneBeach`/`prefBg`/`oceanLevel`/`worldSurface`）直接暴露。
- **mod 音频别用"每个音效 new 一个 MediaPlayer"**：上游是 app 侧 `ModAudio.kt`——音效 SoundPool（解码/混音/音高），音乐 MediaPlayer `prepareAsync` + 每帧按 fade 设音量（0 就 pause+seek）。Pylon 现在照这个：`Core/AndroidAudio.cs`（SoundPool + 异步 MediaPlayer + `OnLoadCompleteListener`/`OnPreparedListener` 代理）、`SoundLoader`（注册即开始解码、句柄/状态/MaxInstances）、`ModMusic`（只设音量，不再自己持有播放器）。旧写法在真机上句柄/状态都对，但听不到。
- **Global 分派要用实体级过滤（`AnyFor(entity, …)`）而不是全局表（`AnyWith`）**：`OverridePlan` 目前是"基类声明即算覆写"，`AnyWith` 对任何 vanilla 实体都返回 true，会让每帧 hook 去拦所有原版弹幕（移动/命中全接管）。实体级 `AnyFor` 会先跑 `AppliesToEntity`，原版实体直接跳过。

## 7. 文档一致性

- 本文件是当前唯一的内容管线对照表；新增 Loader 后同步更新第 3 节的“Pylon”列与第 5 节的进度。
- `docs/mods/` 尚未建立：等某条管线稳定后，再按“签名 + 例程”给该管线补页，不预先铺空目录。
- 代码注释里引用的 `docs/mods/*.md` 与 `docs/NN` 是上游 / 早期笔记的编号，未随本仓库发布；
  现行文档以 `README.md`、`docs/构建-从零开始.md`、本文件与 `src/core/README.md` 为准。
- 保持"只作行为对照、不复制代码"的原则；代码注释简短意概，不再引用上游行号。

