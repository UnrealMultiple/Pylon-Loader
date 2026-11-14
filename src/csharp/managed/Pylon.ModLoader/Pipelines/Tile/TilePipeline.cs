using Pylon.ModLoader.Tables;
using Pylon.ModLoader.Textures;
using Terraria;
using Terraria.ID;
using Terraria.ObjectData;

namespace Pylon.ModLoader;

/// <summary>
/// 方块管线：挖、尘土、掉落、边框、通电、制作站、右键。
/// 每类型的位集与掉落数据在 TileTable（密集 struct 数组）。
/// </summary>
internal sealed class TilePipeline : ILoaderModule
{
    public static TilePipeline Instance { get; } = new();

    private readonly HashSet<int> _reported = [];

    public string Name => "tile";

    public IReadOnlyList<TableRequest> Tables =>
        [new(TileTables.Set, () => ModContent.NextType(ContentKind.Tile), early: true)];


    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.WorldGen.CanKillTile_int_int_SpecialKillTileContext += CanKill;
        On.Terraria.WorldGen.CanKillTile_int_int += CanKillFast;
        On.Terraria.WorldGen.CanKillTile_int_int_boolRef += CanKillOut;
        On.Terraria.WorldGen.KillTile += Kill;
        On.Terraria.WorldGen.KillTile_PlaySounds += PlaySounds;
        On.Terraria.WorldGen.KillTile_MakeTileDust += MakeDust;
        On.Terraria.WorldGen.KillTile_DropItems += DropItems;
        On.Terraria.WorldGen.TileFrame += Frame;
        On.Terraria.Wiring.HitWireSingle += Wire;
        On.Terraria.Player.AdjTiles += Stations;
        On.Terraria.Player.TileInteractionsUse += Interact;
        On.Terraria.Player.TileInteractionsMouseOver += MouseOver;
        On.Terraria.WorldGen.UpdateWorld_OvergroundTile += RandomUpdateOver;
        On.Terraria.WorldGen.UpdateWorld_UndergroundTile += RandomUpdateUnder;
        TileMergeFix.Install();
        TileInteractionHooks.Install();

        Log.Info("[tile] 已挂（挖 / 尘土 / 掉落 / 边框 / 通电 / 制作站 / 右键 / 光标 / 随机更新）；逐格绘制与光照本版无入口");
        return true;
    }

    private int _maintained;

    internal void Build()
    {
        LimitPatch.Apply(VanillaCounts.Tile + ModContent.Count(ContentKind.Tile), Log);
        TileTable.Build(Log);
        TileMergeFix.FixToTotal();
        PostSetupTileMerge();
        ModDoors.Build(Log);
        TileSavePipeline.Build(Log);
        TileLists.Build(Log);
        ModTileMap.Build(Log);
        TileDrawHooks.Build(Log);
    }

    private static void PostSetupTileMerge()
    {
        var globals = GlobalTileRegistry.For(HookId.PostSetupTileMerge);
        for (var i = 0; i < globals.Count; i++)
        {
            var global = globals[i];
            Safe.Run(global.Name + ".PostSetupTileMerge", global.PostSetupTileMerge);
        }
    }

    internal void Maintain()
    {
        var total = TileTable.Count;
        if (total <= 0) return;

        try
        {
            if (_maintained != total)
            {
                _maintained = total;

                var glow = Main.tileGlowMask;
                if (glow is not null && glow.Length >= total)
                {
                    for (var type = TileID.Count; type < total; type++) glow[type] = -1;
                }

                foreach (var player in Main.player)
                {
                    if (player is null) continue;

                    var adj = player.adjTile;
                    if (adj is null || adj.Length >= total) continue;

                    var grown = new bool[total];
                    Array.Copy(adj, grown, adj.Length);
                    player.adjTile = grown;
                }
            }

            var data = TileObjectData._data;
            if (data is not null && data.Count < total)
            {
                while (data.Count < total) data.Add(TileObjectData.Style1x1);

                var shapes = TileTable.Shapes;
                var restored = 0;
                for (var type = VanillaCounts.Tile; type < total && type < shapes.Length; type++)
                {
                    if (shapes[type] is not { } shape) continue;
                    data[type] = shape;
                    restored++;
                }

                if (restored > 0) Log.Info($"[tile] _data 重建后回放 {restored} 个 mod 形状");
            }
        }
        catch (Exception e)
        {
            Log.Warn("[tile] 维护失败：" + Safe.Describe(e));
        }
    }

    #region 挖

    private bool CanKill(On.Terraria.WorldGen.orig_CanKillTile_int_int_SpecialKillTileContext orig, int i, int j,
                         WorldGen.SpecialKillTileContext context)
    {
        var tile = TileTable.At(i, j);
        if (tile is null || !tile.HookEnabled(HookId.CanKillTile)) return orig(i, j, context);

        var damaged = false;

        try
        {
            return tile.CanKillTile(i, j, ref damaged) && orig(i, j, context);
        }
        catch (Exception e)
        {
            Fail(tile, HookId.CanKillTile, e);
            return orig(i, j, context);
        }
    }

    private bool CanKillFast(On.Terraria.WorldGen.orig_CanKillTile_int_int orig, int i, int j)
    {
        var tile = TileTable.At(i, j);
        if (tile is null || !tile.HookEnabled(HookId.CanKillTile)) return orig(i, j);

        var damaged = false;

        try
        {
            return tile.CanKillTile(i, j, ref damaged) && orig(i, j);
        }
        catch (Exception e)
        {
            Fail(tile, HookId.CanKillTile, e);
            return orig(i, j);
        }
    }

    private static bool _removingMulti;

    private bool CanKillOut(On.Terraria.WorldGen.orig_CanKillTile_int_int_boolRef orig, int i, int j, out bool blockDamaged)
    {
        var tile = TileTable.At(i, j);
        if (tile is null || !tile.HookEnabled(HookId.CanKillTile))
            return orig(i, j, out blockDamaged);

        var damaged = false;
        try
        {
            var allowed = tile.CanKillTile(i, j, ref damaged);
            var result = orig(i, j, out var originalDamaged);
            blockDamaged = damaged || originalDamaged;
            return allowed && result;
        }
        catch (Exception e)
        {
            Fail(tile, HookId.CanKillTile, e);
            return orig(i, j, out blockDamaged);
        }
    }

    private void Kill(On.Terraria.WorldGen.orig_KillTile orig, int i, int j, bool fail, bool effectOnly, bool noItem)
    {
        var tile = TileTable.At(i, j);
        var cache = Main.tile[i, j];
        var type = cache.type;
        var frameX = cache.frameX;
        var frameY = cache.frameY;

        var data = type > 0 ? TileObjectData.GetTileData(type, 0) : null;
        var multi = data is not null && data.Width * data.Height > 1;
        var left = i;
        var top = j;

        if (multi)
        {
            left -= (frameX / 18) % data!.Width;
            top -= (frameY / 18) % data.Height;
        }

        if (tile is not null && tile.HookEnabled(HookId.KillTile))
        {
            try
            {
                tile.KillTile(i, j, ref fail, ref effectOnly, ref noItem);
            }
            catch (Exception e)
            {
                Fail(tile, HookId.KillTile, e);
            }
        }

        orig(i, j, fail, effectOnly, noItem);

        if (tile is null || !multi || fail || effectOnly || _removingMulti || !tile.HookEnabled(HookId.KillMultiTile)) return;

        var tiles = Main.tile;
        var objectData = data!;

        if (tiles[i, j].active() && tiles[i, j].type == type) return;

        for (var x = left; x < left + objectData.Width; x++)
        {
            for (var y = top; y < top + objectData.Height; y++)
            {
                if (x == i && y == j) continue;

                var part = tiles[x, y];
                if (!part.active() || part.type != type) continue;

                _removingMulti = true;
                try
                {
                    WorldGen.KillTile(x, y, false, false, true);
                }
                finally
                {
                    _removingMulti = false;
                }
            }
        }

        try
        {
            tile.KillMultiTile(left, top,
                (short)(frameX - ((frameX / 18) % objectData.Width) * 18),
                (short)(frameY - ((frameY / 18) % objectData.Height) * 18));
        }
        catch (Exception e)
        {
            Fail(tile, HookId.KillMultiTile, e);
        }
    }

    #endregion

    #region 尘土与掉落

    private void PlaySounds(On.Terraria.WorldGen.orig_KillTile_PlaySounds orig, int i, int j, bool fail,
                            Tile tileCache)
    {
        var type = tileCache.type;
        var tile = TileTable.Of(type);
        if (tile is not null && tile.HookEnabled(HookId.KillSound))
        {
            try
            {
                if (!tile.KillSound(i, j, fail)) return;
            }
            catch (Exception e)
            {
                Fail(tile, HookId.KillSound, e);
            }
        }

        var globals = GlobalTileRegistry.For(HookId.KillSound);
        for (var index = 0; index < globals.Count; index++)
        {
            var global = globals[index];
            try
            {
                if (!global.KillSound(i, j, type, fail)) return;
            }
            catch (Exception e)
            {
                if (_reported.Add(HashCode.Combine(global.GetType(), HookId.KillSound)))
                    ContentErrors.Report($"[tile] {global.GetType().Name}.KillSound 异常:{Safe.Describe(e)}");
            }
        }

        if (tile is not null)
        {
            var sound = -1;
            try
            {
                sound = tile.HitSound;
            }
            catch (Exception e)
            {
                Fail(tile, HookId.KillSound, e);
            }

            if (sound >= 0)
            {
                Terraria.Audio.SoundEngine.PlaySound(sound, i * 16, j * 16, 1, 1f, 0f);
                return;
            }
        }

        orig(i, j, fail, tileCache);
    }

    private int MakeDust(On.Terraria.WorldGen.orig_KillTile_MakeTileDust orig, int i, int j, Tile tileCache)
    {
        var tile = TileTable.At(i, j);
        if (tile is null || !tile.HookEnabled(HookId.CreateDust)) return orig(i, j, tileCache);

        var type = tile.DustType > 0 ? tile.DustType : 0;

        try
        {
            if (!tile.CreateDust(i, j, ref type)) return 0;

            return orig(i, j, tileCache);
        }
        catch (Exception e)
        {
            Fail(tile, HookId.CreateDust, e);
            return orig(i, j, tileCache);
        }
    }

    private void DropItems(On.Terraria.WorldGen.orig_KillTile_DropItems orig, int x, int y, Tile tileCache,
                           bool includeLargeObjectDrops)
    {
        if (_removingMulti)
        {
            orig(x, y, tileCache, includeLargeObjectDrops);
            return;
        }

        var tile = TileTable.At(x, y);

        if (tile is null || !tile.HookEnabled(HookId.CanDrop) && !tile.HookEnabled(HookId.GetItemDrops))
        {
            DropDefault(tileCache, x, y);
            orig(x, y, tileCache, includeLargeObjectDrops);
            return;
        }

        try
        {
            if (!tile.CanDrop(x, y))
            {
                orig(x, y, tileCache, includeLargeObjectDrops);
                return;
            }

            var drops = tile.GetItemDrops(x, y);
            if (drops is not null && drops.Length > 0)
            {
                for (var k = 0; k < drops.Length; k++)
                {
                    var type = drops[k];
                    if (type > 0) Item.NewItem(x * 16, y * 16, 16, 16, type);
                }

                orig(x, y, tileCache, includeLargeObjectDrops);
                return;
            }
        }
        catch (Exception e)
        {
            Fail(tile, HookId.GetItemDrops, e);
        }

        DropDefault(tileCache, x, y);
        orig(x, y, tileCache, includeLargeObjectDrops);
    }

    private void DropDefault(Tile tileCache, int i, int j)
    {
        var item = TileTable.DropOf(tileCache.type, StyleOf(tileCache));
        if (item > 0) Item.NewItem(i * 16, j * 16, 16, 16, item);
    }

    private static int StyleOf(Tile tile)
    {
        var data = Terraria.ObjectData.TileObjectData.GetTileData(tile.type, 0);
        if (data is null) return 0;

        var styleX = tile.frameX / 18 / data.CoordinateFullWidth;
        var styleY = tile.frameY / 18 / data.CoordinateFullHeight;

        if (data.StyleHorizontal) return data.StyleWrapLimit > 0 ? styleX % data.StyleWrapLimit : styleX;

        return styleX + styleY;
    }

    #endregion

    #region 边框、通电、制作站、右键

    private void Frame(On.Terraria.WorldGen.orig_TileFrame orig, int i, int j, bool resetFrame, bool noBreak)
    {
        var tile = TileTable.At(i, j);

        if (tile is not null && tile.HookEnabled(HookId.TileFrame))
        {
            try
            {
                if (!tile.TileFrame(i, j, ref resetFrame, ref noBreak)) return;
            }
            catch (Exception e)
            {
                Fail(tile, HookId.TileFrame, e);
            }
        }

        orig(i, j, resetFrame, noBreak);
    }

    private void Wire(On.Terraria.Wiring.orig_HitWireSingle orig, int i, int j)
    {
        var tile = TileTable.At(i, j);

        if (tile is not null && tile.HookEnabled(HookId.HitWire))
        {
            try
            {
                tile.HitWire(i, j);
            }
            catch (Exception e)
            {
                Fail(tile, HookId.HitWire, e);
            }
        }

        orig(i, j);
    }

    private void Stations(On.Terraria.Player.orig_AdjTiles orig, Player self)
    {
        orig(self);

        var tiles = Main.tile;
        if (tiles is null) return;

        var all = TileTable.All;
        for (var n = 0; n < all.Length; n++)
        {
            var tile = all[n];
            if (tile is null || !tile.HookEnabled(HookId.AdjTiles)) continue;

            var extra = tile.AdjTiles;
            if (extra is null || extra.Length == 0) continue;

            var type = tile.Type;
            if (!NearBy(self, tiles, type)) continue;

            for (var k = 0; k < extra.Length; k++)
            {
                var station = extra[k];
                if (station >= 0 && station < self.adjTile.Length) self.adjTile[station] = true;
            }
        }
    }

    private static bool NearBy(Player self, TileData tiles, int type)
    {
        var cx = (int)(self.Center.X / 16f);
        var cy = (int)(self.Center.Y / 16f);

        for (var x = cx - 2; x <= cx + 2; x++)
        {
            if (x < 0 || x >= Main.maxTilesX) continue;

            for (var y = cy - 2; y <= cy + 2; y++)
            {
                if (y < 0 || y >= Main.maxTilesY) continue;

                if (tiles[x, y].type == type) return true;
            }
        }

        return false;
    }

    private static readonly ulong[] LastPress = new ulong[256];
    private static readonly bool[] HadPress = new bool[256];
    private static readonly int[] LastTargetX = new int[256];
    private static readonly int[] LastTargetY = new int[256];

    private static bool Fresh(Player player, int tileX, int tileY)
    {
        if (!player.tileInteractAttempted) return false;

        var id = player.whoAmI;
        if ((uint)id >= 256u) return false;

        var frame = Main.GameUpdateCount;
        var had = HadPress[id];
        var sameTarget = had && LastTargetX[id] == tileX && LastTargetY[id] == tileY;
        var sameBurst = had && frame - LastPress[id] <= 1 && !player.releaseUseTile;

        LastPress[id] = frame;
        HadPress[id] = true;
        LastTargetX[id] = tileX;
        LastTargetY[id] = tileY;

        return !(sameTarget && sameBurst);
    }

    private void MouseOver(On.Terraria.Player.orig_TileInteractionsMouseOver orig, Player self, int myX, int myY)
    {
        orig(self, myX, myY);

        try
        {
            if (myX != Player.tileTargetX || myY != Player.tileTargetY) return;
            if ((uint)myX >= (uint)Main.maxTilesX || (uint)myY >= (uint)Main.maxTilesY) return;

            var tile = TileTable.At(myX, myY);
            var type = Main.tile[myX, myY].type;

            if (tile is not null && tile.HookEnabled(HookId.MouseOver))
            {
                try
                {
                    tile.MouseOver(myX, myY);
                }
                catch (Exception e)
                {
                    Fail(tile, HookId.MouseOver, e);
                }
            }

            var globals = GlobalTileRegistry.For(HookId.MouseOver);
            for (var index = 0; index < globals.Count; index++)
            {
                try
                {
                    globals[index].MouseOver(myX, myY, type);
                }
                catch (Exception e)
                {
                    Fail(tile, HookId.MouseOver, e);
                }
            }

            if (tile is not null && tile.HookEnabled(HookId.RightClick) && !self.cursorItemIconEnabled)
            {
                var style = (type >= 0 && type < TileObjectData._data.Count) ? (int)Main.tile[myX, myY].frameY / 36 : 0;
                self.noThrow = 2;
                self.cursorItemIconEnabled = true;
                self.cursorItemIconID = TileTable.DropOf(type, style);
            }
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tile] 光标判定异常：" + Safe.Describe(e));
        }
    }

    private void Interact(On.Terraria.Player.orig_TileInteractionsUse orig, Player self, int myX, int myY)
    {
        var tile = TileTable.At(myX, myY);
        var fresh = Fresh(self, myX, myY);

        if (tile is not null && !fresh) return;

        var type = Main.tile[myX, myY].type;
        var right = tile is not null && tile.HookEnabled(HookId.RightClick);

        var chest = right && IsIn(Terraria.ID.TileID.Sets.BasicChest, type) && Terraria.ID.TileID.Sets.BasicChest[type];
        var dresser = right && IsIn(Terraria.ID.TileID.Sets.BasicDresser, type) && Terraria.ID.TileID.Sets.BasicDresser[type];
        var fire = right && IsIn(Terraria.ID.TileID.Sets.Campfires, type) && Terraria.ID.TileID.Sets.Campfires[type];

        if (right)
        {
            Set(Terraria.ID.TileID.Sets.BasicChest, type, false);
            Set(Terraria.ID.TileID.Sets.BasicDresser, type, false);
            Set(Terraria.ID.TileID.Sets.Campfires, type, false);
        }

        try
        {
            orig(self, myX, myY);
        }
        finally
        {
            if (right)
            {
                Set(Terraria.ID.TileID.Sets.BasicChest, type, chest);
                Set(Terraria.ID.TileID.Sets.BasicDresser, type, dresser);
                Set(Terraria.ID.TileID.Sets.Campfires, type, fire);
            }
        }

        if (tile is not null && right)
        {
            try
            {
                if (tile.RightClick(myX, myY)) self.tileInteractionHappened = true;
            }
            catch (Exception e)
            {
                Fail(tile, HookId.RightClick, e);
            }
        }

        var globals = GlobalTileRegistry.For(HookId.RightClick);
        for (var index = 0; index < globals.Count; index++)
        {
            try
            {
                globals[index].RightClick(myX, myY, type);
            }
            catch (Exception e)
            {
                Fail(tile, HookId.RightClick, e);
            }
        }
    }

    #endregion

    private void RandomUpdateOver(On.Terraria.WorldGen.orig_UpdateWorld_OvergroundTile orig,
                                  int i, int j, bool checkNPCSpawns, int wallDist)
    {
        orig(i, j, checkNPCSpawns, wallDist);
        RandomUpdate(i, j, underground: false);
    }

    private void RandomUpdateUnder(On.Terraria.WorldGen.orig_UpdateWorld_UndergroundTile orig,
                                   int i, int j, bool checkNPCSpawns, int wallDist)
    {
        orig(i, j, checkNPCSpawns, wallDist);
        RandomUpdate(i, j, underground: true);
    }

    private void RandomUpdate(int i, int j, bool underground)
    {
        var tiles = Main.tile;
        if (tiles is null || i < 0 || j < 0 || i >= Main.maxTilesX || j >= Main.maxTilesY) return;

        var type = tiles[i, j].type;

        var tile = TileTable.At(i, j);
        if (tile is not null && tile.HookEnabled(HookId.RandomUpdate))
        {
            try { tile.RandomUpdate(i, j); }
            catch (Exception e) { Fail(tile, HookId.RandomUpdate, e); }
        }

        var wall = WallTable.At(i, j);
        if (wall is not null && wall.HookEnabled(HookId.RandomUpdate))
        {
            try { wall.RandomUpdate(i, j); }
            catch (Exception e)
            {
                if (_reported.Add(HashCode.Combine(wall.Type, HookId.RandomUpdate, 1)))
                    ContentErrors.Report($"[wall] {wall.GetType().Name}.RandomUpdate 异常：{Safe.Describe(e)}");
            }
        }

        var globals = GlobalTileRegistry.For(HookId.RandomUpdate);
        for (var g = 0; g < globals.Count; g++)
        {
            try { globals[g].RandomUpdate(i, j, type, underground); }
            catch (Exception e)
            {
                if (_reported.Add(HashCode.Combine(globals[g].GetType(), HookId.RandomUpdate, 2)))
                    ContentErrors.Report($"[tile] {globals[g].GetType().Name}.RandomUpdate 异常：{Safe.Describe(e)}");
            }
        }
    }

    private static bool IsIn(bool[]? set, int type) => set is not null && (uint)type < (uint)set.Length;

    private static void Set(bool[]? set, int type, bool value)
    {
        if (!IsIn(set, type)) return;
        set![type] = value;
    }

    private void Fail(ModTile? tile, HookId hook, Exception exception)
    {
        var type = tile?.Type ?? -1;
        if (!_reported.Add(HashCode.Combine(type, hook))) return;

        var who = tile is null ? "GlobalTile" : tile.GetType().Name;
        ContentErrors.Report($"[tile] {who}.{hook} 异常：{Safe.Describe(exception)}");
    }
}

/// <summary>方块编号相关的原生表（754 长与 TileID.Count 一致；尾部按众数填，避免新方块变实心泥土）。</summary>
internal static class TileTables
{
    private static readonly Type[] Types =
    [
        typeof(Terraria.Main),
        typeof(Terraria.ID.TileID.Sets),
        typeof(Terraria.ID.TileID.Sets.Conversion),
        typeof(Terraria.ID.TileID.Sets.TileCutIgnore),
        typeof(Terraria.ID.TileID.Sets.ForAdvancedCollision),
        typeof(Terraria.ID.TileID.Sets.RoomNeeds),
        typeof(Terraria.ID.TileID.Sets.Wiring),
        typeof(Terraria.GameContent.TextureAssets),
        typeof(Terraria.WorldGen),
        typeof(Terraria.Map.MapHelper),
        typeof(Terraria.Recipe),
        typeof(Terraria.GameContent.Metadata.TileMaterials),
        typeof(Terraria.WorldGen.Skyblock),
        typeof(Terraria.GameContent.Biomes.CorruptionPitBiome),
        typeof(Terraria.GameContent.Biomes.CaveHouse.HouseUtils),
    ];

    internal static readonly TableSet Set = new("方块", () => 754, Types, fillWithMode: true);

}
