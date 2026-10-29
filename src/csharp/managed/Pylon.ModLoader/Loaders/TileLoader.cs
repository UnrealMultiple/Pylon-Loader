using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.Drawing;
using Terraria.ID;
using Terraria.ObjectData;

namespace Pylon.ModLoader
{
    /// <summary>自定义方块的运行期管线（占号/贴图/表扩容在 ContentInstaller）：注册期装 hook，表定型后扩表、跑 SetStaticDefaults、顶开编译期上限。</summary>
    internal static class TileLoader
    {
        private const int VanillaCount = 754;

        private static ModLogger _log;
        private static bool _installed;
        private static bool _setupDone;
        private static int _total;

        private static readonly Dictionary<Type, OverridePlan> Plans = new();
        private static readonly Dictionary<int, int> AnimationHeight = new();
        private static readonly HashSet<int> UncachedDraw = new();
        private static readonly HashSet<int> AdjTiles = new();
        private static readonly Dictionary<string, int> ItemDrops = new();   // "type:style" / "type" → item

        private static readonly List<(int X, int Y, float R, float G, float B)> Lights = new();
        private static int _lightAge = 3;
        private static int _dustLeft = -1;
        private static readonly Dictionary<int, uint> LastPress = new();
        private static bool[] _mergeRows;
        private static bool[] _mergeCols;

        internal static bool Any => ModContent.Count(ContentKind.Tile) > 0;

        internal static ModTile At(int i, int j)
        {
            if (!Any) return null;
            try { return ModContent.Find(ContentKind.Tile, Main.tile[i, j].type) as ModTile; }
            catch { return null; }
        }

        private static bool Over(ModTile tile, string method) => PlanOf(tile).Has(method);

        private static OverridePlan PlanOf(ModTile tile)
        {
            var type = tile.GetType();
            if (Plans.TryGetValue(type, out var plan)) return plan;
            plan = OverridePlan.Build(type, typeof(ModTile));
            Plans[type] = plan;
            return plan;
        }

        // ===================== 注册期：装 hook =====================

        internal static void Install(ModLogger log)
        {
            if (_installed || !Any) return;
            _installed = true;
            _log = log;

            On.Terraria.Player.GetPickaxeDamage += PickaxePostfix;
            On.Terraria.WorldGen.TileFrameImportant += ImportantPrefix;
            On.Terraria.WorldGen.TileFrame += WorldTileFramePrefix;
            On.Terraria.WorldGen.CanKillTile += CanKillPrefix;
            On.Terraria.WorldGen.KillTile += KillTilePrefix;
            On.Terraria.WorldGen.KillTile += KillTilePostfix;
            On.Terraria.WorldGen.KillTile_GetItemDrops += ItemDropsPostfix;
            On.Terraria.WorldGen.KillTile_MakeTileDust += MakeDustPrefix;
            On.Terraria.WorldGen.KillTile_PlaySounds += PlaySoundsPrefix;
            On.Terraria.WorldGen.PlaceTile += PlaceTilePrefix;
            On.Terraria.WorldGen.PlaceTile += PlaceTilePostfix;
            On.Terraria.ObjectData.TileObjectData.CallPostPlacementPlayerHook += PostPlacementPostfix;
            On.Terraria.WorldGen.UpdateWorld_OvergroundTile += RandomUpdateOverground;
            On.Terraria.WorldGen.UpdateWorld_UndergroundTile += RandomUpdateUnderground;
            On.Terraria.Player.AdjTiles += AdjTilesPostfix;
            On.Terraria.Wiring.HitWireSingle += HitWirePostfix;
            On.Terraria.WorldGen.SlopeTile += SlopePrefix;
            On.Terraria.WorldGen.PoundTile += PoundPrefix;
            On.Terraria.Main.AnimateTiles += AnimateTilesPostfix;
            On.Terraria.Player.TileInteractionsUse += TileUsePrefix;
            On.Terraria.Player.TileInteractionsMouseOver += MouseOverPostfix;
            On.Terraria.Player.TileInteractionsCheckLongDistance += MouseOverFarPostfix;
            On.Terraria.TileData.Allocate += AllocatePostfix;

            // 光照只在有 ModifyLight 的 tile 时才有意义；面积前缀 + 世界更新后统一 AddLight。
            if (HasAny("ModifyLight"))
                On.Terraria.Lighting.LightTiles += LightAreaPrefix;
            if (HasAny("AnimateTile"))
                AnimationHook = true;
            if (HasAny("NearbyEffects"))
                NearbyHook = true;

            // 这里**不挂 SceneMetrics.Scan**：参数 `SceneMetricsScanSettings` 是 ValueType，游戏按指针收，
            // 托管按值声明会让 Harmony 再解一层，真机出现"扫描输入被弄脏"（出生点 bgStyle=4/ZoneBeach 错乱）。
            // NearbyEffects 不依赖 Scan，改成世界更新后按玩家周围自己扫（NearbyTick）。
            if (HasAny("ModifyLight") || NearbyHook)
                On.Terraria.Main.DoUpdateInWorld += UpdateWorldPostfix;

            // 每格绘制都要经过它：只有 Type >= 754 才做活。家具的宽/高/偏移与动画帧都靠这里。
            On.Terraria.GameContent.Drawing.TileDrawing.GetTileDrawData += DrawDataPostfix;
        }

        private static bool AnimationHook;
        private static bool NearbyHook;

        private static bool HasAny(string method)
        {
            foreach (var tile in ModContent.GetContent<ModTile>())
                if (Over(tile, method)) return true;
            return false;
        }

        // ===================== 表定型后 =====================

        internal static void Setup(ModLogger log)
        {
            if (_setupDone || !Any) return;
            _setupDone = true;
            _log = log;
            _total = ModContent.NextType(ContentKind.Tile);

            FixTileMerge(_total);
            HookSetupTileMerge();
            FixGlowMask(_total);
            FixTileLists(_total);
            PadObjectData(_total);
            GrowInstances(_total);
            LimitPatch.Apply(_total, log);

            var tiles = ModContent.GetContent<ModTile>();
            foreach (var global in GlobalTileRegistry.All)
                Safe.Run(global.Name + ".SetStaticDefaults", global.SetStaticDefaults);

            var wasLocked = TileObjectData.readOnlyData;
            try
            {
                TileObjectData.readOnlyData = false;
                foreach (var tile in tiles)
                {
                    try
                    {
                        tile.SetStaticDefaults();
                        if (Main.tileLavaDeath[tile.Type]) Main.tileObsidianKill[tile.Type] = true;
                        if (Main.tileSolid[tile.Type]) Main.tileNoSunLight[tile.Type] = true;
                        tile.PostSetDefaults();
                    }
                    catch (Exception e)
                    {
                        log?.Warn($"{tile.Name} 的 SetStaticDefaults 失败：{e.Message}");
                    }
                }
            }
            finally
            {
                TileObjectData.readOnlyData = wasLocked;
            }

            foreach (var tile in tiles)
            {
                try { ApplyLateStatic(tile); }
                catch (Exception e) { log?.Warn($"{tile.Name} 的静态收尾失败：{e.Message}"); }
            }

            foreach (var global in GlobalTileRegistry.All)
                Safe.Run(global.Name + ".PostSetupTileMerge", global.PostSetupTileMerge);

            log?.Info($"[tile] 已安装 {tiles.Count} 个方块（id {VanillaCount}..{_total - 1}）；" +
                      $"动画 {AnimationHeight.Count}、带形状 {ObjectDataCount()}");
        }

        private static int ObjectDataCount()
        {
            var count = 0;
            foreach (var tile in ModContent.GetContent<ModTile>())
                if (TileObjectData.GetTileData(tile.Type, 0, 0) != null) count++;
            return count;
        }

        private static void ApplyLateStatic(ModTile tile)
        {
            var type = tile.Type;
            if (tile.AnimationFrameHeight > 0) AnimationHeight[type] = tile.AnimationFrameHeight;
            if (!tile.CacheDrawData) UncachedDraw.Add(type);
            if (tile.AdjTiles != null && tile.AdjTiles.Length > 0) AdjTiles.Add(type);

            AddRoomNeed("Chair", type, TileID.Sets.RoomNeeds.CountsAsChair, v => TileID.Sets.RoomNeeds.CountsAsChairTypes = v,
                () => TileID.Sets.RoomNeeds.CountsAsChairTypes);
            AddRoomNeed("Table", type, TileID.Sets.RoomNeeds.CountsAsTable, v => TileID.Sets.RoomNeeds.CountsAsTableTypes = v,
                () => TileID.Sets.RoomNeeds.CountsAsTableTypes);
            AddRoomNeed("Torch", type, TileID.Sets.RoomNeeds.CountsAsTorch, v => TileID.Sets.RoomNeeds.CountsAsTorchTypes = v,
                () => TileID.Sets.RoomNeeds.CountsAsTorchTypes);
            AddRoomNeed("Door", type, TileID.Sets.RoomNeeds.CountsAsDoor, v => TileID.Sets.RoomNeeds.CountsAsDoorTypes = v,
                () => TileID.Sets.RoomNeeds.CountsAsDoorTypes);

            if (TileID.Sets.HasOutlines[type])
            {
                if (ModContent.HasAsset(tile.Mod, tile.HighlightTexture))
                {
                    var asset = ModContent.Request(tile.Mod, tile.HighlightTexture);
                    if (type < TextureAssets.HighlightMask.Length) TextureAssets.HighlightMask[type] = asset;
                    else TileID.Sets.HasOutlines[type] = false;
                }
                else TileID.Sets.HasOutlines[type] = false;
            }

            AddCraftingStation(type);
            BlankMapEntry(type);
        }

        private static void AddRoomNeed(string need, int type, Il2CppStructArray<bool> flags,
                                        Action<Il2CppStructArray<int>> set, Func<Il2CppStructArray<int>> get)
        {
            if (flags == null || type >= flags.Length || !flags[type]) return;
            var list = get();
            if (list != null)
                foreach (var value in list)
                    if (value == type) return;

            var old = list?.Length ?? 0;
            var grown = new int[old + 1];
            if (list != null) Array.Copy(list, grown, old);
            grown[old] = type;
            set(grown);
            _log?.Info($"[tile] {type} 计入房间需求 {need}");
        }

        private static void AddCraftingStation(int type)
        {
            var item = GetItemDrop(type, 0);
            if (item <= 0) return;
            var table = TileID.Sets.CraftingStationItemId;
            if (table.Length < _total)
            {
                var grown = new int[_total];
                Array.Copy(table, grown, table.Length);
                table = grown;
                TileID.Sets.CraftingStationItemId = grown;
            }
            if (table[type] == 0) table[type] = item;
        }

        /// <summary>地图项暂不接，先写 0 让地图跳过而不是读越界。</summary>
        private static void BlankMapEntry(int type)
        {
            var lookup = Terraria.Map.MapHelper.tileLookup;
            var counts = Terraria.Map.MapHelper.tileOptionCounts;
            if (lookup != null && type < lookup.Length) lookup[type] = 0;
            if (counts != null && type < counts.Length) counts[type] = 0;
        }

        // ===================== 挖 =====================

        private static int PickaxePostfix(On.Terraria.Player.orig_GetPickaxeDamage orig, Player self,
                                          int x, int y, int pickPower, int hitBufferIndex, Tile tileTarget)
        {
            var result = orig(self, x, y, pickPower, hitBufferIndex, tileTarget);
            var tile = At(x, y);
            if (tile == null) return result;
            if (pickPower < tile.MinPick) return 0;
            if (tile.MineResist > 0) result /= tile.MineResist;
            return result;
        }

        /// <summary>CanKillTile 在代理签名里没带 blockDamaged（手机版是 SpecialKillTileContext），mod 的 ref 只走本地副本。</summary>
        private static bool CanKillPrefix(On.Terraria.WorldGen.orig_CanKillTile orig, int i, int j,
                                          WorldGen.SpecialKillTileContext context)
        {
            var tile = At(i, j);
            if (tile == null) return orig(i, j, context);

            var blockDamaged = false;
            var can = true;
            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.CanKillTile)))
            {
                try { can &= global.CanKillTile(i, j, tile.Type, ref blockDamaged); }
                catch (Exception e) { Safe.Report(global.Name + ".CanKillTile", e); }
            }
            if (can)
            {
                try { can = tile.CanKillTile(i, j, ref blockDamaged); }
                catch (Exception e) { Safe.Report(tile.Name + ".CanKillTile", e); can = true; }
            }
            if (can) return orig(i, j, context);
            return false;
        }

        private static void KillTilePrefix(On.Terraria.WorldGen.orig_KillTile orig, int i, int j,
                                           bool fail, bool effectOnly, bool noItem)
        {
            var tile = At(i, j);
            if (tile == null)
            {
                orig(i, j, fail, effectOnly, noItem);
                return;
            }

            // 代理签名不带 ref：mod 的 KillTile 改动只留在本地副本里。
            var failRef = fail;
            var effectRef = effectOnly;
            var noItemRef = noItem;
            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.KillTile)))
            {
                try { global.KillTile(i, j, tile.Type, ref failRef, ref effectRef, ref noItemRef); }
                catch (Exception e) { Safe.Report(global.Name + ".KillTile", e); }
            }
            try { tile.KillTile(i, j, ref failRef, ref effectRef, ref noItemRef); }
            catch (Exception e) { Safe.Report(tile.Name + ".KillTile", e); }

            var count = failRef ? 3 : 10;
            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.NumDust)))
            {
                try { global.NumDust(i, j, tile.Type, failRef, ref count); }
                catch (Exception e) { Safe.Report(global.Name + ".NumDust", e); }
            }
            try { tile.NumDust(i, j, failRef, ref count); }
            catch (Exception e) { Safe.Report(tile.Name + ".NumDust", e); }
            _dustLeft = Math.Max(0, count);
            orig(i, j, fail, effectOnly, noItem);
        }

        private static void KillTilePostfix(On.Terraria.WorldGen.orig_KillTile orig, int i, int j,
                                            bool fail, bool effectOnly, bool noItem)
        {
            orig(i, j, fail, effectOnly, noItem);
            _dustLeft = -1;
        }

        private static void ItemDropsPostfix(On.Terraria.WorldGen.orig_KillTile_GetItemDrops orig,
                                             int x, int y, Tile tileCache, ref int dropItem, ref int dropItemStack,
                                             ref int secondaryItem, ref int secondaryItemStack, ref bool noPrefix,
                                             bool includeLargeObjectDrops)
        {
            orig(x, y, tileCache, ref dropItem, ref dropItemStack,
                 ref secondaryItem, ref secondaryItemStack, ref noPrefix, includeLargeObjectDrops);
            var tile = At(x, y);
            if (tile == null) return;

            if (!includeLargeObjectDrops && IsMultiTile(tile.Type))
            {
                dropItem = 0;
                secondaryItem = 0;
                return;
            }

            var can = true;
            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.CanDrop)))
            {
                try { can &= global.CanDrop(x, y, tile.Type); }
                catch (Exception e) { Safe.Report(global.Name + ".CanDrop", e); }
            }
            if (!can)
            {
                dropItem = 0;
                secondaryItem = 0;
                return;
            }

            int[] list = null;
            if (Over(tile, "GetItemDrops"))
            {
                try { list = tile.GetItemDrops(x, y); }
                catch (Exception e) { Safe.Report(tile.Name + ".GetItemDrops", e); }
            }
            if (list != null && list.Length > 0)
            {
                dropItem = list[0];
                dropItemStack = 1;
                secondaryItem = list.Length > 1 ? list[1] : 0;
                secondaryItemStack = list.Length > 1 ? 1 : 0;
            }
            else
            {
                var style = HasObjectData(tile.Type) ? StyleAt(x, y) : 0;
                var item = GetItemDrop(tile.Type, style);
                if (item > 0)
                {
                    dropItem = item;
                    dropItemStack = 1;
                }
            }

            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.Drop)))
            {
                try { global.Drop(x, y, tile.Type); }
                catch (Exception e) { Safe.Report(global.Name + ".Drop", e); }
            }
        }

        private static int MakeDustPrefix(On.Terraria.WorldGen.orig_KillTile_MakeTileDust orig, int i, int j, Tile tileCache)
        {
            var tile = At(i, j);
            if (tile == null) return orig(i, j, tileCache);

            if (_dustLeft == 0) return 6000;
            if (_dustLeft > 0) _dustLeft--;

            var dustType = tile.DustType;
            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.CreateDust)))
            {
                try
                {
                    if (!global.CreateDust(i, j, tile.Type, ref dustType)) return 6000;
                }
                catch (Exception e) { Safe.Report(global.Name + ".CreateDust", e); }
            }
            try
            {
                if (!tile.CreateDust(i, j, ref dustType)) return 6000;
            }
            catch (Exception e) { Safe.Report(tile.Name + ".CreateDust", e); }
            if (dustType < 0) return 6000;

            return Dust.NewDust(new Vector2(i * 16, j * 16), 16, 16, dustType, 0, 0, 0, Color.White, 1f);
        }

        private static void PlaySoundsPrefix(On.Terraria.WorldGen.orig_KillTile_PlaySounds orig, int i, int j, bool fail, Tile tileCache)
        {
            var tile = At(i, j);
            if (tile == null)
            {
                orig(i, j, fail, tileCache);
                return;
            }

            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.KillSound)))
            {
                try
                {
                    if (!global.KillSound(i, j, tile.Type, fail))
                    {
                        orig(i, j, fail, tileCache);
                        return;
                    }
                }
                catch (Exception e) { Safe.Report(global.Name + ".KillSound", e); }
            }
            try
            {
                if (!tile.KillSound(i, j, fail))
                {
                    orig(i, j, fail, tileCache);
                    return;
                }
            }
            catch (Exception e) { Safe.Report(tile.Name + ".KillSound", e); }

            if (tile.HitSound < 0)
            {
                orig(i, j, fail, tileCache);
                return;
            }
            Terraria.Audio.SoundEngine.PlaySound(tile.HitSound, i * 16, j * 16, 1, 1f, 0f);
        }

        // ===================== 放 =====================

        private static bool PlaceTilePrefix(On.Terraria.WorldGen.orig_PlaceTile orig, int i, int j, int Type,
                                            bool mute, bool forced, int plr, int style)
        {
            if (Type < VanillaCount) return orig(i, j, Type, mute, forced, plr, style);
            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.CanPlace)))
            {
                try
                {
                    if (!global.CanPlace(i, j, Type)) return false;
                }
                catch (Exception e) { Safe.Report(global.Name + ".CanPlace", e); }
            }
            return orig(i, j, Type, mute, forced, plr, style);
        }

        private static bool PlaceTilePostfix(On.Terraria.WorldGen.orig_PlaceTile orig, int i, int j, int Type,
                                             bool mute, bool forced, int plr, int style)
        {
            var result = orig(i, j, Type, mute, forced, plr, style);
            if (!result || Type < VanillaCount || HasObjectData(Type)) return result;
            Placed(i, j, Type);
            return result;
        }

        private static bool PostPlacementPostfix(On.Terraria.ObjectData.TileObjectData.orig_CallPostPlacementPlayerHook orig,
                                                 int tileX, int tileY, int type, int style, int dir, int alternate, TileObject data)
        {
            var result = orig(tileX, tileY, type, style, dir, alternate, data);
            if (!result) return result;
            Placed(tileX, tileY, type);
            return result;
        }

        private static void Placed(int i, int j, int type)
        {
            var tile = ModContent.Find(ContentKind.Tile, type) as ModTile;
            if (tile == null) return;
            var player = Main.player[Main.myPlayer];
            var item = player?.HeldItem;
            try { tile.PlaceInWorld(i, j, item); }
            catch (Exception e) { Safe.Report(tile.Name + ".PlaceInWorld", e); }
            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.PlaceInWorld)))
            {
                try { global.PlaceInWorld(i, j, type, item); }
                catch (Exception e) { Safe.Report(global.Name + ".PlaceInWorld", e); }
            }
        }

        // ===================== 世界 =====================

        private static void RandomUpdateOverground(On.Terraria.WorldGen.orig_UpdateWorld_OvergroundTile orig,
                                                   int i, int j, bool checkNPCSpawns, int wallDist)
        {
            orig(i, j, checkNPCSpawns, wallDist);
            RandomUpdateInner(i, j, wallDist);
        }

        private static void RandomUpdateUnderground(On.Terraria.WorldGen.orig_UpdateWorld_UndergroundTile orig,
                                                    int i, int j, bool checkNPCSpawns, int wallDist)
        {
            orig(i, j, checkNPCSpawns, wallDist);
            RandomUpdateInner(i, j, wallDist);
        }

        private static void RandomUpdateInner(int i, int j, int wallDist)
        {
            var tile = At(i, j);
            if (tile == null) return;
            try { tile.RandomUpdate(i, j); }
            catch (Exception e) { Safe.Report(tile.Name + ".RandomUpdate", e); }
            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.RandomUpdate)))
            {
                try { global.RandomUpdate(i, j, tile.Type, wallDist > 0); }
                catch (Exception e) { Safe.Report(global.Name + ".RandomUpdate", e); }
            }
        }

        private static void AdjTilesPostfix(On.Terraria.Player.orig_AdjTiles orig, Player self)
        {
            orig(self);
            if (AdjTiles.Count == 0) return;
            var center = self.Center;
            var x = (int)(center.X / 16f);
            var y = (int)((self.position.Y + self.height) / 16f);
            var adj = self.adjTile;
            for (var i = x - 4; i <= x + 4; i++)
            {
                for (var j = y - 3; j <= y + 2; j++)
                {
                    var tile = At(i, j);
                    if (tile == null) continue;
                    if (tile.AdjTiles != null)
                        foreach (var type in tile.AdjTiles)
                            if (type >= 0 && type < adj.Length) adj[type] = true;
                    foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.AdjTiles)))
                    {
                        try
                        {
                            var extra = global.AdjTiles(tile.Type);
                            if (extra == null) continue;
                            foreach (var type in extra)
                                if (type >= 0 && type < adj.Length) adj[type] = true;
                        }
                        catch (Exception e) { Safe.Report(global.Name + ".AdjTiles", e); }
                    }
                }
            }
        }

        private static void HitWirePostfix(On.Terraria.Wiring.orig_HitWireSingle orig, int i, int j)
        {
            orig(i, j);
            var tile = At(i, j);
            if (tile == null) return;
            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.PreHitWire)))
            {
                try
                {
                    if (!global.PreHitWire(i, j, tile.Type)) return;
                }
                catch (Exception e) { Safe.Report(global.Name + ".PreHitWire", e); }
            }
            try { tile.HitWire(i, j); }
            catch (Exception e) { Safe.Report(tile.Name + ".HitWire", e); }
            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.HitWire)))
            {
                try { global.HitWire(i, j, tile.Type); }
                catch (Exception e) { Safe.Report(global.Name + ".HitWire", e); }
            }
        }

        private static bool SlopePrefix(On.Terraria.WorldGen.orig_SlopeTile orig, int i, int j,
                                        int slope, bool noEffects, bool quiet)
        {
            var tile = At(i, j);
            if (tile == null) return orig(i, j, slope, noEffects, quiet);
            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.Slope)))
            {
                try
                {
                    if (!global.Slope(i, j, tile.Type)) return false;
                }
                catch (Exception e) { Safe.Report(global.Name + ".Slope", e); }
            }
            try
            {
                if (!tile.Slope(i, j)) return false;
            }
            catch (Exception e) { Safe.Report(tile.Name + ".Slope", e); }
            return orig(i, j, slope, noEffects, quiet);
        }

        private static bool PoundPrefix(On.Terraria.WorldGen.orig_PoundTile orig, int i, int j)
        {
            var tile = At(i, j);
            if (tile == null) return orig(i, j);
            try
            {
                if (!tile.Slope(i, j)) return false;
            }
            catch (Exception e) { Safe.Report(tile.Name + ".Slope", e); }
            return orig(i, j);
        }

        // ===================== 帧 =====================

        private static void ImportantPrefix(On.Terraria.WorldGen.orig_TileFrameImportant orig, int i, int j,
                                            int type, Tile tileCache, bool resetFrame)
        {
            if (type < VanillaCount)
            {
                orig(i, j, type, tileCache, resetFrame);
                return;
            }
            var tile = ModContent.Find(ContentKind.Tile, type) as ModTile;
            if (tile == null || !HasObjectData(type) || TileID.Sets.Torches[type] || TileID.Sets.Platforms[type])
            {
                orig(i, j, type, tileCache, resetFrame);
                return;
            }
            // mod 方块自己接管成形（CheckObject），跳过原版 TileFrameImportant
            Safe.Run("tile.CheckObject", () => CheckObject(i, j, type));
        }

        private static void WorldTileFramePrefix(On.Terraria.WorldGen.orig_TileFrame orig, int i, int j,
                                                 bool resetFrame, bool noBreak)
        {
            var tile = At(i, j);
            if (tile == null || !Over(tile, "TileFrame"))
            {
                orig(i, j, resetFrame, noBreak);
                return;
            }
            // 代理签名里 resetFrame/noBreak 不带回传（只作输入），mod 的改动用本地副本。
            var reset = resetFrame;
            var noBreakRef = noBreak;
            try
            {
                if (tile.TileFrame(i, j, ref reset, ref noBreakRef)) orig(i, j, resetFrame, noBreak);
            }
            catch (Exception e)
            {
                Safe.Report(tile.Name + ".TileFrame", e);
                orig(i, j, resetFrame, noBreak);
            }
        }

        private static void CheckObject(int i, int j, int type)
        {
            if (WorldGen.destroyObject) return;

            var data = TileObjectData.GetTileData(type, 0, 0);
            if (data == null) return;

            var cache = Main.tile[i, j];
            var frameX = cache.frameX;
            var frameY = cache.frameY;
            var style = StyleOf(data, frameX, frameY);

            data = TileObjectData.GetTileData(cache);
            if (data == null) return;

            var pad = data.CoordinatePadding;
            var partX = (frameX % data.CoordinateFullWidth) / (data.CoordinateWidth + pad);
            var heights = data.CoordinateHeights;
            var partY = 0;
            for (var rest = frameY % data.CoordinateFullHeight;
                 partY + 1 < data.Height && rest - heights[partY] - pad >= 0;
                 partY++) rest -= heights[partY] + pad;

            var left = i - partX;
            var top = j - partY;
            if (IsWhole(left, top, data.Width, data.Height, type) &&
                Stays(left + data.Origin.X, top + data.Origin.Y, type, style))
            {
                return;
            }

            WorldGen.destroyObject = true;
            try
            {
                WorldGen.KillTile_DropItems(i, j, cache, true);
                for (var x = left; x < left + data.Width; x++)
                {
                    for (var y = top; y < top + data.Height; y++)
                    {
                        if (Main.tile[x, y].type != type) continue;
                        WorldGen.KillTile(x, y, false, false, false);
                    }
                }

                var tile = ModContent.Find(ContentKind.Tile, type) as ModTile;
                try { tile?.KillMultiTile(left, top, frameX - frameX % data.CoordinateFullWidth, frameY - frameY % data.CoordinateFullHeight); }
                catch (Exception e) { Safe.Report((tile?.Name ?? type.ToString()) + ".KillMultiTile", e); }
            }
            finally
            {
                WorldGen.destroyObject = false;
            }

            for (var x = left - 1; x < left + data.Width + 2; x++)
                for (var y = top - 1; y < top + data.Height + 2; y++)
                    WorldGen.TileFrame(x, y, false, false);
        }

        private static bool IsWhole(int left, int top, int width, int height, int type)
        {
            for (var x = left; x < left + width; x++)
                for (var y = top; y < top + height; y++)
                    if (Main.tile[x, y].type != type) return false;
            return true;
        }

        private static bool Stays(int x, int y, int type, int style)
        {
            var was = TileID.Sets.BreakableWhenPlacing[type];
            TileID.Sets.BreakableWhenPlacing[type] = true;
            try
            {
                return TileObject.CanPlace(x, y, type, style, 0, out _, false, default);
            }
            finally
            {
                TileID.Sets.BreakableWhenPlacing[type] = was;
            }
        }

        private static int StyleAt(int i, int j)
        {
            var tile = Main.tile[i, j];
            var data = TileObjectData.GetTileData(tile.type, 0, 0);
            return data == null ? 0 : Math.Max(0, StyleOf(data, tile.frameX, tile.frameY));
        }

        private static int StyleOf(TileObjectData data, int frameX, int frameY)
        {
            var subX = frameX / data.CoordinateFullWidth;
            var subY = frameY / data.CoordinateFullHeight;
            var wrap = data.StyleWrapLimit > 0 ? data.StyleWrapLimit : 1;
            var skip = data.StyleLineSkip > 0 ? data.StyleLineSkip : 1;
            var subTile = data.StyleHorizontal ? subY / skip * wrap + subX : subX / skip * wrap + subY;
            return subTile / Math.Max(1, data.StyleMultiplier);
        }

        internal static bool HasObjectData(int type) => TileObjectData.GetTileData(type, 0, 0) != null;
        internal static bool IsMultiTile(int type)
        {
            var data = TileObjectData.GetTileData(type, 0, 0);
            return data != null && (data.Width != 1 || data.Height != 1);
        }

        // ===================== 掉落 =====================

        internal static int GetItemDrop(int type, int style = 0)
        {
            var tile = ModContent.Find(ContentKind.Tile, type) as ModTile;
            if (tile == null) return 0;

            foreach (var entry in tile.ItemDrops)
                if (entry.Style == style || entry.Style == -1) return entry.ItemType;
            if (tile.ItemDrop != 0) return tile.ItemDrop;

            BuildItemDrops();
            return ItemDrops.TryGetValue(type + ":" + style, out var item) ? item
                 : ItemDrops.TryGetValue(type.ToString(), out var fallback) ? fallback : 0;
        }

        private static void BuildItemDrops()
        {
            if (ItemDrops.Count > 0) return;
            var samples = Terraria.ID.ContentSamples.ItemsByType;
            if (samples == null) return;
            foreach (var modItem in ModContent.GetContent<ModItem>())
            {
                if (!samples.TryGetValue(modItem.Type, out var item)) continue;
                var tile = item.createTile;
                if (tile < VanillaCount) continue;
                ItemDrops.TryAdd(tile + ":" + item.placeStyle, modItem.Type);
                ItemDrops.TryAdd(tile.ToString(), modItem.Type);
            }
        }

        // ===================== 光照 / 动画 =====================

        private static void LightAreaPrefix(On.Terraria.Lighting.orig_LightTiles orig, int firstX, int lastX, int firstY, int lastY)
        {
            if (Main.gameMenu)
            {
                orig(firstX, lastX, firstY, lastY);
                return;
            }
            if (++_lightAge < 3)
            {
                orig(firstX, lastX, firstY, lastY);
                return;
            }
            _lightAge = 0;
            Lights.Clear();

            const int pad = 28;
            var x0 = Math.Max(0, firstX - pad);
            var x1 = Math.Min(Main.maxTilesX - 1, lastX + pad);
            var y0 = Math.Max(0, firstY - pad);
            var y1 = Math.Min(Main.maxTilesY - 1, lastY + pad);
            for (var x = x0; x <= x1; x++)
            {
                for (var y = y0; y <= y1; y++)
                {
                    var tile = At(x, y);
                    if (tile == null || !Main.tileLighted[tile.Type] || !Over(tile, "ModifyLight")) continue;
                    float r = 0, g = 0, b = 0;
                    try { tile.ModifyLight(x, y, ref r, ref g, ref b); }
                    catch (Exception e) { Safe.Report(tile.Name + ".ModifyLight", e); continue; }
                    if (r > 0 || g > 0 || b > 0) Lights.Add((x, y, r, g, b));
                }
            }
            orig(firstX, lastX, firstY, lastY);
        }


        /// <summary>世界更新之后：把这一轮算好的 mod 光照加进去，并跑 NearbyEffects。</summary>
        private static void UpdateWorldPostfix(On.Terraria.Main.orig_DoUpdateInWorld orig, Main self)
        {
            orig(self);
            if (Main.gameMenu) return;

            if (Lights.Count > 0)
            {
                foreach (var light in Lights)
                    Lighting.AddLight(light.X, light.Y, light.R, light.G, light.B);
            }

            if (NearbyHook) NearbyTick();
        }

        private static int _nearbyAge = 5;

        /// <summary>NearbyEffects：不挂 SceneMetrics.Scan（struct 按值会脏输入），世界更新后自己扫玩家周围（buffScanArea，closer=false）+ 屏幕内（closer=true），每 5 帧。</summary>
        private static void NearbyTick()
        {
            if (++_nearbyAge < 5) return;
            _nearbyAge = 0;

            var player = Main.LocalPlayer;
            if (player == null) return;
            var cx = (int)(player.Center.X / 16f);
            var cy = (int)(player.Center.Y / 16f);
            var w = Math.Max(16, Main.buffScanAreaWidth / 2);
            var h = Math.Max(16, Main.buffScanAreaHeight / 2);
            Nearby(cx - w, cy - h, cx + w, cy + h, false);

            var screen = Main.screenPosition;
            var x0 = (int)(screen.X / 16f);
            var y0 = (int)(screen.Y / 16f);
            Nearby(x0, y0, x0 + (int)(Main.screenWidth / 16f) + 1, y0 + (int)(Main.screenHeight / 16f) + 1, true);
        }

        private static void Nearby(int x0, int y0, int x1, int y1, bool closer)
        {
            x0 = Math.Max(0, x0);
            y0 = Math.Max(0, y0);
            x1 = Math.Min(Main.maxTilesX - 1, x1);
            y1 = Math.Min(Main.maxTilesY - 1, y1);
            for (var x = x0; x <= x1; x++)
            {
                for (var y = y0; y <= y1; y++)
                {
                    var tile = At(x, y);
                    if (tile == null || !Over(tile, "NearbyEffects")) continue;
                    try { tile.NearbyEffects(x, y, closer); }
                    catch (Exception e) { Safe.Report(tile.Name + ".NearbyEffects", e); }
                }
            }
        }

        private static void AnimateTilesPostfix(On.Terraria.Main.orig_AnimateTiles orig)
        {
            orig();
            if (!AnimationHook) return;
            foreach (var tile in ModContent.GetContent<ModTile>())
            {
                if (!Over(tile, "AnimateTile")) continue;
                var type = tile.Type;
                var frame = Main.tileFrame[type];
                var counter = Main.tileFrameCounter[type];
                try { tile.AnimateTile(ref frame, ref counter); }
                catch (Exception e) { Safe.Report(tile.Name + ".AnimateTile", e); continue; }
                Main.tileFrame[type] = frame;
                Main.tileFrameCounter[type] = counter;
            }
            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.AnimateTile)))
                Safe.Run(global.Name + ".AnimateTile", global.AnimateTile);
        }

        // ===================== 绘制数据 =====================

        private static void DrawDataPostfix(On.Terraria.GameContent.Drawing.TileDrawing.orig_GetTileDrawData orig, TileDrawing self,
                                            int x, int y, TileDrawInfo tileCache, ushort typeCache,
                                            ref short tileFrameX, ref short tileFrameY,
                                            ref int tileWidth, ref int tileHeight, ref int tileTop, ref int halfBrickHeight,
                                            ref int addFrX, ref int addFrY, ref SpriteEffects tileSpriteEffect,
                                            ref Texture2D glowTexture, ref Rectangle glowSourceRect, ref Color glowColor)
        {
            orig(self, x, y, tileCache, typeCache, ref tileFrameX, ref tileFrameY,
                 ref tileWidth, ref tileHeight, ref tileTop, ref halfBrickHeight,
                 ref addFrX, ref addFrY, ref tileSpriteEffect,
                 ref glowTexture, ref glowSourceRect, ref glowColor);
            if (typeCache < VanillaCount) return;
            var tile = ModContent.Find(ContentKind.Tile, typeCache) as ModTile;
            if (tile == null) return;

            var data = TileObjectData.GetTileData(typeCache, 0, 0);
            if (data != null)
            {
                tileWidth = data.CoordinateWidth;
                tileTop = data.DrawYOffset;
                if (data.CoordinateHeights != null && data.Height > 0)
                {
                    var row = 0;
                    var fullHeight = data.CoordinateFullHeight;
                    for (var rest = fullHeight > 0 ? tileFrameY % fullHeight : 0;
                         row + 1 < data.Height && data.CoordinateHeights[row] + data.CoordinatePadding > 0 &&
                         rest - data.CoordinateHeights[row] - data.CoordinatePadding >= 0;
                         row++)
                    {
                        rest -= data.CoordinateHeights[row] + data.CoordinatePadding;
                    }
                    if (row < data.CoordinateHeights.Length) tileHeight = data.CoordinateHeights[row];
                }
            }

            if (AnimationHeight.TryGetValue(typeCache, out var height))
                addFrY = Main.tileFrame[typeCache] * height;

            if (Over(tile, "SetDrawPositions"))
            {
                var frameX = (int)tileFrameX;
                var frameY = (int)tileFrameY;
                try { tile.SetDrawPositions(x, y, ref tileWidth, ref tileTop, ref tileHeight, ref frameX, ref frameY); }
                catch (Exception e) { Safe.Report(tile.Name + ".SetDrawPositions", e); }
                tileFrameX = (short)frameX;
                tileFrameY = (short)frameY;
            }
            if (Over(tile, "AnimateIndividualTile"))
            {
                try { tile.AnimateIndividualTile(typeCache, x, y, ref addFrX, ref addFrY); }
                catch (Exception e) { Safe.Report(tile.Name + ".AnimateIndividualTile", e); }
            }
            if (Over(tile, "SetSpriteEffects"))
            {
                try { tile.SetSpriteEffects(x, y, ref tileSpriteEffect); }
                catch (Exception e) { Safe.Report(tile.Name + ".SetSpriteEffects", e); }
            }
        }

        // ===================== 交互 =====================

        private static void TileUsePrefix(On.Terraria.Player.orig_TileInteractionsUse orig, Player self, int myX, int myY)
        {
            var tile = At(myX, myY);
            if (tile == null)
            {
                orig(self, myX, myY);
                return;
            }

            // 手机每帧调三次；只认一次新按下。
            if (!FreshPress(self)) return;   // 非新按下：跳过原版

            var right = Over(tile, "RightClick");
            var handled = false;
            if (right)
            {
                try { handled = tile.RightClick(myX, myY); }
                catch (Exception e) { Safe.Report(tile.Name + ".RightClick", e); }
            }
            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.RightClick)))
            {
                try { global.RightClick(myX, myY, tile.Type); }
                catch (Exception e) { Safe.Report(global.Name + ".RightClick", e); }
            }
            if (handled) self.tileInteractionHappened = true;
            // 已处理本帧交互：跳过原版（触摸不会变成 RightClick）
        }

        private static bool FreshPress(Player player)
        {
            if (!player.tileInteractAttempted) return false;
            var frame = Main.GameUpdateCount;
            var index = player.whoAmI;
            if (LastPress.TryGetValue(index, out var last) && last == frame) return false;
            LastPress[index] = frame;
            return player.releaseUseTile || last == 0 || frame - last > 1;
        }

        private static void MouseOverPostfix(On.Terraria.Player.orig_TileInteractionsMouseOver orig, Player self, int myX, int myY)
        {
            orig(self, myX, myY);
            if (myX != Player.tileTargetX || myY != Player.tileTargetY) return;
            var tile = At(myX, myY);
            if (tile == null) return;

            try { tile.MouseOver(myX, myY); }
            catch (Exception e) { Safe.Report(tile.Name + ".MouseOver", e); }
            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.MouseOver)))
            {
                try { global.MouseOver(myX, myY, tile.Type); }
                catch (Exception e) { Safe.Report(global.Name + ".MouseOver", e); }
            }

            // 手机上没有光标图标，触摸不会变成 RightClick。
            if (!self.cursorItemIconEnabled && Over(tile, "RightClick"))
            {
                self.noThrow = 2;
                self.cursorItemIconEnabled = true;
                self.cursorItemIconID = GetItemDrop(tile.Type);
            }
        }

        private static void MouseOverFarPostfix(On.Terraria.Player.orig_TileInteractionsCheckLongDistance orig, Player self, int myX, int myY)
        {
            orig(self, myX, myY);
            var tile = At(myX, myY);
            if (tile == null) return;
            try { tile.MouseOverFar(myX, myY); }
            catch (Exception e) { Safe.Report(tile.Name + ".MouseOverFar", e); }
            foreach (var global in GlobalTileRegistry.For(nameof(GlobalTile.MouseOverFar)))
            {
                try { global.MouseOverFar(myX, myY, tile.Type); }
                catch (Exception e) { Safe.Report(global.Name + ".MouseOverFar", e); }
            }
        }

        // ===================== 表维护 =====================

        private static void FixTileMerge(int total)
        {
            var merge = Main.tileMerge;
            if (merge == null) return;
            if (merge.Length < total)
            {
                var grown = new Il2CppReferenceArray<Il2CppStructArray<bool>>(total);
                for (var i = 0; i < merge.Length; i++) grown[i] = merge[i];
                Main.tileMerge = grown;
                merge = grown;
            }
            for (var i = 0; i < total; i++)
            {
                var row = merge[i];
                if (row != null && row.Length >= total) continue;
                var fresh = new bool[total];
                if (row != null) Array.Copy(row, fresh, Math.Min(row.Length, total));
                merge[i] = fresh;
            }
        }

        private static void HookSetupTileMerge()
        {
            Hooks.Once("tile.setupmerge", () =>
            {
                try
                {
                    On.Terraria.Main.SetupTileMerge += SetupMergeHook;
                    _log?.Info("[tile] Main.SetupTileMerge 已挂");
                }
                catch (Exception e) { _log?.Warn("[tile] 挂 Main.SetupTileMerge 失败：" + e.Message); }
            });
        }

        /// <summary>SetupTileMerge 一次调用里先备份（MergePrefix）后恢复（MergePostfix）；合并成单个订阅避免两个 handler 各自调 orig 造成二次执行。</summary>
        private static void SetupMergeHook(On.Terraria.Main.orig_SetupTileMerge orig)
        {
            MergePrefix();
            orig();
            MergePostfix();
        }

        private static void MergePrefix()
        {
            var merge = Main.tileMerge;
            if (merge == null || merge.Length < _total) return;
            var mods = _total - VanillaCount;
            _mergeRows = new bool[mods * _total];
            _mergeCols = new bool[VanillaCount * mods];
            for (var t = 0; t < mods; t++)
            {
                var row = merge[VanillaCount + t];
                if (row == null) continue;
                for (var x = 0; x < _total; x++) _mergeRows[t * _total + x] = row[x];
            }
            for (var v = 0; v < VanillaCount; v++)
            {
                var row = merge[v];
                if (row == null) continue;
                for (var t = 0; t < mods; t++)
                    if (VanillaCount + t < row.Length) _mergeCols[v * mods + t] = row[VanillaCount + t];
            }
        }

        private static void MergePostfix()
        {
            if (_total <= VanillaCount) return;
            FixTileMerge(_total);
            var merge = Main.tileMerge;
            var mods = _total - VanillaCount;
            if (merge == null || _mergeRows == null) return;
            for (var t = 0; t < mods; t++)
            {
                var row = merge[VanillaCount + t];
                if (row == null) continue;
                for (var x = 0; x < _total; x++) row[x] = _mergeRows[t * _total + x];
            }
            for (var v = 0; v < VanillaCount && v < merge.Length; v++)
            {
                var row = merge[v];
                if (row == null) continue;
                for (var t = 0; t < mods; t++)
                    if (VanillaCount + t < row.Length) row[VanillaCount + t] = _mergeCols[v * mods + t];
            }
        }

        // ---- TileData.TileLists：世界把 tile 定义按 (type << 12) 内联在一张 uint 表里；mod 类型 key 越界（真机 SIGSEGV）----
        // 注意：不能走托管 getter（uint* 被 interop 解坏），也不能把 C# null 当空指针，只用 native field 读写 + IntPtr.Zero。
        private static IntPtr _tileListsField;
        private static IntPtr _tileLists;
        private static int _tileListEntries;

        private static unsafe IntPtr TileListsField()
        {
            if (_tileListsField != IntPtr.Zero) return _tileListsField;
            var cls = Il2CppClassPointerStore<TileData>.NativeClassPtr;
            if (cls == IntPtr.Zero) return IntPtr.Zero;
            _tileListsField = IL2CPP.il2cpp_class_get_field_from_name(cls, "TileLists");
            return _tileListsField;
        }

        private static unsafe IntPtr ReadTileLists()
        {
            var field = TileListsField();
            if (field == IntPtr.Zero) return IntPtr.Zero;
            var value = IntPtr.Zero;
            IL2CPP.il2cpp_field_static_get_value(field, &value);
            return value;
        }

        private static void FixTileLists(int total)
        {
            if (total <= VanillaCount) return;
            try
            {
                var field = TileListsField();
                if (field == IntPtr.Zero)
                {
                    _log?.Warn("[tile] 找不到 TileData.TileLists");
                    return;
                }

                var typeCount = TileData.TileListTypeCount;
                var current = ReadTileLists();
                var entries = total << 12;
                if (current == _tileLists && _tileListEntries >= entries) return;
                _log?.Info($"[tile] TileLists ptr=0x{current.ToInt64():x} typeCount={typeCount} total={total}");

                var memory = Marshal.AllocHGlobal(entries * sizeof(uint));
                var copyTypes = typeCount > 0 && typeCount <= VanillaCount ? typeCount : VanillaCount;
                unsafe
                {
                    var table = (uint*)memory;
                    for (var i = 0; i < entries; i++) table[i] = 0xFFFFFFFFu;

                    // 原版表的实际类型数可能小于 754；按它拷，避免读越界。
                    if (current != IntPtr.Zero)
                    {
                        var source = (uint*)current;
                        var copyEntries = copyTypes << 12;
                        for (var i = 0; i < copyEntries; i++) table[i] = source[i];
                    }
                    IL2CPP.il2cpp_field_static_set_value(field, memory.ToPointer());
                }
                _tileLists = memory;
                _tileListEntries = entries;
                _log?.Info($"[tile] TileData.TileLists 扩到 {entries} 项（拷 {copyTypes} 个类型）");
            }
            catch (Exception e)
            {
                _log?.Warn("[tile] TileData.TileLists 扩容异常：" + e);
            }
        }

        private static void AllocatePostfix(On.Terraria.TileData.orig_Allocate orig, TileData self, int x, int y)
        {
            orig(self, x, y);
            FixTileLists(_total);
        }

        private static void FixGlowMask(int total)
        {
            var glow = Main.tileGlowMask;
            if (glow == null || glow.Length < total || total <= VanillaCount) return;
            // 游戏还没填 -1 时先等（下一帧/下一次维护再写）。
            if (glow[0] != -1 && glow[1] != -1) return;
            for (var type = VanillaCount; type < total; type++) glow[type] = -1;
        }

        private static void PadObjectData(int total)
        {
            var list = TileObjectData._data;
            if (list == null) return;
            // 空槽给 1x1 默认形状而不是 null：原版 PlaceTile 直接解引用 GetTileData 返回值（null → 真机 SIGSEGV）。
            while (list.Count < total) list.Add(TileObjectData.Style1x1);
        }

        private static void GrowInstances(int total)
        {
            foreach (var player in Main.player)
            {
                if (player == null) continue;
                var adj = player.adjTile;
                if (adj != null && adj.Length < total)
                {
                    var grown = new bool[total];
                    Array.Copy(adj, grown, adj.Length);
                    player.adjTile = grown;
                }
            }

            GrowMetrics(Main.SceneMetrics, total);
            GrowMetrics(Main.PlayerSceneMetrics, total);
            GrowMetrics(Main._cameraSceneMetrics, total);

            try
            {
                var crafting = GUIInstance.Active?.GUICrafting;
                if (crafting != null && crafting.oldAdjTile != null && crafting.oldAdjTile.Length < total)
                {
                    var grown = new bool[total];
                    Array.Copy(crafting.oldAdjTile, grown, crafting.oldAdjTile.Length);
                    crafting.oldAdjTile = grown;
                }
            }
            catch { /* 菜单还没建 */ }
        }

        private static void GrowMetrics(SceneMetrics metrics, int total)
        {
            if (metrics == null) return;
            var counts = metrics._tileCounts;
            if (counts == null || counts.Length >= total) return;
            var grown = new int[total];
            Array.Copy(counts, grown, counts.Length);
            metrics._tileCounts = grown;
        }

        /// <summary>每帧维护：表被游戏重建/实例新建时补扩。由 ContentInstaller 调。</summary>
        internal static void Maintain()
        {
            if (!_setupDone || _total <= VanillaCount) return;
            try
            {
                FixGlowMask(_total);
                FixTileLists(_total);
                PadObjectData(_total);
                GrowInstances(_total);
                if (_total > VanillaCount) FixTileMerge(_total);
            }
            catch (Exception e)
            {
                _log?.Warn("[tile] 维护失败：" + e);
            }
        }
    }
}
