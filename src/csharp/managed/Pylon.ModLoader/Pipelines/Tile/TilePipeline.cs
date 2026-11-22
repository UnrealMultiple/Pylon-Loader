using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Patching;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.Tables;
using Terraria;
using Terraria.ID;
using Terraria.ObjectData;

namespace Pylon.ModLoader.Pipelines;
/// <summary>方块管线：挖、尘土、掉落、边框、通电、制作站、右键；每类型的位集与掉落数据在 TileTable（密集 struct 数组）。</summary>
internal sealed class TilePipeline : ILoaderModule
{
    public static TilePipeline Instance { get; } = new();

    private readonly HashSet<int> _reported = [];

    public string Name => "tile";

    public IReadOnlyList<TableRequest> Tables =>
        [new(TileTables.Set, () => ModContent.NextType(ContentKind.Tile), early: true)];


    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>tile 管线的安装门：条件与订阅同居一门，由 GateHub 自动发现与派发。</summary>
    internal static class Gates
    {
        internal struct Merge : IGate
        {
            public static bool Wanted() => ModContent.GetContent<ModTile>().Count > 0;

            public static void Install()
            {
                TileMergeFix.Install();
                Log.Info("[tile] 已挂（mod 方块内容：编号 / 挖 / 边框 / 制作站等组按覆写分装）");
            }
        }

        internal struct Kill : IGate
        {
            public static bool Wanted() => KillWanted();

            public static void Install()
            {
                On.Terraria.WorldGen.CanKillTile_int_int_SpecialKillTileContext += Instance.CanKill;
                On.Terraria.WorldGen.CanKillTile_int_int += Instance.CanKillFast;
                On.Terraria.WorldGen.CanKillTile_int_int_boolRef += Instance.CanKillOut;
                On.Terraria.WorldGen.KillTile += Instance.Kill;
                On.Terraria.WorldGen.KillTile_PlaySounds += Instance.PlaySounds;
                On.Terraria.WorldGen.KillTile_MakeTileDust += Instance.MakeDust;
                On.Terraria.WorldGen.KillTile_DropItems += Instance.DropItems;
                On.Terraria.WorldGen.KillTile_GetItemDrops += Instance.GetItemDrops;
            }
        }

        internal struct Frame : IGate
        {
            public static bool Wanted() =>
                WantsTile(HookId.TileFrame) || GlobalTileRegistry.AnyWith(HookId.TileFrame);

            public static void Install() => On.Terraria.WorldGen.TileFrame += Instance.Frame;
        }

        internal struct FrameImportant : IGate
        {
            public static bool Wanted() => ModContent.GetContent<ModTile>().Count > 0;

            public static void Install() => On.Terraria.WorldGen.TileFrameImportant += Instance.FrameImportant;
        }

        internal struct Random : IGate
        {
            public static bool Wanted() =>
                HookGuard.Wants<ModTile>(HookId.RandomUpdate) || HookGuard.Wants<ModWall>(HookId.RandomUpdate) ||
                GlobalTileRegistry.AnyWith(HookId.RandomUpdate);

            public static void Install()
            {
                On.Terraria.WorldGen.UpdateWorld_OvergroundTile += Instance.RandomUpdateOver;
                On.Terraria.WorldGen.UpdateWorld_UndergroundTile += Instance.RandomUpdateUnder;
            }
        }

        internal struct Wire : IGate
        {
            public static bool Wanted() =>
                WantsTile(HookId.HitWire) || GlobalTileRegistry.AnyWith(HookId.PreHitWire, HookId.HitWire) ||
                HookGuard.Wants<ModNpc>(HookId.CanGoToStatue, HookId.OnGoToStatue) ||
                GlobalNpcRegistry.AnyWith(HookId.CanGoToStatue, HookId.OnGoToStatue) ||
                MusicPipeline.Instance.HasBoxes;

            public static void Install() => On.Terraria.Wiring.HitWireSingle += Instance.Wire;
        }

        internal struct Adj : IGate
        {
            public static bool Wanted() => WantsTile(HookId.AdjTiles) || GlobalTileRegistry.AnyWith(HookId.AdjTiles);

            public static void Install() => On.Terraria.Player.AdjTiles += Instance.Stations;
        }

        internal struct Pick : IGate
        {
            public static bool Wanted() => ModContent.GetContent<ModTile>().Count > 0;

            public static void Install() => On.Terraria.Player.GetPickaxeDamage += PickaxeDamage;
        }

        internal struct Click : IGate
        {
            public static bool Wanted() =>
                WantsTile(HookId.RightClick, HookId.HasSmartInteract) ||
                GlobalTileRegistry.AnyWith(HookId.RightClick, HookId.HasSmartInteract) ||
                MusicPipeline.Instance.HasBoxes;

            public static void Install() => On.Terraria.Player.TileInteractionsUse += Instance.Interact;
        }

        internal struct Mouse : IGate
        {
            public static bool Wanted() =>
                WantsTile(HookId.MouseOver, HookId.MouseOverFar, HookId.RightClick) ||
                GlobalTileRegistry.AnyWith(HookId.MouseOver, HookId.MouseOverFar, HookId.RightClick);

            public static void Install()
            {
                On.Terraria.Player.TileInteractionsMouseOver += Instance.MouseOver;
                On.Terraria.Player.TileInteractionsCheckLongDistance += Instance.MouseOverFar;
            }
        }

        internal struct Place : IGate
        {
            public static bool Wanted() =>
                WantsTile(HookId.CanPlace, HookId.CanExplode, HookId.CanReplace) ||
                GlobalTileRegistry.AnyWith(HookId.CanPlace, HookId.CanExplode, HookId.CanReplace);

            public static void Install()
            {
                On.Terraria.Player.PlaceThing_Tiles_BlockPlacementForAssortedThings += Instance.CanPlace;
                On.Terraria.Projectile.CanExplodeTile += Instance.Explode;
                On.Terraria.WorldGen.ReplaceTile += Instance.Replace;
            }
        }

        internal struct Danger : IGate
        {
            public static bool Wanted() =>
                WantsTile(HookId.IsTileDangerous, HookId.IsTileSpelunkable) ||
                GlobalTileRegistry.AnyWith(HookId.IsTileDangerous, HookId.IsTileSpelunkable);

            public static void Install()
            {
                On.Terraria.GameContent.Drawing.TileDrawing.IsTileDangerous += Instance.Danger;
                On.Terraria.Main.IsTileSpelunkable_ushort_short_short += Instance.Spelunk;
            }
        }

        internal struct Effects : IGate
        {
            public static bool Wanted() =>
                WantsTile(HookId.NearbyEffects) || GlobalTileRegistry.AnyWith(HookId.NearbyEffects);

            public static void Install() => TileEffectsHooks.Install();
        }

        internal struct Light : IGate
        {
            public static bool Wanted() =>
                WantsTile(HookId.ModifyLight) || GlobalTileRegistry.AnyWith(HookId.ModifyLight);

            public static void Install() => TileLightHooks.Install();
        }

        internal struct Interact : IGate
        {
            public static bool Wanted() =>
                ModContent.GetContent<ModTile>().Count > 0 || GlobalTileRegistry.All.Count > 0;

            public static void Install() => TileInteractionHooks.Install();
        }
    }

    public bool Install() => GateHub<TilePipeline>.Run();

    private static bool WantsTile(params ReadOnlySpan<HookId> hooks) => HookGuard.Wants<ModTile>(hooks);

    private static bool KillWanted() =>
        ModContent.GetContent<ModTile>().Count > 0 ||
        GlobalTileRegistry.AnyWith(HookId.CanKillTile, HookId.KillTile, HookId.NumDust, HookId.CreateDust,
            HookId.KillSound, HookId.CanDrop, HookId.Drop);

    private int _maintained;

    internal void Build()
    {
        LimitPatch.Apply(VanillaCounts.Tile + ModContent.Count(ContentKind.Tile), Log);
        TileTable.Build(Log);
        TileIdTml.EnsurePylonHook();
        TileMergeFix.FixToTotal();
        TileSavePipeline.Build(Log);
        TileLists.Build(Log);
        ModTileMap.Build(Log);
        TileDrawHooks.Build(Log);
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
                while (data.Count < total) data.Add(null);

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
        var type = Main.tile[i, j].type;
        var damaged = false;

        if (tile is not null && tile.HookEnabled(HookId.CanKillTile))
        {
            try
            {
                if (!tile.CanKillTile(i, j, ref damaged)) return false;
            }
            catch (Exception e)
            {
                Fail(tile, HookId.CanKillTile, e);
            }
        }

        var globals = GlobalTileRegistry.For(HookId.CanKillTile);
        for (var index = 0; index < globals.Count; index++)
        {
            try
            {
                if (!globals[index].CanKillTile(i, j, type, ref damaged)) return false;
            }
            catch (Exception e)
            {
                Fail(null, HookId.CanKillTile, e);
            }
        }

        return orig(i, j, context);
    }

    private bool CanKillFast(On.Terraria.WorldGen.orig_CanKillTile_int_int orig, int i, int j)
    {
        var tile = TileTable.At(i, j);
        var type = Main.tile[i, j].type;
        var damaged = false;

        if (tile is not null && tile.HookEnabled(HookId.CanKillTile))
        {
            try
            {
                if (!tile.CanKillTile(i, j, ref damaged)) return false;
            }
            catch (Exception e)
            {
                Fail(tile, HookId.CanKillTile, e);
            }
        }

        var globals = GlobalTileRegistry.For(HookId.CanKillTile);
        for (var index = 0; index < globals.Count; index++)
        {
            try
            {
                if (!globals[index].CanKillTile(i, j, type, ref damaged)) return false;
            }
            catch (Exception e)
            {
                Fail(null, HookId.CanKillTile, e);
            }
        }

        return orig(i, j);
    }

    private bool CanKillOut(On.Terraria.WorldGen.orig_CanKillTile_int_int_boolRef orig, int i, int j, out bool blockDamaged)
    {
        var tile = TileTable.At(i, j);
        var type = Main.tile[i, j].type;
        var damaged = false;

        if (tile is not null && tile.HookEnabled(HookId.CanKillTile))
        {
            try
            {
                if (!tile.CanKillTile(i, j, ref damaged))
                {
                    blockDamaged = damaged;
                    return false;
                }
            }
            catch (Exception e)
            {
                Fail(tile, HookId.CanKillTile, e);
            }
        }

        var globals = GlobalTileRegistry.For(HookId.CanKillTile);
        for (var index = 0; index < globals.Count; index++)
        {
            try
            {
                if (!globals[index].CanKillTile(i, j, type, ref damaged))
                {
                    blockDamaged = damaged;
                    return false;
                }
            }
            catch (Exception e)
            {
                Fail(null, HookId.CanKillTile, e);
            }
        }

        var result = orig(i, j, out var originalDamaged);
        blockDamaged = damaged || originalDamaged;
        return result;
    }

    [ThreadStatic] private static int _dustBudget;
    [ThreadStatic] private static int _killing;

    private void Kill(On.Terraria.WorldGen.orig_KillTile orig, int i, int j, bool fail, bool effectOnly, bool noItem)
    {
        var tile = TileTable.At(i, j);
        var type = Main.tile[i, j].type;

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

        var globals = GlobalTileRegistry.For(HookId.KillTile);
        for (var index = 0; index < globals.Count; index++)
        {
            try
            {
                globals[index].KillTile(i, j, type, ref fail, ref effectOnly, ref noItem);
            }
            catch (Exception e)
            {
                Fail(null, HookId.KillTile, e);
            }
        }

        var count = fail ? 3 : 10;
        if (tile is not null && tile.HookEnabled(HookId.NumDust))
        {
            try
            {
                tile.NumDust(i, j, fail, ref count);
            }
            catch (Exception e)
            {
                Fail(tile, HookId.NumDust, e);
            }
        }

        var dustGlobals = GlobalTileRegistry.For(HookId.NumDust);
        for (var index = 0; index < dustGlobals.Count; index++)
        {
            try
            {
                dustGlobals[index].NumDust(i, j, type, fail, ref count);
            }
            catch (Exception e)
            {
                Fail(null, HookId.NumDust, e);
            }
        }

        var previousBudget = _dustBudget;
        var previousKilling = _killing;
        _dustBudget = Math.Max(0, count);
        _killing++;

        try
        {
            orig(i, j, fail, effectOnly, noItem);
        }
        finally
        {
            _dustBudget = previousBudget;
            _killing = previousKilling;
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

            if (sound >= 0) Terraria.Audio.SoundEngine.PlaySound(sound, i * 16, j * 16, 1, 1f, 0f);
            return;
        }

        orig(i, j, fail, tileCache);
    }

    private int MakeDust(On.Terraria.WorldGen.orig_KillTile_MakeTileDust orig, int i, int j, Tile tileCache)
    {
        if (_killing > 0 && _dustBudget <= 0) return 6000;
        if (_killing > 0) _dustBudget--;

        var type = tileCache.type;
        var tile = TileTable.Of(type);

        if (tile is null)
        {
            var index = orig(i, j, tileCache);
            if ((uint)index >= 6000u) return index;

            var d = Main.dust[index];
            var dust = d.type;
            var globals = GlobalTileRegistry.For(HookId.CreateDust);
            for (var k = 0; k < globals.Count; k++)
            {
                try
                {
                    if (!globals[k].CreateDust(i, j, type, ref dust))
                    {
                        d.active = false;
                        return 6000;
                    }
                }
                catch (Exception e)
                {
                    Fail(null, HookId.CreateDust, e);
                }
            }

            d.type = dust;
            return index;
        }

        var kind = tile.DustType;
        try
        {
            if (!tile.CreateDust(i, j, ref kind)) return 6000;
        }
        catch (Exception e)
        {
            Fail(tile, HookId.CreateDust, e);
            return 6000;
        }

        var dustGlobals = GlobalTileRegistry.For(HookId.CreateDust);
        for (var k = 0; k < dustGlobals.Count; k++)
        {
            try
            {
                if (!dustGlobals[k].CreateDust(i, j, type, ref kind)) return 6000;
            }
            catch (Exception e)
            {
                Fail(null, HookId.CreateDust, e);
            }
        }

        return Terraria.Dust.NewDust(new Il2CppMicrosoft.Xna.Framework.Vector2(i * 16, j * 16), 16, 16, kind, 0, 0, 0, default, 1);
    }

    private void DropItems(On.Terraria.WorldGen.orig_KillTile_DropItems orig, int x, int y, Tile tileCache,
                           bool includeLargeObjectDrops)
    {
        var type = tileCache.type;
        var tile = TileTable.Of(type);
        if (tile is null)
        {
            orig(x, y, tileCache, includeLargeObjectDrops);
            return;
        }

        if (!AllowDrop(x, y, type, tile)) return;
        if (!includeLargeObjectDrops)
        {
            var shape = Terraria.ObjectData.TileObjectData.GetTileData(type, 0);
            if (shape is not null && shape.Width * shape.Height > 1) return;
        }

        try
        {
            var drops = tile.GetItemDrops(x, y);
            if (drops is not null && drops.Length > 0)
            {
                for (var k = 0; k < drops.Length; k++)
                {
                    if (drops[k] > 0) Item.NewItem(x * 16, y * 16, 16, 16, drops[k]);
                }
            }
            else
            {
                var item = TileTable.DropOf(type, StyleOf(tileCache));
                if (item > 0) Item.NewItem(x * 16, y * 16, 16, 16, item);
            }
        }
        catch (Exception e)
        {
            Fail(tile, HookId.GetItemDrops, e);
        }

        var globals = GlobalTileRegistry.For(HookId.Drop);
        for (var k = 0; k < globals.Count; k++)
        {
            try
            {
                globals[k].Drop(x, y, type);
            }
            catch (Exception e)
            {
                Fail(null, HookId.Drop, e);
            }
        }
    }

    private bool AllowDrop(int x, int y, int type, ModTile tile)
    {
        if (tile.HookEnabled(HookId.CanDrop))
        {
            try
            {
                if (!tile.CanDrop(x, y)) return false;
            }
            catch (Exception e)
            {
                Fail(tile, HookId.CanDrop, e);
            }
        }

        var globals = GlobalTileRegistry.For(HookId.CanDrop);
        for (var k = 0; k < globals.Count; k++)
        {
            try
            {
                if (!globals[k].CanDrop(x, y, type)) return false;
            }
            catch (Exception e)
            {
                Fail(null, HookId.CanDrop, e);
            }
        }

        return true;
    }

    private void GetItemDrops(On.Terraria.WorldGen.orig_KillTile_GetItemDrops orig, int x, int y, Tile tileCache,
                              out int dropItem, out int dropItemStack, out int secondaryItem, out int secondaryItemStack,
                              out bool noPrefix, bool includeLargeObjectDrops)
    {
        orig(x, y, tileCache, out dropItem, out dropItemStack, out secondaryItem, out secondaryItemStack, out noPrefix,
             includeLargeObjectDrops);

        var type = tileCache.type;
        var tile = TileTable.Of(type);
        if (tile is null) return;

        if (!AllowDrop(x, y, type, tile))
        {
            dropItem = secondaryItem = 0;
            return;
        }

        dropItem = secondaryItem = 0;
        dropItemStack = secondaryItemStack = 1;
        if (!includeLargeObjectDrops)
        {
            var shape = Terraria.ObjectData.TileObjectData.GetTileData(type, 0);
            if (shape is not null && shape.Width * shape.Height > 1) return;
        }

        try
        {
            var drops = tile.GetItemDrops(x, y);
            if (drops is not null && drops.Length > 0)
            {
                if (drops[0] > 0) dropItem = drops[0];
                if (drops.Length > 1 && drops[1] > 0) secondaryItem = drops[1];
            }
            else
            {
                var item = TileTable.DropOf(type, StyleOf(tileCache));
                if (item > 0) dropItem = item;
            }
        }
        catch (Exception e)
        {
            Fail(tile, HookId.GetItemDrops, e);
        }
    }

    internal static int StyleOf(Tile tile)
    {
        var data = Terraria.ObjectData.TileObjectData.GetTileData(tile.type, 0);
        if (data is null) return 0;

        var x = tile.frameX / Math.Max(1, data.CoordinateFullWidth);
        var y = tile.frameY / Math.Max(1, data.CoordinateFullHeight);

        var style = data.StyleHorizontal
            ? y / Math.Max(1, data.StyleLineSkip) * Math.Max(1, data.StyleWrapLimit) + x
            : x / Math.Max(1, data.StyleLineSkip) * Math.Max(1, data.StyleWrapLimit) + y;
        return style / Math.Max(1, data.StyleMultiplier);
    }

    #endregion

    #region 边框、通电、制作站、右键

    private void Frame(On.Terraria.WorldGen.orig_TileFrame orig, int i, int j, bool resetFrame, bool noBreak)
    {
        var tile = TileTable.At(i, j);
        var result = true;

        if (tile is not null && tile.HookEnabled(HookId.TileFrame))
        {
            try
            {
                result = tile.TileFrame(i, j, ref resetFrame, ref noBreak);
            }
            catch (Exception e)
            {
                Fail(tile, HookId.TileFrame, e);
            }
        }

        var type = Main.tile[i, j].type;
        var globals = GlobalTileRegistry.For(HookId.TileFrame);
        for (var index = 0; index < globals.Count; index++)
        {
            try
            {
                result &= globals[index].TileFrame(i, j, type, ref resetFrame, ref noBreak);
            }
            catch (Exception e)
            {
                Fail(null, HookId.TileFrame, e);
            }
        }

        if (!result) return;

        orig(i, j, resetFrame, noBreak);
    }

    private void FrameImportant(On.Terraria.WorldGen.orig_TileFrameImportant orig, int i, int j,
                                int type, Tile tileCache, bool resetFrame)
    {
        if ((uint)type >= (uint)VanillaCounts.Tile && TileTable.Has(type) && TileObjectCheck.Check(i, j, type))
            return;

        orig(i, j, type, tileCache, resetFrame);
    }

    [ThreadStatic] private static int _wireDepth;
    [ThreadStatic] private static int _wireX;
    [ThreadStatic] private static int _wireY;
    private static readonly HashSet<int> _wireReported = [];

    /// <summary>电线作用域（基线 TileHooks.WireScope 同款）：记下本次信号点 (i,j)，同点重入直接放行不重复派发，不同点是新的独立作用域；释放时（owner）才在原版信号处理之后派发 mod 方块与全局的 HitWire。</summary>
    internal readonly struct WireScope : IDisposable
    {
        internal readonly bool Allowed;
        private readonly bool _owner;
        private readonly int _x, _y, _type, _prevDepth, _prevX, _prevY;

        internal WireScope(int i, int j)
        {
            if (_wireDepth > 0 && _wireX == i && _wireY == j)
            {
                Allowed = true;
                return;
            }

            var tile = Main.tile[i, j];
            _type = tile.type;
            foreach (var global in GlobalTileRegistry.For(HookId.PreHitWire))
            {
                try
                {
                    if (!global.PreHitWire(i, j, _type)) return;
                }
                catch (Exception e)
                {
                    if (_wireReported.Add(HashCode.Combine(global.GetType(), HookId.PreHitWire)))
                        ContentErrors.Report($"[tile] {global.Name}.PreHitWire 异常：{Safe.Describe(e)}");
                }
            }

            Allowed = true;
            _owner = true;
            _x = i;
            _y = j;
            _prevDepth = _wireDepth;
            _prevX = _wireX;
            _prevY = _wireY;
            _wireDepth++;
            _wireX = i;
            _wireY = j;
        }

        public void Dispose()
        {
            if (!_owner) return;
            try
            {
                var tile = TileTable.At(_x, _y);
                if (tile is not null && tile.HookEnabled(HookId.HitWire))
                {
                    try
                    {
                        tile.HitWire(_x, _y);
                    }
                    catch (Exception e)
                    {
                        if (_wireReported.Add(HashCode.Combine(tile.Type, HookId.HitWire)))
                            ContentErrors.Report($"[tile] {tile.Name}.HitWire 异常：{Safe.Describe(e)}");
                    }
                }
                foreach (var global in GlobalTileRegistry.For(HookId.HitWire))
                {
                    try
                    {
                        global.HitWire(_x, _y, _type);
                    }
                    catch (Exception e)
                    {
                        if (_wireReported.Add(HashCode.Combine(global.GetType(), HookId.HitWire)))
                            ContentErrors.Report($"[tile] {global.Name}.HitWire 异常：{Safe.Describe(e)}");
                    }
                }
            }
            finally
            {
                _wireDepth = _prevDepth;
                _wireX = _prevX;
                _wireY = _prevY;
            }
        }
    }

    private void Wire(On.Terraria.Wiring.orig_HitWireSingle orig, int i, int j)
    {
        using var scope = new WireScope(i, j);
        if (!scope.Allowed) return;

        if (NpcStatueWire.TryTrigger(i, j)) return;

        orig(i, j);

        var tile = TileTable.Of(Main.tile[i, j].type);
        if (tile is not null && !tile.HookEnabled(HookId.HitWire) && MusicPipeline.Instance.IsBoxTile(tile.Type))
            ToggleMusicBox(i, j, wired: true);
    }

    private static void ToggleMusicBox(int i, int j, bool wired)
    {
        var origin = Main.tile[i, j];
        var type = origin.type;
        var left = i - origin.frameX / 18 % 2;
        var top = j - origin.frameY / 18 % 2;
        for (var x = left; x < left + 2; x++)
        {
            for (var y = top; y < top + 2; y++)
            {
                var cell = Main.tile[x, y];
                if (!cell.active() || cell.type != type) continue;
                cell.frameX = (short)(cell.frameX < 36 ? cell.frameX + 36 : cell.frameX - 36);
                if (wired) Wiring.SkipWire(x, y);
            }
        }
        if (Main.netMode != 0) NetMessage.SendTileSquare(-1, left, top, 2, 2);
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
            if (tile is null) continue;

            var type = tile.Type;
            var extras = new List<int>();

            if (tile.HookEnabled(HookId.AdjTiles) || GlobalTileRegistry.For(HookId.AdjTiles).Count > 0)
            {
                if (NearBy(self, tiles, type))
                {
                    if (tile.HookEnabled(HookId.AdjTiles))
                    {
                        var own = tile.AdjTiles;
                        if (own is not null) extras.AddRange(own);
                    }

                    var globals = GlobalTileRegistry.For(HookId.AdjTiles);
                    for (var g = 0; g < globals.Count; g++)
                    {
                        try
                        {
                            extras.AddRange(globals[g].AdjTiles(type));
                        }
                        catch (Exception e)
                        {
                            Fail(null, HookId.AdjTiles, e);
                        }
                    }
                }
            }

            for (var k = 0; k < extras.Count; k++)
            {
                var station = extras[k];
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
                self.noThrow = 2;
                self.cursorItemIconEnabled = true;
                self.cursorItemIconID = TileTable.DropOf(type, StyleOf(Main.tile[myX, myY]));
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
        else if (tile is not null && MusicPipeline.Instance.IsBoxTile(type))
        {
            ToggleMusicBox(myX, myY, wired: false);
            self.tileInteractionHappened = true;
        }

        if (!fresh) return;

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
            try { tile.RandomUpdate(i, j, underground); }
            catch (Exception e) { Fail(tile, HookId.RandomUpdate, e); }
        }

        var wall = WallTable.At(i, j);
        if (wall is not null && wall.HookEnabled(HookId.RandomUpdate))
        {
            try { wall.RandomUpdate(i, j, underground); }
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

    private static int PickaxeDamage(On.Terraria.Player.orig_GetPickaxeDamage orig, Player player,
                                     int i, int j, int power, int buffer, Tile tile)
    {
        var damage = orig(player, i, j, power, buffer, tile);

        var type = tile.type;
        if (!TileTable.Has(type)) return damage;

        ref var meta = ref TileTable.Meta(type);
        if (power < meta.MinPick) return 0;
        return meta.MineResist > 0f ? (int)(damage / meta.MineResist) : damage;
    }

    private void MouseOverFar(On.Terraria.Player.orig_TileInteractionsCheckLongDistance orig, Player self, int i, int j)
    {
        orig(self, i, j);

        try
        {
            if ((uint)i >= (uint)Main.maxTilesX || (uint)j >= (uint)Main.maxTilesY) return;

            var tile = TileTable.At(i, j);
            var type = Main.tile[i, j].type;

            if (tile is not null && tile.HookEnabled(HookId.MouseOverFar))
            {
                try
                {
                    tile.MouseOverFar(i, j);
                }
                catch (Exception e)
                {
                    Fail(tile, HookId.MouseOverFar, e);
                }
            }

            var globals = GlobalTileRegistry.For(HookId.MouseOverFar);
            for (var index = 0; index < globals.Count; index++)
            {
                try
                {
                    globals[index].MouseOverFar(i, j, type);
                }
                catch (Exception e)
                {
                    Fail(tile, HookId.MouseOverFar, e);
                }
            }
        }
        catch (Exception e)
        {
            ContentErrors.Report("[tile] 远距光标判定异常：" + Safe.Describe(e));
        }
    }

    private bool CanPlace(On.Terraria.Player.orig_PlaceThing_Tiles_BlockPlacementForAssortedThings orig,
                          Player player, bool canPlace)
    {
        if (!orig(player, canPlace)) return false;

        var type = player.HeldItem.createTile;
        if (type < 0) return true;

        var i = Player.tileTargetX;
        var j = Player.tileTargetY;
        var tile = TileTable.Of(type);

        if (tile is not null && tile.HookEnabled(HookId.CanPlace))
        {
            try
            {
                if (!tile.CanPlace(i, j)) return false;
            }
            catch (Exception e)
            {
                Fail(tile, HookId.CanPlace, e);
            }
        }

        var globals = GlobalTileRegistry.For(HookId.CanPlace);
        for (var index = 0; index < globals.Count; index++)
        {
            try
            {
                if (!globals[index].CanPlace(i, j, type)) return false;
            }
            catch (Exception e)
            {
                Fail(null, HookId.CanPlace, e);
            }
        }

        return true;
    }

    private bool Explode(On.Terraria.Projectile.orig_CanExplodeTile orig, Projectile projectile, int i, int j, bool ores)
    {
        if (!orig(projectile, i, j, ores)) return false;

        var type = Main.tile[i, j].type;
        var tile = TileTable.Of(type);

        if (tile is not null && tile.HookEnabled(HookId.CanExplode))
        {
            try
            {
                if (!tile.CanExplode(i, j)) return false;
            }
            catch (Exception e)
            {
                Fail(tile, HookId.CanExplode, e);
            }
        }

        var globals = GlobalTileRegistry.For(HookId.CanExplode);
        for (var index = 0; index < globals.Count; index++)
        {
            try
            {
                if (!globals[index].CanExplode(i, j, type)) return false;
            }
            catch (Exception e)
            {
                Fail(null, HookId.CanExplode, e);
            }
        }

        return true;
    }

    private bool Replace(On.Terraria.WorldGen.orig_ReplaceTile orig, int i, int j, int target, int style)
    {
        var type = Main.tile[i, j].type;
        var tile = TileTable.Of(type);

        if (tile is not null && tile.HookEnabled(HookId.CanReplace))
        {
            try
            {
                if (!tile.CanReplace(i, j, target)) return false;
            }
            catch (Exception e)
            {
                Fail(tile, HookId.CanReplace, e);
            }
        }

        var globals = GlobalTileRegistry.For(HookId.CanReplace);
        for (var index = 0; index < globals.Count; index++)
        {
            try
            {
                if (!globals[index].CanReplace(i, j, type, target)) return false;
            }
            catch (Exception e)
            {
                Fail(null, HookId.CanReplace, e);
            }
        }

        return orig(i, j, target, style);
    }

    private bool Danger(On.Terraria.GameContent.Drawing.TileDrawing.orig_IsTileDangerous orig,
                        Player player, short sTileHeader, ushort typeCache,
                        short tileFrameX)
    {
        var result = orig(player, sTileHeader, typeCache, tileFrameX);

        var (i, j, ok) = TileDrawHooks.TrackedCell();
        if (!ok) return result;

        bool? final = null;
        var tile = TileTable.Of(typeCache);
        if (tile is not null && tile.HookEnabled(HookId.IsTileDangerous))
        {
            try
            {
                if (tile.IsTileDangerous(i, j, player)) final = true;
            }
            catch (Exception e)
            {
                Fail(tile, HookId.IsTileDangerous, e);
            }
        }

        var globals = GlobalTileRegistry.For(HookId.IsTileDangerous);
        for (var index = 0; index < globals.Count; index++)
        {
            try
            {
                var own = globals[index].IsTileDangerous(i, j, typeCache, player);
                if (own == false) return false;
                if (own == true) final = true;
            }
            catch (Exception e)
            {
                Fail(null, HookId.IsTileDangerous, e);
            }
        }

        return final == true || result;
    }

    private bool Spelunk(On.Terraria.Main.orig_IsTileSpelunkable_ushort_short_short orig, ushort typeCache,
                         short tileFrameX, short tileFrameY)
    {
        var result = orig(typeCache, tileFrameX, tileFrameY);

        var (i, j, ok) = TileDrawHooks.TrackedCell();
        if (!ok) return result;

        bool? final = null;
        var tile = TileTable.Of(typeCache);
        if (tile is not null && tile.HookEnabled(HookId.IsTileSpelunkable))
        {
            try
            {
                if (tile.IsTileSpelunkable(i, j)) final = true;
            }
            catch (Exception e)
            {
                Fail(tile, HookId.IsTileSpelunkable, e);
            }
        }

        var globals = GlobalTileRegistry.For(HookId.IsTileSpelunkable);
        for (var index = 0; index < globals.Count; index++)
        {
            try
            {
                var own = globals[index].IsTileSpelunkable(i, j, typeCache);
                if (own == false) return false;
                if (own == true) final = true;
            }
            catch (Exception e)
            {
                Fail(null, HookId.IsTileSpelunkable, e);
            }
        }

        return final == true || result;
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
