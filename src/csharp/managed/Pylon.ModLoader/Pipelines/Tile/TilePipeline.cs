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

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.WorldGen.CanKillTile += CanKill;
        On.Terraria.WorldGen.CanKillTile_1 += CanKillFast;
        On.Terraria.WorldGen.KillTile += Kill;
        On.Terraria.WorldGen.KillTile_MakeTileDust += MakeDust;
        On.Terraria.WorldGen.KillTile_DropItems += DropItems;
        On.Terraria.WorldGen.TileFrame += Frame;
        On.Terraria.Wiring.HitWireSingle += Wire;
        On.Terraria.Player.AdjTiles += Stations;
        On.Terraria.Player.TileInteractionsUse += Interact;

        Log.Info("[tile] 已挂（挖 / 尘土 / 掉落 / 边框 / 通电 / 制作站 / 右键）；逐格绘制与光照本版无入口");
        return true;
    }

    private int _maintained;
    private int _maintainedData;

    /// <summary>安装期建表（编译期上限、位集、掉落）。</summary>
    internal void Build()
    {
        // 游戏的 PlaceTile/PlaceObject/KillTile 等把 type > 753 编进了机器码；不改立即数，
        // mod 方块（>= 754）放不下去（WorldGen.PlaceObject 直接返回 false）。
        LimitPatch.Apply(VanillaCounts.Tile + ModContent.Count(ContentKind.Tile), Log);
        TileTable.Build(Log);
    }

    /// <summary>
    /// 低频维护：游戏重建表或实例新建后把 mod 类型那几格补齐。
    /// 都是幂等写入，按类型数变化做门控，不在每帧重复扫。
    /// </summary>
    internal void Maintain()
    {
        var total = TileTable.Count;
        if (total <= 0) return;

        try
        {
            if (_maintained != total)
            {
                _maintained = total;

                // 没填 -1 的 mod 类型会去读原版的遮罩贴图（越界或错图）。
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

            // 空槽必须是 1×1 而不是 null：原版放方块时直接解引用 GetTileData 的返回值。
            var data = TileObjectData._data;
            if (data is null || data.Count >= total) return;
            if (_maintainedData == total) return;

            _maintainedData = total;
            while (data.Count < total) data.Add(TileObjectData.Style1x1);
        }
        catch (Exception e)
        {
            Log.Warn("[tile] 维护失败：" + Safe.Describe(e));
        }
    }

    #region 挖

    private bool CanKill(On.Terraria.WorldGen.orig_CanKillTile orig, int i, int j,
                         WorldGen.SpecialKillTileContext context)
    {
        var tile = TileTable.At(i, j);
        if (tile is null || !tile.HookEnabled(HookId.CanKillTile)) return orig(i, j, context);

        // blockDamaged 是本入口拿不到的（游戏在别处维护），作者写它不会回传。
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

    private bool CanKillFast(On.Terraria.WorldGen.orig_CanKillTile_1 orig, int i, int j)
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

    private void Kill(On.Terraria.WorldGen.orig_KillTile orig, int i, int j, bool fail, bool effectOnly, bool noItem)
    {
        var tile = TileTable.At(i, j);

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

        // 多格家具只在左上角发一次：这里已经能拿到帧坐标，比在掉落钩子里猜靠得住。
        if (tile is null || fail || !tile.HookEnabled(HookId.KillMultiTile)) return;

        var cache = Main.tile[i, j];

        try
        {
            tile.KillMultiTile(i, j, cache.frameX, cache.frameY);
        }
        catch (Exception e)
        {
            Fail(tile, HookId.KillMultiTile, e);
        }
    }

    #endregion

    #region 尘土与掉落

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
                // 不让掉：只走原版那半（大物体的连带掉落），本体物品不发。
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

    /// <summary>登记表里的默认掉落：游戏自己不知道该掉什么，所以在这里补一次。</summary>
    private void DropDefault(Tile tileCache, int i, int j)
    {
        var item = TileTable.DropOf(tileCache.type, StyleOf(tileCache));
        if (item > 0) Item.NewItem(i * 16, j * 16, 16, 16, item);
    }

    /// <summary>这块是第几个样式：多格家具按帧坐标折，普通方块就是 0。</summary>
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

    /// <summary>原版算完之后，把作者声明的"也算作某制作站"贴到玩家身上。</summary>
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

    /// <summary>这块在不在这位玩家身边（原版用 5×5 的邻域判定制作站）。</summary>
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

    private void Interact(On.Terraria.Player.orig_TileInteractionsUse orig, Player self, int myX, int myY)
    {
        var i = Player.tileTargetX;
        var j = Player.tileTargetY;
        var tile = TileTable.At(i, j);

        if (tile is not null && tile.HookEnabled(HookId.RightClick))
        {
            try
            {
                if (tile.RightClick(i, j)) return;
            }
            catch (Exception e)
            {
                Fail(tile, HookId.RightClick, e);
            }
        }

        orig(self, myX, myY);
    }

    #endregion

    /// <summary>同一 (类型, 钩子) 只记一条：这些入口都在高频路径上。</summary>
    private void Fail(ModTile tile, HookId hook, Exception exception)
    {
        if (!_reported.Add(HashCode.Combine(tile.Type, hook))) return;
        ContentErrors.Report($"[tile] {tile.GetType().Name}.{hook} 异常：{Safe.Describe(exception)}");
    }
}
