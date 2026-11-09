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
        On.Terraria.WorldGen.CanKillTile += CanKill;
        On.Terraria.WorldGen.CanKillTile_1 += CanKillFast;
        On.Terraria.WorldGen.CanKillTile_2 += CanKillOut;
        On.Terraria.WorldGen.KillTile += Kill;
        On.Terraria.WorldGen.KillTile_MakeTileDust += MakeDust;
        On.Terraria.WorldGen.KillTile_DropItems += DropItems;
        On.Terraria.WorldGen.TileFrame += Frame;
        On.Terraria.Wiring.HitWireSingle += Wire;
        On.Terraria.Player.AdjTiles += Stations;
        On.Terraria.Player.TileInteractionsUse += Interact;
        On.Terraria.Player.TileInteractionsMouseOver += MouseOver;

        Log.Info("[tile] 已挂（挖 / 尘土 / 掉落 / 边框 / 通电 / 制作站 / 右键 / 光标）；逐格绘制与光照本版无入口");
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

    /// <summary>正在替多格家具清残留格：防止 WorldGen.KillTile 重入时再清一遍。</summary>
    private static bool _removingMulti;

    /// <summary><c>CanKillTile(int, int, out bool)</c> 是镐子/公开调用走的那条重载；前两条接不到它。</summary>
    private bool CanKillOut(On.Terraria.WorldGen.orig_CanKillTile_2 orig, int i, int j, ref bool blockDamaged)
    {
        var tile = TileTable.At(i, j);
        if (tile is null || !tile.HookEnabled(HookId.CanKillTile))
            return orig(i, j, ref blockDamaged);

        var damaged = false;
        try
        {
            var allowed = tile.CanKillTile(i, j, ref damaged);
            var originalDamaged = false;
            var result = orig(i, j, ref originalDamaged);
            blockDamaged = damaged || originalDamaged;
            return allowed && result;
        }
        catch (Exception e)
        {
            Fail(tile, HookId.CanKillTile, e);
            return orig(i, j, ref blockDamaged);
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

        // 这版游戏挖多格家具只删目标格；挖掉任意一格就替作者清掉其余格，再发一次 KillMultiTile。
        // 残留格用 noItem=true 清掉，避免本体物品掉多份。
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
        // 多格家具的残留格是替作者清的：即便 noItem 没挡住这个入口，也不能再掉一份。
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

    /// <summary>上一次算"新按下"的帧号与是否按下过（按玩家，最多 256 人）。</summary>
    private static readonly ulong[] LastPress = new ulong[256];
    private static readonly bool[] HadPress = new bool[256];

    /// <summary>
    /// 这一次交互是不是"新按下"：手机端按住会每帧走一次 TileInteractionsUse，
    /// 不加这道闸，晶塔会每帧重开一次地图 —— 表现就是"点了传送之后地图关不掉"。
    /// </summary>
    private static bool Fresh(Player player)
    {
        if (!player.tileInteractAttempted) return false;

        var id = player.whoAmI;
        if ((uint)id >= 256u) return false;

        var frame = Main.GameUpdateCount;
        var last = LastPress[id];
        var had = HadPress[id];

        LastPress[id] = frame;
        HadPress[id] = true;

        return (!had || last != frame) && (player.releaseUseTile || !had || frame - last > 1);
    }

    /// <summary>
    /// 手指/光标停在哪一格：作者的 MouseOver，以及"这一格能交互"的光标图标。
    /// 后者不只是画个图标——手机端的交互目标就是靠它认的，没有它，点 mod 方块根本走不进
    /// <see cref="Interact"/>（点晶塔没反应就是这个原因），与参考实现一致。
    /// </summary>
    private void MouseOver(On.Terraria.Player.orig_TileInteractionsMouseOver orig, Player self, int myX, int myY)
    {
        orig(self, myX, myY);

        try
        {
            // 只有游戏自己认定的目标格才派作者钩子：屏幕上扫过的每一格都派会白跑一堆回调。
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

            // 可右键的 mod 方块要自己撑起光标图标；否则游戏当它是普通方块，手机点上去等于没用。
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
        var fresh = Fresh(self);

        // mod 方块：不是新按下就整条交互跳过（orig 也不用跑，否则原版会再处理一遍这块）。
        if (tile is not null && !fresh) return;

        var type = Main.tile[myX, myY].type;
        var right = tile is not null && tile.HookEnabled(HookId.RightClick);

        // 原版会按"是不是箱子/梳妆台/篝火"给这次交互分派不同分支；mod 方块就算复用了这些集合的
        // 形状，也不是原版那种容器，先临时摘掉，跑完再放回去。
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

        // 原版跑完再派作者的处理：作者要的是"游戏已经把这一格该做的事做完了"这个时机，
        // 而它返回 true 表示这次按下已经被吃掉，要告诉游戏一声，否则同一次按下会被再消费一次。
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

    private static bool IsIn(bool[]? set, int type) => set is not null && (uint)type < (uint)set.Length;

    private static void Set(bool[]? set, int type, bool value)
    {
        if (!IsIn(set, type)) return;
        set![type] = value;
    }

    /// <summary>同一 (类型, 钩子) 只记一条：这些入口都在高频路径上。</summary>
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
