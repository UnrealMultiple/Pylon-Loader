using Il2CppInterop.Runtime;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Globals;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 方块发光的两阶段分派：光照扫描期间引擎正在算这一帧的光，不能边扫边加，所以
/// LightTiles 只收集——裸指针直读 tile 数组（lookup → definition → type/sHeader），
/// 光照区域四边外扩 28 格，只收覆写了 ModifyLight 的类型（有 GlobalTile 覆写时收全部活动格，
/// 与原版"全局钩按格咨询"的口径一致）；世界更新收尾再统一 Lighting.AddLight 落地，
/// 落的是上一帧收集的色值，格子被拆掉或换型时按 active/类型不符丢弃。
/// 作者钩子的门是 Main.tileLighted（作者在 SetStaticDefaults 里置 true 才发光），
/// 派发顺序 ModTile 先、GlobalTile 后，异常逐个记一条，不炸光照循环；门上无人覆写时不装。
/// </summary>
internal static class TileLightHooks
{
    private static bool _installed;

    private static bool[]? _lightTiles;
    private static List<GlobalTile>? _globals;

    private static int[] _lightCells = new int[12288];
    private static float[] _lightR = new float[4096];
    private static float[] _lightG = new float[4096];
    private static float[] _lightB = new float[4096];
    private static int _lightCount;

    private const int TileStaticsOffset = 0xB8;
    private const int LookupOffset = 0x68;
    private const int TypeOffset = 0xA8;
    private const int HeaderOffset = 0xB0;
    private const short ActiveBit = 0x20;

    private static readonly HashSet<int> _reported = [];

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;

        On.Terraria.Lighting.LightTiles += CollectLight;
        On.Terraria.Main.DoUpdateInWorld += ApplyLight;
    }

    private static void CollectLight(On.Terraria.Lighting.orig_LightTiles orig,
                                     int firstX, int lastX, int firstY, int lastY)
    {
        orig(firstX, lastX, firstY, lastY);

        try
        {
            ScanLightCells(firstX, lastX, firstY, lastY);
        }
        catch (Exception e)
        {
            _lightCount = 0;
            Safe.Once("tile.light.scan", "[tile] 方块发光扫描异常：" + Safe.Describe(e), true);
            return;
        }

        var cells = _lightCells;
        var globals = _globals!;
        for (var k = 0; k < _lightCount; k++)
        {
            var i = cells[k * 3];
            var j = cells[k * 3 + 1];
            var type = cells[k * 3 + 2];
            float r = 0, g = 0, b = 0;

            var mod = TileTable.Of(type);
            if (mod is not null && mod.HookEnabled(HookId.ModifyLight) && Lighted(type))
            {
                try
                {
                    mod.ModifyLight(i, j, ref r, ref g, ref b);
                }
                catch (Exception e)
                {
                    Fail(mod, e);
                }
            }

            for (var n = 0; n < globals.Count; n++)
            {
                var global = globals[n];
                try
                {
                    global.ModifyLight(i, j, type, ref r, ref g, ref b);
                }
                catch (Exception e)
                {
                    Fail(global, e);
                }
            }

            _lightR[k] = r;
            _lightG[k] = g;
            _lightB[k] = b;
        }
    }

    private static unsafe void ScanLightCells(int firstX, int lastX, int firstY, int lastY)
    {
        _lightCount = 0;
        EnsureSources();

        var ptrs = ReadPtrs();
        if (!ptrs.Ok) return;

        var left = Math.Max(firstX - 28, 0);
        var top = Math.Max(firstY - 28, 0);
        var right = Math.Min(lastX + 29, ptrs.Width);
        var bottom = Math.Min(lastY + 29, ptrs.Height);
        if (left >= right || top >= bottom) return;

        var lightTiles = _lightTiles!;
        var globals = _globals!;
        var lookup = ptrs.Lookup;
        var types = ptrs.Type;
        var headers = ptrs.SHeader;
        var cells = _lightCells;
        var count = 0;
        for (var j = top; j < bottom; j++)
        {
            var row = (long)ptrs.Width * j;
            for (var i = left; i < right; i++)
            {
                var def = lookup[row + i];
                if ((headers[def] & ActiveBit) == 0) continue;

                var type = types[def];
                var hooked = (uint)type < (uint)lightTiles.Length && lightTiles[type];
                if (!hooked && globals.Count == 0) continue;

                if (count * 3 + 3 > cells.Length)
                {
                    var grown = new int[cells.Length * 2];
                    Array.Copy(cells, grown, cells.Length);
                    cells = grown;
                    _lightR = new float[cells.Length / 3];
                    _lightG = new float[cells.Length / 3];
                    _lightB = new float[cells.Length / 3];
                }

                cells[count * 3] = i;
                cells[count * 3 + 1] = j;
                cells[count * 3 + 2] = type;
                count++;
            }
        }

        _lightCells = cells;
        _lightCount = count;
    }

    private static void EnsureSources()
    {
        if (_lightTiles is not null) return;

        var all = TileTable.All;
        var max = 0;
        foreach (var tile in all)
        {
            if (tile is null || !tile.HookEnabled(HookId.ModifyLight)) continue;
            if (tile.Type > max) max = tile.Type;
        }

        var table = new bool[max + 1];
        foreach (var tile in all)
        {
            if (tile is null || !tile.HookEnabled(HookId.ModifyLight)) continue;
            table[tile.Type] = true;
        }

        _lightTiles = table;
        _globals = GlobalTileRegistry.For(HookId.ModifyLight);
    }

    private static void ApplyLight(On.Terraria.Main.orig_DoUpdateInWorld orig, Main self)
    {
        orig(self);

        if (_lightCount == 0) return;
        if (Main.gameMenu)
        {
            _lightCount = 0;
            return;
        }

        try
        {
            ApplyLightCells();
        }
        catch (Exception e)
        {
            _lightCount = 0;
            Safe.Once("tile.light.apply", "[tile] 方块发光落光异常：" + Safe.Describe(e), true);
        }
    }

    private static unsafe void ApplyLightCells()
    {
        var ptrs = ReadPtrs();
        if (!ptrs.Ok) return;

        var cells = _lightCells;
        for (var k = 0; k < _lightCount; k++)
        {
            var r = _lightR[k];
            var g = _lightG[k];
            var b = _lightB[k];
            if (r == 0 && g == 0 && b == 0) continue;

            var i = cells[k * 3];
            var j = cells[k * 3 + 1];
            if (i < 0 || j < 0 || i >= ptrs.Width || j >= ptrs.Height) continue;

            var def = ptrs.Lookup[(long)ptrs.Width * j + i];
            if ((ptrs.SHeader[def] & ActiveBit) == 0 || ptrs.Type[def] != cells[k * 3 + 2]) continue;

            Lighting.AddLight(i, j, r, g, b);
        }
    }

    private static bool Lighted(int type)
    {
        var lighted = Main.tileLighted;
        return lighted is not null && (uint)type < (uint)lighted.Length && lighted[type];
    }

    private unsafe struct TilePtrs
    {
        public uint* Lookup;
        public ushort* Type;
        public short* SHeader;
        public int Width, Height;
        public bool Ok;
    }

    private static unsafe TilePtrs ReadPtrs()
    {
        TilePtrs ptrs;
        var tile = Main.tile;
        if (tile is null || tile._width <= 0 || tile._height <= 0)
        {
            ptrs = default;
            return ptrs;
        }

        var klass = Il2CppClassPointerStore<TileData>.NativeClassPtr;
        if (klass == IntPtr.Zero)
        {
            ptrs = default;
            return ptrs;
        }

        try { IL2CPP.il2cpp_runtime_class_init(klass); } catch { }

        var block = *(IntPtr*)((byte*)klass + TileStaticsOffset);
        if (block == IntPtr.Zero)
        {
            ptrs = default;
            return ptrs;
        }

        ptrs.Lookup = *(uint**)((byte*)block + LookupOffset);
        ptrs.Type = *(ushort**)((byte*)block + TypeOffset);
        ptrs.SHeader = *(short**)((byte*)block + HeaderOffset);
        ptrs.Width = tile._width;
        ptrs.Height = tile._height;
        ptrs.Ok = ptrs.Lookup != null && ptrs.Type != null && ptrs.SHeader != null;
        return ptrs;
    }

    private static void Fail(ModTile tile, Exception exception)
    {
        if (!_reported.Add(HashCode.Combine(tile.Type, HookId.ModifyLight))) return;
        ContentErrors.Report($"[tile] {tile.GetType().Name}.ModifyLight 异常：{Safe.Describe(exception)}");
    }

    private static void Fail(GlobalTile global, Exception exception)
    {
        if (!_reported.Add(HashCode.Combine(global.GetType(), HookId.ModifyLight))) return;
        ContentErrors.Report($"[tile] {global.GetType().Name}.ModifyLight 异常：{Safe.Describe(exception)}");
    }
}
