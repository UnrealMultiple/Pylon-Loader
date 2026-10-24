using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Pylon.ModLoader
{
    /// <summary>
    /// 游戏把 <c>type &gt; 753</c> / <c>type &gt;= 754</c> 编进了 ARM64 指令里。
    /// 反射/Harmony 都改不到这些常量，只能让内核改立即数；名单按扫描核对过，只面向 1.4.5.8.6。
    /// </summary>
    internal static class LimitPatch
    {
        private readonly struct Entry
        {
            internal readonly string Type;
            internal readonly string Method;
            internal readonly int Argc;
            internal readonly uint Old;
            internal readonly int Offset;
            internal readonly bool BelowToo;
            internal readonly bool Shifted;

            internal Entry(string type, string method, int argc, uint old, int offset,
                           bool belowToo = false, bool shifted = false)
            {
                Type = type;
                Method = method;
                Argc = argc;
                Old = old;
                Offset = offset;
                BelowToo = belowToo;
                Shifted = shifted;
            }
        }

        private const uint TileCount = 754;

        private static readonly Entry[] Entries =
        {
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
            new("Terraria.WorldGen", "TileFrameCosmetic", 3, TileCount, 0, true),
            new("Terraria.WorldGen", "ValidateTypes", 0, TileCount, 0, true),
            new("Terraria.Collision", "IsWorldPointSolid", 2, TileCount, 0),
            new("Terraria.TileData", "GetTileDefinition", 8, TileCount, 0, false, true),
        };

        [DllImport("pylon")]
        private static extern int PylonMod_PatchCompareLimit(string type, string method, int argc,
            uint oldLimit, uint newLimit, int belowToo, int shifted);

        [DllImport("pylon")]
        private static extern IntPtr PylonMod_LastError();

        private static string LastError()
        {
            try
            {
                var pointer = PylonMod_LastError();
                return pointer == IntPtr.Zero ? "" : System.Runtime.InteropServices.Marshal.PtrToStringUTF8(pointer);
            }
            catch { return ""; }
        }

        /// <summary>把方块类型上限从 754 顶到 <paramref name="total"/>；返回失败的方法数。</summary>
        internal static int Apply(int total, ModLogger log)
        {
            if (total <= TileCount) return 0;

            var failed = 0;
            foreach (var entry in Entries)
            {
                var target = (uint)(total + entry.Offset);
                try
                {
                    var patched = PylonMod_PatchCompareLimit(entry.Type, entry.Method, entry.Argc,
                        entry.Old, target, entry.BelowToo ? 1 : 0, entry.Shifted ? 1 : 0);
                    if (patched > 0) continue;
                    failed++;
                    log?.Warn($"[tile] {entry.Type}.{entry.Method} 的上限 {entry.Old} → {target} 没改成" +
                              $"（{(patched == 0 ? "指令里没找到 cmp" : "内核报错：" + LastError())}）");
                }
                catch (Exception e)
                {
                    failed++;
                    log?.Warn($"[tile] {entry.Type}.{entry.Method} 的上限补丁异常：{e.Message}");
                }
            }
            if (failed == 0) log?.Info($"[tile] 编译期上限已顶到 {total}（{Entries.Length} 个方法）");
            return failed;
        }
    }
}
