using Il2CppInterop.Runtime;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.Tables;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 墙壁管线：挖 / 尘土 / 声音 / 掉落 / 边框 / 发光。每类型数据在 WallTable（密集 struct 数组）。
/// 发光是两阶段的：光照扫描只收集（此时引擎正在算这一帧的光，不能边扫边加），世界更新收尾再统一落光；
/// 两阶段之间隔一次绘制，所以作者写的是上一帧格子上的光，墙被拆掉时按类型不符丢弃。
/// </summary>
internal sealed class WallPipeline : ILoaderModule
{
    public static WallPipeline Instance { get; } = new();

    private readonly HashSet<int> _reported = [];

    /// <summary>这一击还要出几粒尘土：NumDust 定预算，MakeWallDust 每粒扣 1。</summary>
    [ThreadStatic] private static int _dustBudget;

    /// <summary>这一击是不是"只出尘土、墙还在"（NumDust / KillSound 要用）。</summary>
    [ThreadStatic] private static bool _failing;

    private const int TileStaticsOffset = 0xB8;
    private const int TileWallOffset = 0xC8;

    private bool[]? _lightWalls;

    private int[] _lightCells = new int[12288];
    private float[] _lightR = new float[4096];
    private float[] _lightG = new float[4096];
    private float[] _lightB = new float[4096];
    private int _lightCount;

    public string Name => "wall";

    public IReadOnlyList<TableRequest> Tables =>
        [new(WallTables.Set, () => ModContent.NextType(ContentKind.Wall), early: true)];


    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>wall 管线的安装门：条件与订阅同居一门，由 GateHub 自动发现与派发。</summary>
    internal static class Gates
    {
        internal struct Kill : IGate
        {
            public static bool Wanted() => ModContent.GetContent<ModWall>().Count > 0;

            public static void Install()
            {
                On.Terraria.WorldGen.KillWall += Instance.Kill;
                On.Terraria.WorldGen.KillWall_MakeWallDust += Instance.MakeDust;
                On.Terraria.WorldGen.KillWall_PlaySounds += Instance.PlaySounds;
                On.Terraria.WorldGen.KillWall_DropItems += Instance.DropItems;
                Log.Info("[wall] 已挂（挖 / 尘土 / 声音 / 掉落；边框 / 动画按覆写分装）");
            }
        }

        internal struct Frame : IGate
        {
            public static bool Wanted() => HookGuard.Wants<ModWall>(HookId.WallFrame);

            public static void Install() => On.Terraria.Framing.WallFrame += Instance.Frame;
        }

        internal struct Animate : IGate
        {
            public static bool Wanted() => HookGuard.Wants<ModWall>(HookId.AnimateWall);

            public static void Install() => On.Terraria.Main.DoUpdate_AnimateWalls += Instance.Animate;
        }

        internal struct Light : IGate
        {
            public static bool Wanted() => HookGuard.Wants<ModWall>(HookId.ModifyLight);

            public static void Install()
            {
                On.Terraria.Lighting.LightTiles += Instance.CollectLight;
                On.Terraria.Main.DoUpdateInWorld += Instance.ApplyLight;
            }
        }
    }

    public bool Install() => GateHub<WallPipeline>.Run();

    private void Animate(On.Terraria.Main.orig_DoUpdate_AnimateWalls orig)
    {
        orig();

        try
        {
            var frames = Main.wallFrame;
            var counters = Main.wallFrameCounter;
            if (frames is null || counters is null) return;

            var walls = WallTable.All;
            for (var n = 0; n < walls.Length; n++)
            {
                var wall = walls[n];
                if (wall is null || !wall.HookEnabled(HookId.AnimateWall)) continue;
                if (wall.Type >= frames.Length || wall.Type >= counters.Length) continue;

                var frame = frames[wall.Type];
                var counter = counters[wall.Type];
                try
                {
                    wall.AnimateWall(ref frame, ref counter);
                }
                catch (Exception e)
                {
                    Fail(wall, HookId.AnimateWall, e);
                    continue;
                }

                frames[wall.Type] = frame;
                counters[wall.Type] = counter;
            }
        }
        catch (Exception e)
        {
            Safe.Once("wall.animate", "[wall] 墙动画推进异常：" + Safe.Dump(e), true);
        }
    }

    internal void Build()
    {
        WallTable.Build(Log);
        InstallCounts();
    }

    /// <summary>
    /// 墙表扩完之后的收尾：<c>WallID.Count</c> 写成新总数，<c>Main.wallBlend</c> 的新槽填成自身。
    /// 少了这两步的表现是"<c>WorldGen.PlaceWall</c> 对 mod 墙类型静默失败"——原版放墙开头就拿
    /// <c>WallID.Count</c> 当上界挡越界类型，正因为如此玩家也永远放不下 mod 墙。
    /// </summary>
    private static void InstallCounts()
    {
        var total = VanillaCounts.Wall + ModContent.Count(ContentKind.Wall);
        if (total <= VanillaCounts.Wall) return;

        Terraria.ID.WallID.Count = (ushort)total;
        var blended = WallTables.FillWallBlend(VanillaCounts.Wall, total);

        Log.Info($"[wall] WallID.Count → {total}（当前 {Terraria.ID.WallID.Count}），wallBlend 补 {blended} 槽");
    }

    private void Kill(On.Terraria.WorldGen.orig_KillWall orig, int i, int j, bool fail)
    {
        var wall = WallTable.At(i, j);

        if (wall is null)
        {
            orig(i, j, fail);
            return;
        }

        if (wall.HookEnabled(HookId.KillWall))
        {
            var failing = fail;
            try
            {
                wall.KillWall(i, j, ref failing);
            }
            catch (Exception e)
            {
                Fail(wall, HookId.KillWall, e);
            }

            fail = failing;
        }

        // 原版默认：只出尘土的 1 粒、真挖掉的 3 粒；作者用 NumDust 改。
        var budget = fail ? 1 : 3;
        if (wall.HookEnabled(HookId.NumDust))
        {
            try
            {
                wall.NumDust(i, j, fail, ref budget);
            }
            catch (Exception e)
            {
                Fail(wall, HookId.NumDust, e);
            }
        }

        var previousBudget = _dustBudget;
        var previousFailing = _failing;
        _dustBudget = Math.Max(0, budget);
        _failing = fail;

        try
        {
            orig(i, j, fail);
        }
        finally
        {
            _dustBudget = previousBudget;
            _failing = previousFailing;
        }
    }

    private void MakeDust(On.Terraria.WorldGen.orig_KillWall_MakeWallDust orig, int i, int j, Tile tileCache)
    {
        var wall = WallTable.Of(tileCache.wall);
        if (wall is null)
        {
            orig(i, j, tileCache);
            return;
        }

        // 预算用完就不出（原版每粒调一次这里）。
        if (_dustBudget <= 0) return;
        _dustBudget--;

        var type = wall.DustType;
        try
        {
            if (!wall.CreateDust(i, j, ref type) || type < 0) return;
        }
        catch (Exception e)
        {
            Fail(wall, HookId.CreateDust, e);
            return;
        }

        Dust.NewDust(new Il2CppMicrosoft.Xna.Framework.Vector2(i * 16, j * 16), 16, 16, type, 0, 0, 0, default, 1);
    }

    private void PlaySounds(On.Terraria.WorldGen.orig_KillWall_PlaySounds orig, int i, int j, Tile tileCache)
    {
        var wall = WallTable.Of(tileCache.wall);
        if (wall is null)
        {
            orig(i, j, tileCache);
            return;
        }

        var play = true;
        if (wall.HookEnabled(HookId.KillSound))
        {
            try
            {
                play = wall.KillSound(i, j, _failing);
            }
            catch (Exception e)
            {
                Fail(wall, HookId.KillSound, e);
            }
        }

        if (!play) return;

        if (wall.HitSound > 0) Terraria.Audio.SoundEngine.PlaySound(wall.HitSound, i * 16, j * 16, 1, 1f, 0f);
    }

    private void DropItems(On.Terraria.WorldGen.orig_KillWall_DropItems orig, int i, int j, Tile tileCache)
    {
        var wall = WallTable.Of(tileCache.wall);
        if (wall is null)
        {
            orig(i, j, tileCache);
            return;
        }

        // 默认掉落（放置物品或 ItemDrop 登记）先填好，作者在 Drop 里可以换。
        var type = WallTable.DropOf(tileCache.wall);

        if (wall.HookEnabled(HookId.Drop))
        {
            try
            {
                if (!wall.Drop(i, j, ref type)) return;
            }
            catch (Exception e)
            {
                Fail(wall, HookId.Drop, e);
            }
        }

        if (type <= 0) return;

        Item.NewItem(i * 16, j * 16, 16, 16, type);
    }

    private void Frame(On.Terraria.Framing.orig_WallFrame orig, int i, int j, bool resetFrame)
    {
        var wall = WallTable.At(i, j);

        if (wall is not null && wall.HookEnabled(HookId.WallFrame))
        {
            var style = 0;
            var frameNumber = 0;

            try
            {
                if (!wall.WallFrame(i, j, resetFrame, ref style, ref frameNumber)) return;
            }
            catch (Exception e)
            {
                Fail(wall, HookId.WallFrame, e);
            }
        }

        orig(i, j, resetFrame);
    }

    private void CollectLight(On.Terraria.Lighting.orig_LightTiles orig, int firstX, int lastX, int firstY, int lastY)
    {
        orig(firstX, lastX, firstY, lastY);

        try
        {
            ScanLightCells(firstX, lastX, firstY, lastY);
        }
        catch (Exception e)
        {
            _lightCount = 0;
            Safe.Once("wall.light.scan", "[wall] 墙发光扫描异常：" + Safe.Dump(e), true);
            return;
        }

        for (var k = 0; k < _lightCount; k++)
        {
            float r = 0, g = 0, b = 0;
            var wall = WallTable.Of(_lightCells[k * 3 + 2]);
            if (wall is not null)
            {
                try
                {
                    wall.ModifyLight(_lightCells[k * 3], _lightCells[k * 3 + 1], ref r, ref g, ref b);
                }
                catch (Exception e)
                {
                    Fail(wall, HookId.ModifyLight, e);
                }
            }

            _lightR[k] = r;
            _lightG[k] = g;
            _lightB[k] = b;
        }
    }

    private unsafe void ScanLightCells(int firstX, int lastX, int firstY, int lastY)
    {
        _lightCount = 0;
        var lightWalls = _lightWalls ??= BuildLightWalls();

        var walls = WallArray(out var width, out var height);
        if (walls is null) return;

        var left = Math.Max(firstX - 28, 0);
        var top = Math.Max(firstY - 28, 0);
        var right = Math.Min(lastX + 29, width);
        var bottom = Math.Min(lastY + 29, height);
        if (left >= right || top >= bottom) return;

        var cells = _lightCells;
        var count = 0;
        for (var j = top; j < bottom; j++)
        {
            var row = j * width;
            for (var i = left; i < right; i++)
            {
                var type = walls[row + i];
                if ((uint)type >= (uint)lightWalls.Length || !lightWalls[type]) continue;

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

    private bool[] BuildLightWalls()
    {
        var all = WallTable.All;
        var max = 0;
        foreach (var wall in all)
        {
            if (wall is null || !wall.HookEnabled(HookId.ModifyLight)) continue;
            if (wall.Type > max) max = wall.Type;
        }

        var table = new bool[max + 1];
        foreach (var wall in all)
        {
            if (wall is null || !wall.HookEnabled(HookId.ModifyLight)) continue;
            table[wall.Type] = true;
        }
        return table;
    }

    private void ApplyLight(On.Terraria.Main.orig_DoUpdateInWorld orig, Main self)
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
            Safe.Once("wall.light.apply", "[wall] 墙发光落光异常：" + Safe.Dump(e), true);
        }
    }

    private unsafe void ApplyLightCells()
    {
        var walls = WallArray(out var width, out var height);
        if (walls is null) return;

        var cells = _lightCells;
        for (var k = 0; k < _lightCount; k++)
        {
            var r = _lightR[k];
            var g = _lightG[k];
            var b = _lightB[k];
            if (r == 0 && g == 0 && b == 0) continue;

            var i = cells[k * 3];
            var j = cells[k * 3 + 1];
            if (i < 0 || j < 0 || i >= width || j >= height || walls[j * width + i] != cells[k * 3 + 2]) continue;

            Lighting.AddLight(i, j, r, g, b);
        }
    }

    private static unsafe ushort* WallArray(out int width, out int height)
    {
        width = 0;
        height = 0;
        var tile = Main.tile;
        if (tile is null) return null;

        width = tile._width;
        height = tile._height;
        if (width <= 0 || height <= 0) return null;

        var klass = Il2CppClassPointerStore<TileData>.NativeClassPtr;
        if (klass == IntPtr.Zero) return null;
        try { IL2CPP.il2cpp_runtime_class_init(klass); } catch { }

        var block = *(IntPtr*)((byte*)klass + TileStaticsOffset);
        if (block == IntPtr.Zero) return null;
        return *(ushort**)((byte*)block + TileWallOffset);
    }

    private void Fail(ModWall wall, HookId hook, Exception exception)
    {
        if (!_reported.Add(HashCode.Combine(wall.Type, hook))) return;
        ContentErrors.Line($"[wall] {wall.GetType().Name}.{hook} 异常：{Safe.Dump(exception)}");
    }
}

/// <summary>墙壁编号相关的原生表：墙、Sets、贴图、地图颜色。</summary>
internal static class WallTables
{
    private static readonly Type[] Types =
    [
        typeof(Terraria.Main),
        typeof(Terraria.ID.WallID.Sets),
        typeof(Terraria.ID.WallID.Sets.Conversion),
        typeof(Terraria.GameContent.TextureAssets),
        typeof(Terraria.Map.MapHelper),
    ];

    internal static readonly TableSet Set = new("墙壁", () => 367, Types);

    /// <summary>
    /// <c>Main.wallBlend</c> 里新墙的空槽填成自身（原版约定 <c>wallBlend[i] == i</c>）。
    /// 留 0 的话边框混合会去查原版墙 0 的样式，mod 墙的边框会闪/错位。返回填了几槽。
    /// </summary>
    internal static int FillWallBlend(int from, int to)
    {
        var blend = Terraria.Main.wallBlend;
        if (blend is null || to <= from || to > blend.Length) return 0;

        var filled = 0;
        for (var type = from; type < to; type++)
        {
            if (blend[type] != 0) continue;
            blend[type] = type;
            filled++;
        }
        return filled;
    }
}
