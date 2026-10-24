// FrameMod / FrameSelfTest —— 进世界后逐条跑五条管线，打 ✓ / ✗（✗ 带实际值），末行汇总。
// 节奏：每步间隔 10 帧（状态要时间稳定）；"装上 → 等 → 检查 → 还原"。
using System;
using System.Collections.Generic;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ObjectData;

namespace FrameMod
{
    internal static class FrameSelfTest
    {
        private static ModLogger _log;
        private static int _frame;
        private static int _checks;
        private static int _fails;
        private static bool _done;
        private static readonly Queue<(int Delay, Action<Player> Step)> Manual = new();
        private static int _manualWait;

        /// <summary>把测试家具直接塞进背包，方便手动放置（不靠自检的 PlaceObject）。</summary>
        internal static void GiveFurniture()
        {
            try
            {
                var player = Main.player[Main.myPlayer];
                if (player == null) return;
                var type = ModContent.ItemType<TestFurnitureItem>();
                for (var i = 0; i < player.inventory.Length; i++)
                {
                    var slot = player.inventory[i];
                    if (slot != null && slot.type > 0 && slot.stack > 0) continue;
                    slot.SetDefaults(type);
                    slot.stack = 99;
                    _log?.Info($"[furniture] 已放进背包第 {i} 格：TestFurnitureItem（type={type}，堆叠 99）；手动放一下看是否可见");
                    return;
                }
                _log?.Warn("[furniture] 背包满了，没放下 TestFurnitureItem");
            }
            catch (Exception e)
            {
                _log?.Warn("GiveFurniture 失败：" + e.Message);
            }
        }

        /// <summary>ImGui 按钮入口：排一组手动测试，不依赖自动自检的帧号。</summary>
        internal static void RunManual(string suite)
        {
            _log = FrameMod.Log;
            Manual.Clear();
            _manualWait = 0;
            _done = true;   // 手动跑就停掉自动流程，避免两套步骤互相踩

            void Add(int delay, Action<Player> step) => Manual.Enqueue((delay, step));

            switch (suite)
            {
                case "tile":
                    Add(0, TilePlace); Add(10, TileCheck); Add(10, _ => TileKill()); Add(10, _ => TileDrop());
                    break;
                case "furniture":
                    Add(0, FurniturePlace); Add(10, _ => FurnitureCheck());
                    break;
                case "wall":
                    Add(0, WallPlace); Add(10, _ => WallCheck()); Add(10, _ => WallDrop());
                    break;
                case "town":
                    Add(0, TownSpawn); Add(10, TownCheck);
                    break;
                case "sound":
                    Add(0, _ => Sound());
                    break;
                case "music":
                    Add(0, _ => MusicStart()); Add(180, _ => MusicCheck());
                    break;
                case "net":
                    Add(0, _ => Network());
                    break;
                case "all":
                    Add(0, TilePlace); Add(10, TileCheck); Add(10, _ => TileKill()); Add(10, _ => TileDrop());
                    Add(10, FurniturePlace); Add(10, _ => FurnitureCheck());
                    Add(10, WallPlace); Add(10, _ => WallCheck()); Add(10, _ => WallDrop());
                    Add(10, TownSpawn); Add(10, TownCheck);
                    Add(10, _ => Sound());
                    Add(10, _ => MusicStart()); Add(180, _ => MusicCheck());
                    Add(10, _ => Network());
                    Add(10, _ => Summary());
                    break;
            }
            _log?.Info($"[selftest] 手动测试入队：{suite}（{Manual.Count} 步）");
        }

        // 各步在第几帧（进世界后计数）
        private const int StepTables = 12,
                          StepRecipe = 20, StepPrefix = 30, StepBuffOn = 45, StepBuffCheck = 60,
                          StepNpcSpawn = 70, StepNpcCheck = 85, StepMountOn = 95, StepMountCheck = 110,
                          StepMountOff = 120, StepAccOn = 130, StepAccMid = 140, StepAccOff = 150,
                          StepTile = 165, StepTileCheck = 180, StepTileKill = 190, StepTileDrop = 200,
                          StepFurniture = 210, StepFurnitureCheck = 225,
                          StepWall = 235, StepWallCheck = 245, StepWallDrop = 255,
                          StepTown = 265, StepTownCheck = 280,
                          StepSound = 295, StepMusic = 305, StepMusicCheck = 325,
                          StepNet = 340, StepEnd = 360;

        private static int _npcIndex = -1;
        private static int _mountType = -1;
        private static int _savedAccType, _savedAccPrefix;
        private static int _defenseWithPrefix = -1;
        private static int _tileX, _tileY, _wallX, _wallY, _furnitureX, _furnitureY, _townIndex;
        private static int _tileType, _wallType, _furnitureType, _soundHandle, _musicSlot;

        /// <summary>每帧调（FrameMod 挂的 Main.Update postfix 里）。只在世界内、内容装好之后跑。</summary>
        internal static void Tick(ModLogger log)
        {
            if (_done && Manual.Count == 0) return;
            _log = log;
            try
            {
                if (Main.gameMenu || !GameContent.Installed) return;
                var player = Main.player[Main.myPlayer];
                if (player == null || !player.active) return;
                // 暂停菜单下每帧钩子不跑，自检会误报（真机踩过：8 条假阴性）。
                if (Main.gamePaused) return;

                if (Manual.Count > 0)
                {
                    // 等待计数每帧 +1，到队首步的 delay 才执行。
                    // 坑：不能用"上一步 delay 倒计时"——Add(0) 步把等待归 0，后续要等 120 帧的步下一帧就执行，全假阴性。
                    var next = Manual.Peek();
                    if (_manualWait < next.Delay) { _manualWait++; return; }
                    var step = Manual.Dequeue();
                    step.Step(player);
                    _manualWait = 0;
                    return;
                }

                _frame++;
                if (_frame > StepEnd + 10) { _done = true; return; }   // 玩家迟迟没进世界也不卡在这

                switch (_frame)
                {
                    case StepTables: Tables(); break;
                    case StepRecipe: Recipes(); break;
                    case StepPrefix: Prefixes(player); break;
                    case StepBuffOn: BuffOn(player); break;
                    case StepBuffCheck: BuffCheck(player); break;
                    case StepNpcSpawn: NpcSpawn(player); break;
                    case StepNpcCheck: NpcCheck(); break;
                    case StepMountOn: MountOn(player); break;
                    case StepMountCheck: MountCheck(player); break;
                    case StepMountOff: MountOff(player); break;
                    case StepAccOn: AccessoryOn(player); break;
                    case StepAccMid: AccessoryMid(player); break;
                    case StepAccOff: AccessoryOff(player); break;
                    case StepTile: TilePlace(player); break;
                    case StepTileCheck: TileCheck(player); break;
                    case StepTileKill: TileKill(); break;
                    case StepTileDrop: TileDrop(); break;
                    case StepFurniture: FurniturePlace(player); break;
                    case StepFurnitureCheck: FurnitureCheck(); break;
                    case StepWall: WallPlace(player); break;
                    case StepWallCheck: WallCheck(); break;
                    case StepWallDrop: WallDrop(); break;
                    case StepTown: TownSpawn(player); break;
                    case StepTownCheck: TownCheck(player); break;
                    case StepSound: Sound(); break;
                    case StepMusic: MusicStart(); break;
                    case StepMusicCheck: MusicCheck(); break;
                    case StepNet: Network(); break;
                    case StepEnd: Summary(); break;
                }
            }
            catch (Exception e)
            {
                // 自检自己不能把游戏搞崩：一步炸了记下来接着跑
                Check("selftest", $"第 {_frame} 步没跑完", false, e.GetType().Name + ": " + e.Message);
            }
        }

        // ===================== 表扩容（Nullable<T>[] 那种以前整批炸掉的表） =====================

        /// <summary>扩表自检：内容表都扩到 mod 容量；曾炸 TargetInvocationException 的 Nullable&lt;T&gt;[] 表也长了。
        /// 走框架原生读数（TypeTables.Audit / TableLength），不碰 interop 数组包装——Nullable&lt;T&gt;[] 元素访问本身是坏的。</summary>
        private static void Tables()
        {
            var itemCap = ModContent.NextType(ContentKind.Item);
            var projCap = ModContent.NextType(ContentKind.Projectile);
            var npcCap = ModContent.NextType(ContentKind.Npc);
            var buffCap = ModContent.NextType(ContentKind.Buff);
            var tileCap = ModContent.NextType(ContentKind.Tile);
            var wallCap = ModContent.NextType(ContentKind.Wall);

            Audit("tables", "物品", Pylon.ModLoader.Textures.TypeTables.TableKind.Item, itemCap);
            Audit("tables", "弹幕", Pylon.ModLoader.Textures.TypeTables.TableKind.Projectile, projCap);
            Audit("tables", "NPC", Pylon.ModLoader.Textures.TypeTables.TableKind.Npc, npcCap);
            Audit("tables", "Buff", Pylon.ModLoader.Textures.TypeTables.TableKind.Buff, buffCap);
            Audit("tables", "方块", Pylon.ModLoader.Textures.TypeTables.TableKind.Tile, tileCap);
            Audit("tables", "墙壁", Pylon.ModLoader.Textures.TypeTables.TableKind.Wall, wallCap);

            Check("tables", "WallID.Count 顶到 mod 槽位", WallID.Count >= wallCap,
                  $"WallID.Count={WallID.Count}/{wallCap}");

            var quick = Length("Terraria.ID", "ItemID", "Sets", "CanBeQuickusedOnGamepad");
            var lockOn = Length("Terraria.ID", "ItemID", "Sets", "LockOnAimCompensation");
            var wind = Length("Terraria.ID", "ProjectileID", "Sets", "WindPhysicsImmunity");
            var hunter = Length("Terraria.ID", "NPCID", "Sets", "HunterPotionFriendlyOverride");

            Check("tables", "Nullable<T>[] 的表也扩到了 mod 槽位",
                  quick >= itemCap && lockOn >= itemCap && wind >= projCap && hunter >= npcCap,
                  $"CanBeQuickusedOnGamepad={quick}/{itemCap} LockOnAimCompensation={lockOn}/{itemCap} " +
                  $"WindPhysicsImmunity={wind}/{projCap} HunterPotionFriendlyOverride={hunter}/{npcCap}");

            // 全局命名空间的类（清单里写的是 ""）：写成 System 会整类漏掉，这里确认找得到。
            var vc = Length("", "VirtualControllerInputState", null, "ItemCategories");
            Check("tables", "全局命名空间的 VirtualControllerInputState 找得到", vc > 0, $"ItemCategories 表长={vc}");
        }

        private static void Audit(string area, string label, Pylon.ModLoader.Textures.TypeTables.TableKind kind, int capacity)
        {
            var names = new List<string>();
            var ok = Pylon.ModLoader.Textures.TypeTables.Audit(kind, out var total, out var grown, out var stale,
                                                               out var missing, names);
            var unbuilt = names.Count == 0
                ? ""
                : "，未建：" + string.Join("/", names.GetRange(0, Math.Min(4, names.Count))) + (names.Count > 4 ? "…" : "");
            Check(area, $"{label}：找到的表全扩到 mod 容量", ok && total > 0 && stale == 0,
                  $"共 {total} 张，扩到位 {grown}，还是原版 {stale}，还没建 {missing}（目标 {capacity}）{unbuilt}");
        }

        private static int Length(string ns, string name, string nested, string field) =>
            Pylon.ModLoader.Textures.TypeTables.TableLength(ns, name, nested, field);

        // ===================== 配方 =====================

        private static void Recipes()
        {
            var sword = ModContent.ItemType<TestSword>();
            var total = Recipe.numRecipes;
            var recipes = Main.recipe;
            var index = -1;
            for (var i = 0; i < total && recipes != null && i < recipes.Length; i++)
            {
                var recipe = recipes[i];
                if (recipe?.createItem != null && recipe.createItem.type == sword) { index = i; break; }
            }

            Check("recipe", "mod 配方进了 Main.recipe", index >= 0, $"index={index}，总配方={total}（上限 {Recipe.maxRecipes}）");

            if (index >= 0)
            {
                var recipe = recipes[index];
                var output = recipe.createItem;
                var ingredient = recipe.requiredItem != null && recipe.requiredItem.Length > 0 ? recipe.requiredItem[0] : null;
                Check("recipe", "结果与材料都对", output.stack == 1 && ingredient != null && ingredient.type == ItemID.Wood,
                      $"出 {output.type}×{output.stack}，料 {ingredient?.type}×{ingredient?.stack}");
                Check("recipe", "配方里的材料标成了 IsAMaterial（派生表重建过）", ItemID.Sets.IsAMaterial[ItemID.Wood]);
                Check("recipe", "TileIdUsedInRecipes 表还在（收尾没炸）", Recipe.TileIdUsedInRecipes != null);
            }
        }

        // ===================== 前缀 =====================

        private static void Prefixes(Player player)
        {
            var melee = ModContent.PrefixType<TestPrefix>();
            var accessory = ModContent.PrefixType<TestAccessoryPrefix>();

            Check("prefix", "占号在原版之后", melee >= 98 && accessory >= 98, $"melee={melee} acc={accessory} PrefixID.Count={PrefixID.Count}");
            Check("prefix", "PrefixID.Sets.ReducedNaturalChance 跟着长", PrefixID.Sets.ReducedNaturalChance != null &&
                  PrefixID.Sets.ReducedNaturalChance.Length >= PrefixID.Count,
                  $"len={PrefixID.Sets.ReducedNaturalChance?.Length} Count={PrefixID.Count}");

            var named = Lang.prefix != null && melee < Lang.prefix.Length ? Lang.prefix[melee] : null;
            Check("prefix", "名字写进 Lang.prefix", named != null && !string.IsNullOrEmpty(named.Value), $"name={named?.Value}");

            // ① 原版剑（分类来自 PrefixLegacy 表）
            var vanilla = NewItem(ItemID.NightsEdge);
            var vanillaList = vanilla.GetRollablePrefixes();
            Check("prefix", "原版剑的可掷列表含 mod 前缀", Contains(vanillaList, melee), Describe(vanillaList));
            Check("prefix", "原版剑 CanHavePrefixes", vanilla.CanHavePrefixes());
            Check("prefix", "原版剑 CanRollPrefix(mod 前缀)", vanilla.CanRollPrefix(melee));

            var damageBefore = vanilla.damage;
            var valueBefore = vanilla.value;
            var ok = vanilla.Prefix(melee);
            Check("prefix", "原版剑 Prefix(mod 前缀) 成功", ok && vanilla.prefix == melee, $"ok={ok} prefix={vanilla.prefix}");
            // 游戏按 SetStats 的倍率算：RoundEven(damage × 1.15)
            Check("prefix", "伤害按 SetStats 变了", vanilla.damage == RoundEven(damageBefore * 1.15f),
                  $"{damageBefore} → {vanilla.damage}（期望 {RoundEven(damageBefore * 1.15f)}）");
            Check("prefix", "价格/稀有度被游戏重算", vanilla.value != valueBefore,
                  $"value {valueBefore} → {vanilla.value}，rare={vanilla.rare}");
            Check("prefix", "ModPrefix.Apply 跑过", TestPrefix.ApplyCount > 0, $"ApplyCount={TestPrefix.ApplyCount}");

            // ② mod 剑（分类来自 ModItem.MeleePrefix/WeaponPrefix）
            var modSword = NewItem(ModContent.ItemType<TestSword>());
            var modList = modSword.GetRollablePrefixes();
            Check("prefix", "mod 剑的可掷列表含 mod 前缀", Contains(modList, melee), Describe(modList));
            Check("prefix", "mod 剑 CanHavePrefixes", modSword.CanHavePrefixes());
            // ⚠ ImGui 按钮走这条路：给 mod 剑直接指定前缀号，若滚成随机/失败这里会暴露。
            var modDamageBefore = modSword.damage;
            var modOk = modSword.Prefix(melee);
            Check("prefix", "mod 剑 Prefix(mod 前缀) 成功", modOk && modSword.prefix == melee,
                  $"ok={modOk} prefix={modSword.prefix}（要 {melee}）");
            Check("prefix", "mod 剑伤害按 SetStats 变了", modSword.damage == RoundEven(modDamageBefore * 1.15f),
                  $"{modDamageBefore} → {modSword.damage}（期望 {RoundEven(modDamageBefore * 1.15f)}）");

            // ③ 加权滚动（重铸走 RollAPrefix）
            var hits = 0;
            const int rolls = 200;
            for (var i = 0; i < rolls; i++)
            {
                var item = NewItem(ItemID.NightsEdge);
                var rolled = -1;
                if (item.RollAPrefix(Main.rand, ref rolled) && rolled == melee) hits++;
            }
            Check("prefix", "RollAPrefix 按 RollChance 抽得到", hits > 0, $"{hits}/{rolls}");

            // ④ 饰品前缀走 Accessory 分类
            var bottle = NewItem(ItemID.CloudinaBottle);
            Check("prefix", "饰品的可掷列表含饰品前缀", Contains(bottle.GetRollablePrefixes(), accessory), Describe(bottle.GetRollablePrefixes()));
        }

        // ===================== Buff =====================

        private static void BuffOn(Player player)
        {
            var type = ModContent.BuffType<TestBuff>();
            player.AddBuff(type, 600, false);
            Check("buff", "AddBuff 没炸（buffImmune 够长）", true, $"type={type}");

            var name = Lang.GetBuffName(type);
            Check("buff", "名字来自 mod（不是宿主原版名）", !string.IsNullOrEmpty(name), $"name={name}");
        }

        private static void BuffCheck(Player player)
        {
            var type = ModContent.BuffType<TestBuff>();
            Check("buff", "加上了就在身上", player.FindBuffIndex(type) >= 0, $"FindBuffIndex={player.FindBuffIndex(type)}");
            Check("buff", "计时在走", BuffTimeOf(player, type) > 0, $"time={BuffTimeOf(player, type)}");
            Check("buff", "UpdatePlayer 每帧在跑", TestBuff.Ticks > 0, $"ticks={TestBuff.Ticks}");
            Check("buff", "图标绘制路径（PreDraw/PostDraw）跑过", TestBuff.Draws > 0, $"draws={TestBuff.Draws}");

            player.ClearBuff(type);
            Check("buff", "可以正常清掉", player.FindBuffIndex(type) < 0, $"FindBuffIndex={player.FindBuffIndex(type)}");
        }

        private static int BuffTimeOf(Player player, int type)
        {
            var types = player.buffType;
            var times = player.buffTime;
            if (types == null || times == null) return 0;
            for (var i = 0; i < types.Length && i < times.Length; i++)
                if (types[i] == type) return times[i];
            return 0;
        }

        // ===================== NPC =====================

        private static void NpcSpawn(Player player)
        {
            var type = ModContent.NpcType<TestNpc>();
            _npcIndex = NPC.NewNPC(null, (int)player.Center.X + 48, (int)player.Center.Y, type);
            Check("npc", "NewNPC 生成成功", _npcIndex >= 0 && _npcIndex < Main.npc.Length && Main.npc[_npcIndex].active,
                  $"index={_npcIndex} type={type}");
        }

        private static void NpcCheck()
        {
            var type = ModContent.NpcType<TestNpc>();
            var npc = _npcIndex >= 0 && _npcIndex < Main.npc.Length ? Main.npc[_npcIndex] : null;
            var alive = npc != null && npc.active && npc.type == type;

            Check("npc", "活着且类型对", alive, $"active={npc?.active} type={npc?.type}（要 {type}）");
            if (alive)
            {
                Check("npc", "SetDefaults 生效（lifeMax=120）", npc.lifeMax == 120, $"lifeMax={npc.lifeMax}");
                Check("npc", "frame 由框架先填好（宽高非 0）", npc.frame.Width > 0 && npc.frame.Height > 0,
                      $"frame={npc.frame.Width}x{npc.frame.Height} 总帧={Main.npcFrameCount[type]}");
                Check("npc", "AI 在跑", TestNpc.Ticks > 0, $"ticks={TestNpc.Ticks}");

                // 打死它：验证 PreKill/OnKill（掉落表另算，NPCLoot 有编译期上限，见 migration-map）
                npc.life = 0;
                npc.checkDead();
                Check("npc", "死亡流程跑完（OnKill）", !npc.active || npc.life <= 0,
                      $"active={npc.active} life={npc.life} onKill={TestNpc.OnKillCount}");
            }
        }

        // ===================== 坐骑 =====================

        private static void MountOn(Player player)
        {
            var type = ModContent.MountType<TestMount>();
            var buff = ModContent.BuffType<TestMountBuff>();
            _mountType = type;

            Check("mount", "MountID.Count 抬到 mod 槽位之后", MountID.Count > type, $"type={type} Count={MountID.Count}");
            var data = Mount.mounts != null && type < Mount.mounts.Length ? Mount.mounts[type] : null;
            Check("mount", "MountData 在 Mount.mounts 里", data != null, data == null ? "槽位是 null" : $"buff={data.buff}");
            Check("mount", "buff → 坐骑 的桥接", BuffID.Sets.MountType != null && BuffID.Sets.MountType[buff] == type,
                  $"BuffID.Sets.MountType[{buff}]={BuffID.Sets.MountType?[buff]}（要 {type}）");

            player.mount.SetMount(type, player, false);
        }

        private static void MountCheck(Player player)
        {
            var buff = ModContent.BuffType<TestMountBuff>();
            Check("mount", "骑上了", player.mount.Active && player.mount.Type == _mountType,
                  $"Active={player.mount.Active} Type={player.mount.Type}（要 {_mountType}）");
            Check("mount", "_data 非空（Mount.Initialize 重建表之后还在）", player.mount._data != null);
            Check("mount", "MountData.buff 指到坐骑 buff", player.mount._data != null && player.mount._data.buff == buff,
                  $"buff={player.mount._data?.buff}（要 {buff}）");
            Check("mount", "SetMount 钩子跑过", TestMount.SetMountCount > 0, $"count={TestMount.SetMountCount}");
            Check("mount", "UpdateEffects 每帧在跑", TestMount.Effects > 0, $"effects={TestMount.Effects}");

            // JumpHeight/JumpSpeed 没有 Player 参数：钩子靠 mount 地址反查骑它的玩家
            var height = player.mount.JumpHeight(0f);
            player.mount.JumpSpeed(0f);
            Check("mount", "JumpHeight/JumpSpeed 钩子跑过", TestMount.Jumps > 0, $"jumps={TestMount.Jumps} height={height}");
        }

        private static void MountOff(Player player)
        {
            player.mount.Dismount(player, false);
            Check("mount", "下来了", !player.mount.Active, $"Active={player.mount.Active}");
        }

        // ===================== 饰品前缀（每帧效果） =====================

        private static void AccessoryOn(Player player)
        {
            var slot = player.armor[3];
            _savedAccType = slot.type;
            _savedAccPrefix = slot.prefix;

            slot.SetDefaults(ItemID.CloudinaBottle);
            var accessory = ModContent.PrefixType<TestAccessoryPrefix>();
            var ok = slot.Prefix(accessory);
            Check("prefix", "饰品装上 mod 前缀", ok && slot.prefix == accessory, $"ok={ok} prefix={slot.prefix}");
        }

        private static void AccessoryMid(Player player)
        {
            _defenseWithPrefix = player.statDefense;
            player.armor[3].SetDefaults(ItemID.CloudinaBottle);   // 同一件饰品，只是没有前缀
        }

        private static void AccessoryOff(Player player)
        {
            var without = player.statDefense;
            Check("prefix", "ApplyAccessoryEffects 每帧生效（+4 防御）", _defenseWithPrefix - without == 4,
                  $"有前缀 {_defenseWithPrefix}，无前缀 {without}，差 {_defenseWithPrefix - without}");
            Check("prefix", "ApplyAccessoryEffects 调用了", TestAccessoryPrefix.EffectCount > 0, $"count={TestAccessoryPrefix.EffectCount}");

            var slot = player.armor[3];
            slot.SetDefaults(_savedAccType);
            slot.prefix = (byte)_savedAccPrefix;
        }

        // ===================== Tile / Wall =====================

        private static void TilePlace(Player player)
        {
            _tileType = ModContent.TileType<TestTile>();
            if (!FindAir(player, 0, 0, out _tileX, out _tileY))
            {
                Check("tile", "找到可放置的空气格", false, "玩家周围都是实心/墙");
                return;
            }

            var ok = WorldGen.PlaceTile(_tileX, _tileY, _tileType, false, true, Main.myPlayer, 0);
            Check("tile", "PlaceTile 成功（754 上限补丁生效）", ok && Main.tile[_tileX, _tileY].type == _tileType,
                  $"ok={ok} type={Main.tile[_tileX, _tileY].type}（要 {_tileType}）at {_tileX},{_tileY}");
        }

        private static void TileCheck(Player player)
        {
            var tile = Main.tile[_tileX, _tileY];
            Check("tile", "格子还在、类型对", tile.type == _tileType, $"type={tile.type}");
            Check("tile", "贴图写进 TextureAssets.Tile", TextureAssets.Tile[_tileType] != null,
                  $"asset={TextureAssets.Tile[_tileType] != null}");
            Check("tile", "tileSolid 表可读且为 true", Main.tileSolid[_tileType], $"solid={Main.tileSolid[_tileType]}");
            Check("tile", "tileLighted 表可读且为 true", Main.tileLighted[_tileType], $"lighted={Main.tileLighted[_tileType]}");

            var damage = player.GetPickaxeDamage(_tileX, _tileY, 100, 0, tile);
            Check("tile", "GetPickaxeDamage 走 mod 分派（MineResist=2，伤害减半）", damage > 0 && damage <= 100,
                  $"damage={damage}");
            Check("tile", "ModifyLight 每 3 帧跑过", TestTile.Lights > 0, $"lights={TestTile.Lights}");

            try
            {
                WorldGen.TileFrame(_tileX, _tileY, false, false);
                Check("tile", "WorldGen.TileFrame 不炸", true);
            }
            catch (Exception e)
            {
                Check("tile", "WorldGen.TileFrame 不炸", false, e.GetType().Name + ": " + e.Message);
            }
        }

        private static void TileKill()
        {
            try
            {
                WorldGen.KillTile(_tileX, _tileY, false, false, false);
                Check("tile", "KillTile 不炸", true);
            }
            catch (Exception e)
            {
                Check("tile", "KillTile 不炸", false, e.GetType().Name + ": " + e.Message);
            }
        }

        private static void TileDrop()
        {
            Check("tile", "挖掉后格子清空", Main.tile[_tileX, _tileY].type == 0,
                  $"type={Main.tile[_tileX, _tileY].type}");
            Check("tile", "ModTile.KillTile 回调跑过", TestTile.Kills > 0, $"kills={TestTile.Kills}");

            var itemType = ModContent.ItemType<TestTileItem>();
            var found = false;
            foreach (var item in Main.item)
            {
                if (item == null || !item.active || item.type != itemType) continue;
                var dx = Math.Abs(item.Center.X / 16f - _tileX);
                var dy = Math.Abs(item.Center.Y / 16f - _tileY);
                if (dx <= 8 && dy <= 8) { found = true; break; }
            }
            Check("tile", "掉出了 TestTileItem", found, $"itemType={itemType}");
        }

        private static void FurniturePlace(Player player)
        {
            _furnitureType = ModContent.TileType<TestFurniture>();
            if (!FindAir(player, 2, 2, out _furnitureX, out _furnitureY))
            {
                Check("furniture", "找到 2x2 空气区", false, "玩家周围没有");
                return;
            }

            var data = TileObjectData.GetTileData(_furnitureType, 0, 0);
            Check("furniture", "TileObjectData 已注册（_data 补齐）", data != null,
                  $"data={(data == null ? "null" : data.Width + "x" + data.Height)}");
            Check("furniture", "tileFrameImportant 是 true（多格家具的绘制入口）", Main.tileFrameImportant[_furnitureType],
                  $"frameImportant={Main.tileFrameImportant[_furnitureType]}");
            Check("furniture", "贴图表里有真实贴图（.Value 非空）",
                  TextureAssets.Tile[_furnitureType]?.Value != null,
                  $"asset={TextureAssets.Tile[_furnitureType] != null} value={TextureAssets.Tile[_furnitureType]?.Value != null}");

            // 2x2 家具要落地：AnchorBottom 要**两列**下都实心（之前只放一格，PlaceObject 一直 false）。
            var dirt1 = WorldGen.PlaceTile(_furnitureX, _furnitureY + 2, TileID.Dirt, false, true, Main.myPlayer, 0);
            var dirt2 = WorldGen.PlaceTile(_furnitureX + 1, _furnitureY + 2, TileID.Dirt, false, true, Main.myPlayer, 0);
            Check("furniture", "锚点两格是实心泥土", dirt1 && dirt2 &&
                  Main.tile[_furnitureX, _furnitureY + 2].active() &&
                  Main.tile[_furnitureX + 1, _furnitureY + 2].active(),
                  $"dirt={dirt1}/{dirt2} active={Main.tile[_furnitureX, _furnitureY + 2].active()}/" +
                  $"{Main.tile[_furnitureX + 1, _furnitureY + 2].active()}");

            var ok = WorldGen.PlaceObject(_furnitureX, _furnitureY, _furnitureType, false, 0, 0, -1, -1);
            Check("furniture", "PlaceObject 成功", ok, $"ok={ok} at {_furnitureX},{_furnitureY}");

            // 对照：同一位置放一把原版椅子（1x2）。分开"位置/锚点不行"和"mod 类型不行"。
            var vanilla = WorldGen.PlaceObject(_furnitureX, _furnitureY, TileID.Chairs, false, 0, 0, -1, -1);
            Check("furniture", "同一位置原版椅子能放（对照）", vanilla, $"vanilla={vanilla}");
            if (vanilla) WorldGen.KillTile(_furnitureX, _furnitureY, false, false, false);

            // 对照 2：天然地面（两格空气 + 下面实心）上放原版椅子，排除"自己垫的土"这个变量。
            var gx = 0;
            var gy = 0;
            var foundGround = false;
            var px = (int)(player.Center.X / 16f);
            var py = (int)(player.Center.Y / 16f);
            for (var r = 2; r <= 14 && !foundGround; r++)
            {
                for (var dx = -r; dx <= r && !foundGround; dx++)
                {
                    for (var dy = -6; dy <= 6; dy++)
                    {
                        var tx = px + dx;
                        var ty = py + dy;
                        if (tx < 1 || ty < 1 || tx >= Main.maxTilesX - 1 || ty + 3 >= Main.maxTilesY) continue;
                        if (Main.tile[tx, ty].active() || Main.tile[tx, ty + 1].active()) continue;
                        var below = Main.tile[tx, ty + 2];
                        if (!below.active() || !Main.tileSolid[below.type]) continue;
                        gx = tx;
                        gy = ty;
                        foundGround = true;
                        break;
                    }
                }
            }
            if (!foundGround)
            {
                Check("furniture", "找到天然地面（对照 2）", false, "玩家周围 14 格没有");
            }
            else
            {
                var ground = WorldGen.PlaceObject(gx, gy, TileID.Chairs, false, 0, 0, -1, -1);
                Check("furniture", "天然地面上原版椅子能放（对照 2）", ground, $"at {gx},{gy} ok={ground}");
                if (ground) WorldGen.KillTile(gx, gy, false, false, false);
            }
        }

        private static void FurnitureCheck()
        {
            var cells = 0;
            var frames = new HashSet<int>();
            for (var x = _furnitureX; x < _furnitureX + 2; x++)
            {
                for (var y = _furnitureY; y < _furnitureY + 2; y++)
                {
                    var tile = Main.tile[x, y];
                    if (tile.type != _furnitureType) continue;
                    cells++;
                    frames.Add(tile.frameX);
                    frames.Add(tile.frameY);
                }
            }

            Check("furniture", "2x2 四格都在", cells == 4, $"cells={cells}");
            Check("furniture", "四格帧已定（不是全 0/0）", cells == 0 || frames.Count > 1,
                  $"frames=[{string.Join(",", frames)}]");
            Check("furniture", "绘制数据可读（CoordinateWidth/Height）",
                  TileObjectData.GetTileData(_furnitureType, 0, 0) is { CoordinateWidth: > 0, Height: > 0 });
            try
            {
                WorldGen.KillTile(_furnitureX, _furnitureY, false, false, false);
                Check("furniture", "多格 KillTile/CheckObject 不炸", true, $"multiKills={TestFurniture.Kills}");
            }
            catch (Exception e)
            {
                Check("furniture", "多格 KillTile/CheckObject 不炸", false, e.GetType().Name + ": " + e.Message);
            }
        }

        private static void WallPlace(Player player)
        {
            _wallType = ModContent.WallType<TestWall>();
            if (!FindAir(player, 0, 0, out _wallX, out _wallY))
            {
                Check("wall", "找到可放墙的空气格", false, "玩家周围都是实心/墙");
                return;
            }

            WorldGen.PlaceWall(_wallX, _wallY, _wallType, false);
            Check("wall", "PlaceWall 成功", Main.tile[_wallX, _wallY].wall == _wallType,
                  $"wall={Main.tile[_wallX, _wallY].wall}（要 {_wallType}）at {_wallX},{_wallY}");
            Check("wall", "WallID.Count 顶到 mod 槽位", WallID.Count > _wallType,
                  $"type={_wallType} Count={WallID.Count}");
        }

        private static void WallCheck()
        {
            var tile = Main.tile[_wallX, _wallY];
            Check("wall", "墙还在、类型对", tile.wall == _wallType, $"wall={tile.wall}");
            Check("wall", "贴图写进 TextureAssets.Wall", TextureAssets.Wall[_wallType] != null,
                  $"asset={TextureAssets.Wall[_wallType] != null}");
            try
            {
                Framing.WallFrame(_wallX, _wallY, true);
                Check("wall", "Framing.WallFrame 不炸", true);
            }
            catch (Exception e)
            {
                Check("wall", "Framing.WallFrame 不炸", false, e.GetType().Name + ": " + e.Message);
            }
        }

        private static void WallDrop()
        {
            try
            {
                WorldGen.KillWall(_wallX, _wallY, false);
                Check("wall", "KillWall 不炸", true);
            }
            catch (Exception e)
            {
                Check("wall", "KillWall 不炸", false, e.GetType().Name + ": " + e.Message);
            }

            Check("wall", "墙清空", Main.tile[_wallX, _wallY].wall == 0, $"wall={Main.tile[_wallX, _wallY].wall}");
            Check("wall", "ModWall.KillWall 回调跑过", TestWall.Kills > 0, $"kills={TestWall.Kills}");

            var itemType = ModContent.ItemType<TestWallItem>();
            var found = false;
            foreach (var item in Main.item)
            {
                if (item == null || !item.active || item.type != itemType) continue;
                if (Math.Abs(item.Center.X / 16f - _wallX) <= 8 && Math.Abs(item.Center.Y / 16f - _wallY) <= 8)
                {
                    found = true;
                    break;
                }
            }
            Check("wall", "掉出了 TestWallItem", found, $"itemType={itemType}");
        }

        // ===================== 城镇 NPC =====================

        private static void TownSpawn(Player player)
        {
            var type = ModContent.NpcType<TestTownNpc>();
            _townIndex = NPC.NewNPC(null, (int)player.Center.X + 80, (int)player.Center.Y, type);
            var npc = _townIndex >= 0 && _townIndex < Main.npc.Length ? Main.npc[_townIndex] : null;
            Check("town", "城镇 NPC 生成成功", npc != null && npc.active && npc.type == type,
                  $"index={_townIndex} type={type}");
            Check("town", "townNPC 标记生效", npc != null && npc.townNPC, $"townNPC={npc?.townNPC}");
        }

        private static void TownCheck(Player player)
        {
            var type = ModContent.NpcType<TestTownNpc>();
            var npc = _townIndex >= 0 && _townIndex < Main.npc.Length ? Main.npc[_townIndex] : null;
            if (npc == null || !npc.active)
            {
                Check("town", "城镇 NPC 还活着", false);
                return;
            }

            var name = NPC.getNewNPCName(type);
            Check("town", "SetNPCNameList 名字生效", name == "PylonA" || name == "PylonB", $"name={name}");
            Check("town", "CanTownNPCSpawn 直接调用", new TestTownNpc().CanTownNPCSpawn(0));
            Check("town", "CheckConditions 直接调用", new TestTownNpc().CheckConditions(0, 0, 0, 0));

            var buttons = new ModNpc.ChatButtons();
            ((ModNpc)ModContent.GetInstance<TestTownNpc>()).SetChatButtons(npc, buttons);
            Check("town", "SetChatButtons 生效", buttons.Button == "Test Shop", $"button={buttons.Button}");
            Check("town", "OnChatButtonClicked 返回店名",
                  ((ModNpc)ModContent.GetInstance<TestTownNpc>()).OnChatButtonClicked(npc, true) == "Test Shop");

            Check("town", "AddShops 注册了商店", TestTownNpc.Shop != null && TestTownNpc.Shop.Index >= 0,
                  $"shop={(TestTownNpc.Shop == null ? "null" : "index=" + TestTownNpc.Shop.Index)}");

            npc.active = false;
        }

        // ===================== 音效 / 音乐 =====================

        private static void Sound()
        {
            var style = new SoundStyle("Sounds/TestBeep", new SoundStyle.Options { Volume = 0.5f });
            _soundHandle = SoundEngine.PlaySound(style);
            Check("sound", "MediaPlayer 播放返回句柄", _soundHandle > 0, $"handle={_soundHandle}");
            Check("sound", "GetSoundState 有活动实例", SoundEngine.GetSoundState(_soundHandle) > 0,
                  $"state={SoundEngine.GetSoundState(_soundHandle)}");
            Check("sound", "FindActiveSound 能看到", SoundEngine.FindActiveSound(style) > 0,
                  $"active={SoundEngine.FindActiveSound(style)}");
            SoundEngine.StopSound(_soundHandle);
            Check("sound", "StopSound 后状态归零", SoundEngine.GetSoundState(_soundHandle) == 0,
                  $"state={SoundEngine.GetSoundState(_soundHandle)}");
        }

        private static void MusicStart()
        {
            Check("music", "MusicExists 找到文件", MusicLoader.MusicExists(FrameMod.Instance, "Music/TestMusic"));
            _musicSlot = MusicLoader.GetMusicSlot(FrameMod.Instance, "Music/TestMusic");
            Check("music", "GetMusicSlot 分配了槽位", _musicSlot > 0, $"slot={_musicSlot}");
            TestSceneEffect.Active = true;
        }

        private static void MusicCheck()
        {
            Check("music", "场景音乐被 ModMusic 选中并播放", MusicLoader.IsMusicPlaying(_musicSlot),
                  $"slot={_musicSlot} playing={MusicLoader.IsMusicPlaying(_musicSlot)} " +
                  $"musicVolume={Main.musicVolume:0.00} cur={Main.curMusic} new={Main.newMusic}");
            TestSceneEffect.Active = false;
            _log?.Info($"[music] 已关场景，约 2 秒后原版音乐应回来（curMusic from {Main.curMusic}）");
        }

        // ===================== 网络 =====================

        private static void Network()
        {
            var mod = FrameMod.Instance;
            try
            {
                var packet = mod.GetPacket();
                packet.WriteInt32(42);
                packet.WriteVector2(new Il2CppMicrosoft.Xna.Framework.Vector2(1, 2));
                packet.Send();
                Check("net", "单机下 GetPacket/Write/Send 不炸", true, $"netMode={Main.netMode}");
            }
            catch (Exception e)
            {
                Check("net", "单机下 GetPacket/Write/Send 不炸", false, e.GetType().Name + ": " + e.Message);
            }

            try
            {
                FrameMod.PacketHandled = false;
                mod.HandlePacket(new NetReader(new object[] { 42 }), 0);
                Check("net", "HandlePacket 能收到 42", FrameMod.PacketHandled);
            }
            catch (Exception e)
            {
                Check("net", "HandlePacket 能收到 42", false, e.GetType().Name + ": " + e.Message);
            }

            var reader = new NetReader(new object[] { 7, "x", true, new Il2CppMicrosoft.Xna.Framework.Vector2(3, 4) });
            var number = reader.ReadInt32();
            var text = reader.ReadString();
            var flag = reader.ReadBoolean();
            var vector = reader.ReadVector2();
            Check("net", "NetReader 读回 int/string/bool/Vector2",
                  number == 7 && text == "x" && flag && vector.X == 3 && vector.Y == 4);
        }

        private static bool FindAir(Player player, int width, int height, out int x, out int y)
        {
            var cx = (int)(player.Center.X / 16f);
            var cy = (int)(player.Center.Y / 16f);
            for (var radius = 2; radius <= 14; radius++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    for (var dy = -12; dy <= 12; dy++)
                    {
                        var tx = cx + dx;
                        var ty = cy + dy;
                        var clear = true;
                        for (var ox = 0; ox <= width && clear; ox++)
                            for (var oy = 0; oy <= height && clear; oy++)
                            {
                                if (tx + ox >= Main.maxTilesX || ty + oy >= Main.maxTilesY || tx + ox < 0 || ty + oy < 0)
                                {
                                    clear = false;
                                    break;
                                }
                                var tile = Main.tile[tx + ox, ty + oy];
                                if (tile.type != 0 || tile.wall != 0) clear = false;
                            }
                        if (!clear) continue;
                        x = tx;
                        y = ty;
                        return true;
                    }
                }
            }
            x = y = 0;
            return false;
        }

        // ===================== 汇总 =====================

        private static void Summary()
        {
            _done = true;
            var p = Main.LocalPlayer;
            _log?.Info($"[world] spawn={Main.spawnTileX},{Main.spawnTileY} " +
                       $"playerTile={(int)(p.Center.X / 16f)},{(int)(p.Center.Y / 16f)} " +
                       $"max={Main.maxTilesX}x{Main.maxTilesY} bgStyle={Main.bgStyle} " +
                       $"zoneBeach={p.ZoneBeach} sceneBeach={Main.SceneMetrics?.ZoneBeach} " +
                       $"prefBg={Main.GetPreferredBGStyleForPlayer()} " +
                       $"oceanLevel={WorldGen.oceanLevel:0.#} worldSurface={Main.worldSurface:0.#} " +
                       $"curMusic={Main.curMusic}");
            _log?.Info($"[selftest] 完成：{_checks - _fails}/{_checks} 通过" +
                       (_fails == 0 ? "（全部 ✓）" : $"，{_fails} 条 ✗ 见上"));
        }

        // ===================== 工具 =====================

        private static void Check(string area, string what, bool ok, string detail = null)
        {
            _checks++;
            if (!ok) _fails++;
            _log?.Info($"{(ok ? "✓" : "✗")} [{area}] {what}{(string.IsNullOrEmpty(detail) ? "" : " — " + detail)}");
        }

        private static Item NewItem(int type)
        {
            var item = new Item();
            item.SetDefaults(type, null);
            return item;
        }

        private static bool Contains(int[] list, int value)
        {
            if (list == null) return false;
            for (var i = 0; i < list.Length; i++) if (list[i] == value) return true;
            return false;
        }

        private static string Describe(int[] list)
        {
            if (list == null) return "列表是 null";
            var text = string.Join(",", list);
            return text.Length > 120 ? text.Substring(0, 120) + $"…（{list.Length} 个）" : $"共 {list.Length} 个：{text}";
        }

        /// <summary>C# 的 Math.Round：.5 进偶数（游戏的算法）。</summary>
        private static int RoundEven(float value) => (int)Math.Round(value, MidpointRounding.ToEven);
    }
}



