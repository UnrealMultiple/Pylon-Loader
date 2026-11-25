using Pylon.ModLoader.Logging;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader.Patching;
/// <summary>
/// 游戏把 <c>type &gt; 753</c> / <c>type &gt;= 754</c> 编进了 ARM64 指令里，反射与 hook 都改不到这些常量，
/// 只能让内核改写立即数。名单按 1.4.6 的扫描结果核对过。
/// </summary>
internal static class LimitPatch
{
    internal const uint TileCount = 754;

    /// <summary>
    /// Runtime locator input. <see cref="Class"/> is the Il2CppInterop proxy type;
    /// <see cref="MethodCode.TryResolve"/> turns it into the native method address at runtime.
    /// <see cref="Sites"/> is the expected match count (metadata, not an address).
    /// </summary>
    internal readonly record struct Entry(
        System.Type Class,
        string Method,
        int Argc,
        uint Old,
        int Offset,
        int Sites,
        bool BelowToo = false,
        bool Shifted = false)
    {
        internal string TypeName => Class.FullName ?? Class.Name;
    }

    internal static readonly Entry[] Entries =
    [
        new(typeof(WorldGen), "PlaceTile", 7, TileCount - 1, -1, 2),
        new(typeof(WorldGen), "PlaceObject", 8, TileCount - 1, -1, 1),
        new(typeof(WorldGen), "KillTile", 5, TileCount - 1, -1, 1),
        new(typeof(WorldGen), "KillWall", 3, TileCount - 1, -1, 1),
        new(typeof(WorldGen), "TileFrameImportant", 5, TileCount - 1, -1, 1),
        new(typeof(WorldGen), "WouldTileReplacementWork", 3, TileCount - 1, -1, 1),
        new(typeof(WorldGen), "CheckPile", 2, TileCount - 1, -1, 1),
        new(typeof(WorldGen), "Check2x1", 3, TileCount - 1, -1, 1),
        new(typeof(WorldGen), "Check2x2Style", 3, TileCount - 1, -1, 1),
        new(typeof(WorldGen), "placeTrap", 3, TileCount - 1, -1, 1),
        new(typeof(WorldGen), "BlockBelowMakesSandConvertIntoHardenedSand", 2, TileCount - 1, -1, 1),
        new(typeof(DelegateMethods), "SpreadTile", 6, TileCount - 1, -1, 1),
        new(typeof(Projectile), "AI_006", 0, TileCount - 1, -1, 1),
        new(typeof(Player), "PlaceThing_Tiles_CheckRopeUsability", 1, TileCount - 1, -1, 1),
        new(typeof(SmartCursorHelper), "Step_Boulders", 3, TileCount - 1, -1, 1),
        new(typeof(SmartCursorHelper), "Step_PumpkinSeeds", 3, TileCount - 1, -1, 1),
        new(typeof(WorldGen), "TileFrameCosmetic", 3, TileCount, 0, 1, BelowToo: true),
        new(typeof(WorldGen), "ValidateTypes", 0, TileCount, 0, 1, BelowToo: true),
        new(typeof(Collision), "IsWorldPointSolid", 2, TileCount, 0, 1),
        new(typeof(TileData), "GetTileDefinition", 8, TileCount, 0, 1, Shifted: true),
    ];

    /// <summary>Install the runtime-resolved profile; no JSON addresses are required.</summary>
    internal static int Apply(int total, IModLog log)
    {
        if (total <= TileCount) return 0;
        var failed = RuntimeLimitInstaller.Apply(total, log);
        if (failed != 0)
            log.Warn($"[limit] runtime installer failed={failed}; no tile-limit sites were patched");
        return failed;
    }
}
