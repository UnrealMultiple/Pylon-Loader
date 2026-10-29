using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;

namespace Pylon.ModLoader
{
    /// <summary>自定义墙壁的运行期管线；结构同 TileLoader。</summary>
    internal static class WallLoader
    {
        private const int VanillaCount = 367;

        private static ModLogger _log;
        private static bool _installed;
        private static bool _setupDone;
        private static int _total;

        private static readonly Dictionary<Type, OverridePlan> Plans = new();
        private static readonly Dictionary<string, int> ItemDrops = new();
        private static readonly Dictionary<int, int> FrameStyle = new();
        private static readonly List<(int X, int Y, float R, float G, float B)> Lights = new();

        private static int _dustLeft = -1;
        private static bool _fail;
        private static int _drop = -1;
        private static int _oldFrameX;
        private static int _oldFrameY;
        private static int _oldFrameNumber;
        private static int _lightAge = 3;

        internal static bool Any => ModContent.Count(ContentKind.Wall) > 0;

        internal static ModWall At(int i, int j)
        {
            if (!Any) return null;
            try { return ModContent.Find(ContentKind.Wall, Main.tile[i, j].wall) as ModWall; }
            catch { return null; }
        }

        private static bool Over(ModWall wall, string method) => PlanOf(wall).Has(method);

        private static OverridePlan PlanOf(ModWall wall)
        {
            var type = wall.GetType();
            if (Plans.TryGetValue(type, out var plan)) return plan;
            plan = OverridePlan.Build(type, typeof(ModWall));
            Plans[type] = plan;
            return plan;
        }

        internal static void Install(ModLogger log)
        {
            if (_installed || !Any) return;
            _installed = true;
            _log = log;

            On.Terraria.WorldGen.KillWall += KillWall;
            On.Terraria.WorldGen.KillWall_MakeWallDust += MakeDustPrefix;
            On.Terraria.WorldGen.KillWall_PlaySounds += PlaySoundsPrefix;
            On.Terraria.WorldGen.KillWall_DropItems += DropItems;
            On.Terraria.WorldGen.KillWall_GetItemDrops += GetItemDropsPrefix;
            On.Terraria.Framing.WallFrame += WallFrame;
            On.Terraria.WorldGen.UpdateWorld_OvergroundTile += RandomUpdateOverground;
            On.Terraria.WorldGen.UpdateWorld_UndergroundTile += RandomUpdateUnderground;

            if (HasAny("ModifyLight"))
            {
                On.Terraria.Lighting.LightTiles += LightAreaPrefix;
                On.Terraria.Main.DoUpdateInWorld += ApplyLightsPostfix;
            }
            if (HasAny("AnimateWall"))
            {
                On.Terraria.Main.DoUpdate_AnimateWalls += AnimateWallsPostfix;
            }
        }

        private static bool HasAny(string method)
        {
            foreach (var wall in ModContent.GetContent<ModWall>())
                if (Over(wall, method)) return true;
            return false;
        }

        internal static void Setup(ModLogger log)
        {
            if (_setupDone || !Any) return;
            _setupDone = true;
            _log = log;
            _total = ModContent.NextType(ContentKind.Wall);

            var walls = ModContent.GetContent<ModWall>();
            foreach (var wall in walls)
            {
                try { wall.SetStaticDefaults(); }
                catch (Exception e) { log?.Warn($"{wall.Name} 的 SetStaticDefaults 失败：{e.Message}"); }
            }

            var blend = Main.wallBlend;
            if (blend != null && blend.Length >= _total)
                for (var type = VanillaCount; type < _total; type++)
                    if (blend[type] == 0) blend[type] = type;

            foreach (var wall in walls)
            {
                try { BlankMapEntry(wall.Type); }
                catch (Exception e) { log?.Warn($"{wall.Name} 的地图项写入失败：{e.Message}"); }
            }

            WallID.Count = (ushort)_total;
            log?.Info($"[wall] 已安装 {walls.Count} 面墙（id {VanillaCount}..{_total - 1}），WallID.Count={WallID.Count}");
        }

        /// <summary>地图项暂不接；写 0 让地图跳过而不是读越界。</summary>
        private static void BlankMapEntry(int type)
        {
            var lookup = Terraria.Map.MapHelper.wallLookup;
            var counts = Terraria.Map.MapHelper.wallOptionCounts;
            if (lookup != null && type < lookup.Length) lookup[type] = 0;
            if (counts != null && type < counts.Length) counts[type] = 0;
        }

        // ===================== 挖 =====================

        private static void KillWall(On.Terraria.WorldGen.orig_KillWall orig, int i, int j, bool fail)
        {
            var wall = At(i, j);
            if (wall != null)
            {
                try { wall.KillWall(i, j, ref fail); }
                catch (Exception e) { Safe.Report(wall.Name + ".KillWall", e); }

                var count = fail ? 1 : 3;
                try { wall.NumDust(i, j, fail, ref count); }
                catch (Exception e) { Safe.Report(wall.Name + ".NumDust", e); }

                _dustLeft = Math.Max(0, count);
                _fail = fail;
            }

            orig(i, j, fail);
            _dustLeft = -1;
        }

        private static void MakeDustPrefix(On.Terraria.WorldGen.orig_KillWall_MakeWallDust orig,
                                           int i, int j, Tile tileCache)
        {
            var wall = ModContent.Find(ContentKind.Wall, tileCache.wall) as ModWall;
            if (wall == null) { orig(i, j, tileCache); return; }

            if (_dustLeft == 0) return;
            if (_dustLeft > 0) _dustLeft--;

            var dustType = wall.DustType;
            try
            {
                if (!wall.CreateDust(i, j, ref dustType)) return;
            }
            catch (Exception e) { Safe.Report(wall.Name + ".CreateDust", e); }

            if (dustType < 0) return;
            Dust.NewDust(new Vector2(i * 16, j * 16), 16, 16, dustType, 0, 0, 0, Color.White, 1f);
        }

        private static void PlaySoundsPrefix(On.Terraria.WorldGen.orig_KillWall_PlaySounds orig,
                                             int i, int j, Tile tileCache)
        {
            var wall = ModContent.Find(ContentKind.Wall, tileCache.wall) as ModWall;
            if (wall == null) { orig(i, j, tileCache); return; }

            try
            {
                if (!wall.KillSound(i, j, _fail)) return;   // mod 不要原版声音
            }
            catch (Exception e) { Safe.Report(wall.Name + ".KillSound", e); }

            if (wall.HitSound < 0) { orig(i, j, tileCache); return; }
            Terraria.Audio.SoundEngine.PlaySound(wall.HitSound, i * 16, j * 16, 1, 1f, 0f);
        }

        private static void DropItems(On.Terraria.WorldGen.orig_KillWall_DropItems orig,
                                      int i, int j, Tile tileCache)
        {
            var wall = ModContent.Find(ContentKind.Wall, tileCache.wall) as ModWall;
            if (wall != null)
            {
                var item = GetItemDrop(wall);
                try
                {
                    if (!wall.Drop(i, j, ref item)) _drop = 0;
                    else _drop = item;
                }
                catch (Exception e) { Safe.Report(wall.Name + ".Drop", e); _drop = item; }
            }

            orig(i, j, tileCache);
            _drop = -1;
        }

        private static int GetItemDropsPrefix(On.Terraria.WorldGen.orig_KillWall_GetItemDrops orig,
                                              Tile tileCache)
        {
            var wall = ModContent.Find(ContentKind.Wall, tileCache.wall) as ModWall;
            if (wall == null) return orig(tileCache);
            return _drop >= 0 ? _drop : GetItemDrop(wall);
        }

        internal static int GetItemDrop(ModWall wall)
        {
            if (wall.ItemDrop != 0) return wall.ItemDrop;
            BuildItemDrops();
            return ItemDrops.TryGetValue(wall.Type.ToString(), out var item) ? item : 0;
        }

        private static void BuildItemDrops()
        {
            if (ItemDrops.Count > 0) return;
            var samples = Terraria.ID.ContentSamples.ItemsByType;
            if (samples == null) return;
            foreach (var modItem in ModContent.GetContent<ModItem>())
            {
                if (!samples.TryGetValue(modItem.Type, out var item)) continue;
                var wall = item.createWall;
                if (wall < VanillaCount) continue;
                ItemDrops.TryAdd(wall.ToString(), modItem.Type);
            }
        }

        // ===================== 世界 =====================

        private static void RandomUpdateOverground(On.Terraria.WorldGen.orig_UpdateWorld_OvergroundTile orig,
                                                   int i, int j, bool checkNPCSpawns, int wallDist)
        {
            orig(i, j, checkNPCSpawns, wallDist);
            RandomUpdate(i, j);
        }

        private static void RandomUpdateUnderground(On.Terraria.WorldGen.orig_UpdateWorld_UndergroundTile orig,
                                                    int i, int j, bool checkNPCSpawns, int wallDist)
        {
            orig(i, j, checkNPCSpawns, wallDist);
            RandomUpdate(i, j);
        }

        private static void RandomUpdate(int i, int j)
        {
            var wall = At(i, j);
            if (wall == null) return;
            try { wall.RandomUpdate(i, j); }
            catch (Exception e) { Safe.Report(wall.Name + ".RandomUpdate", e); }
        }

        private static void WallFrame(On.Terraria.Framing.orig_WallFrame orig, int i, int j, bool resetFrame)
        {
            var tile = Main.tile[i, j];
            _oldFrameX = tile.wallFrameX();
            _oldFrameY = tile.wallFrameY();
            _oldFrameNumber = tile.wallFrameNumber();

            orig(i, j, resetFrame);

            var wall = At(i, j);
            if (wall == null) return;

            if (FrameStyle.Count == 0) BuildFrameStyle();

            var frameX = tile.wallFrameX();
            var frameY = tile.wallFrameY();
            var number = (int)tile.wallFrameNumber();
            var style = StyleOf(frameX, frameY, number);
            try
            {
                if (!wall.WallFrame(i, j, resetFrame, ref style, ref number))
                {
                    tile.wallFrameX(_oldFrameX);
                    tile.wallFrameY(_oldFrameY);
                    tile.wallFrameNumber((byte)_oldFrameNumber);
                    return;
                }
            }
            catch (Exception e)
            {
                Safe.Report(wall.Name + ".WallFrame", e);
                return;
            }

            var lookup = Framing.wallFrameLookup;
            if (lookup == null || style < 0 || style >= lookup.Length) return;
            var row = lookup[style];
            if (row == null || number < 0 || number >= row.Length) return;
            tile.wallFrameX(row[number].X);
            tile.wallFrameY(row[number].Y);
            tile.wallFrameNumber((byte)number);
        }

        private static void BuildFrameStyle()
        {
            var lookup = Framing.wallFrameLookup;
            if (lookup == null) return;
            for (var style = 0; style < lookup.Length; style++)
            {
                var row = lookup[style];
                if (row == null) continue;
                for (var number = 0; number < row.Length; number++)
                {
                    var key = Key(row[number].X, row[number].Y, number);
                    FrameStyle.TryAdd(key, style);
                }
            }
        }

        private static int StyleOf(int frameX, int frameY, int number) =>
            FrameStyle.TryGetValue(Key(frameX, frameY, number), out var style) ? style : 0;

        private static int Key(int x, int y, int number) => (x & 0xFF) | ((y & 0xFF) << 8) | ((number & 0xFF) << 16);

        // ===================== 光照 / 动画 =====================

        private static void LightAreaPrefix(On.Terraria.Lighting.orig_LightTiles orig,
                                            int firstX, int lastX, int firstY, int lastY)
        {
            if (!Main.gameMenu && ++_lightAge >= 3)
            {
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
                        var wall = At(x, y);
                        if (wall == null || !Main.wallLighted[wall.Type] || !Over(wall, "ModifyLight")) continue;
                        float r = 0, g = 0, b = 0;
                        try { wall.ModifyLight(x, y, ref r, ref g, ref b); }
                        catch (Exception e) { Safe.Report(wall.Name + ".ModifyLight", e); continue; }
                        if (r > 0 || g > 0 || b > 0) Lights.Add((x, y, r, g, b));
                    }
                }
            }

            orig(firstX, lastX, firstY, lastY);
        }

        private static void ApplyLightsPostfix(On.Terraria.Main.orig_DoUpdateInWorld orig, Main self)
        {
            orig(self);
            if (Main.gameMenu || Lights.Count == 0) return;
            foreach (var light in Lights)
                Lighting.AddLight(light.X, light.Y, light.R, light.G, light.B);
        }

        private static void AnimateWallsPostfix(On.Terraria.Main.orig_DoUpdate_AnimateWalls orig)
        {
            orig();
            foreach (var wall in ModContent.GetContent<ModWall>())
            {
                if (!Over(wall, "AnimateWall")) continue;
                var type = wall.Type;
                var frame = (int)Main.wallFrame[type];
                var counter = (int)Main.wallFrameCounter[type];
                try { wall.AnimateWall(ref frame, ref counter); }
                catch (Exception e) { Safe.Report(wall.Name + ".AnimateWall", e); continue; }
                Main.wallFrame[type] = (byte)(frame & 0xFF);
                Main.wallFrameCounter[type] = (byte)(counter & 0xFF);
            }
        }

        /// <summary>每帧维护：WallID.Count 被游戏改回原版时重新顶上去。</summary>
        internal static void Maintain()
        {
            if (!_setupDone || _total <= VanillaCount) return;
            if (WallID.Count < _total) WallID.Count = (ushort)_total;
            var blend = Main.wallBlend;
            if (blend != null)
                for (var type = VanillaCount; type < _total && type < blend.Length; type++)
                    if (blend[type] == 0) blend[type] = type;
        }
    }
}
