using System.Runtime.InteropServices;

namespace Pylon.ModLoader;

/// <summary>
/// 游戏把 <c>type &gt; 753</c> / <c>type &gt;= 754</c> 编进了 ARM64 指令里，反射与 hook 都改不到这些常量，
/// 只能让内核改写立即数。名单按 1.4.5.8.6 的扫描结果核对过。
/// </summary>
internal static partial class LimitPatch
{
    private const uint TileCount = 754;

    private readonly record struct Entry(
        string Type,
        string Method,
        int Argc,
        uint Old,
        int Offset,
        bool BelowToo = false,
        bool Shifted = false);

    private static readonly Entry[] Entries =
    [
        new("Terraria.WorldGen", "PlaceTile", 7, TileCount - 1, -1),
        new("Terraria.WorldGen", "PlaceObject", 8, TileCount - 1, -1),
        new("Terraria.WorldGen", "KillTile", 5, TileCount - 1, -1),
        new("Terraria.WorldGen", "KillWall", 3, TileCount - 1, -1),
        new("Terraria.WorldGen", "TileFrameImportant", 5, TileCount - 1, -1),
        new("Terraria.WorldGen", "WouldTileReplacementWork", 3, TileCount - 1, -1),
        new("Terraria.WorldGen", "CheckPile", 2, TileCount - 1, -1),
        new("Terraria.WorldGen", "Check2x1", 3, TileCount - 1, -1),
        new("Terraria.WorldGen", "Check2x2Style", 3, TileCount - 1, -1),
        new("Terraria.WorldGen", "placeTrap", 3, TileCount - 1, -1),
        new("Terraria.WorldGen", "BlockBelowMakesSandConvertIntoHardenedSand", 2, TileCount - 1, -1),
        new("Terraria.DelegateMethods", "SpreadTile", 6, TileCount - 1, -1),
        new("Terraria.Projectile", "AI_006", 0, TileCount - 1, -1),
        new("Terraria.Player", "PlaceThing_Tiles_CheckRopeUsability", 1, TileCount - 1, -1),
        new("Terraria.GameContent.SmartCursorHelper", "Step_Boulders", 3, TileCount - 1, -1),
        new("Terraria.GameContent.SmartCursorHelper", "Step_PumpkinSeeds", 3, TileCount - 1, -1),
        new("Terraria.WorldGen", "TileFrameCosmetic", 3, TileCount, 0, BelowToo: true),
        new("Terraria.WorldGen", "ValidateTypes", 0, TileCount, 0, BelowToo: true),
        new("Terraria.Collision", "IsWorldPointSolid", 2, TileCount, 0),
        new("Terraria.TileData", "GetTileDefinition", 8, TileCount, 0, Shifted: true),
    ];

    [LibraryImport("pylon", EntryPoint = "PylonMod_PatchCompareLimit", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int PatchCompareLimit(string type, string method, int argc, uint oldLimit, uint newLimit,
        int belowToo, int shifted);

    [LibraryImport("pylon", EntryPoint = "PylonMod_LastError")]
    private static partial IntPtr LastErrorPointer();

    /// <summary>把方块类型上限从 754 顶到 <paramref name="total"/>；返回失败的方法数。</summary>
    internal static int Apply(int total, ModLogger log)
    {
        if (total <= TileCount) return 0;

        var failed = 0;
        foreach (var entry in Entries)
        {
            var target = (uint)(total + entry.Offset);
            var patched = PatchCompareLimit(entry.Type, entry.Method, entry.Argc, entry.Old, target,
                entry.BelowToo ? 1 : 0, entry.Shifted ? 1 : 0);

            if (patched > 0) continue;

            failed++;
            var reason = patched == 0 ? "指令里没找到 cmp" : "内核报错：" + LastError();
            log.Warn($"[tile] {entry.Type}.{entry.Method} 的上限 {entry.Old} → {target} 没改成（{reason}）");
        }

        if (failed == 0) log.Info($"[tile] 编译期上限已顶到 {total}（{Entries.Length} 个方法）");
        return failed;
    }

    private static string LastError()
    {
        var pointer = LastErrorPointer();
        return pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
    }
}
