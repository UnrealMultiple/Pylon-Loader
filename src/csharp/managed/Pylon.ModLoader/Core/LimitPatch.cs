using System.Runtime.InteropServices;
using Pylon.NaviteAPI.Host;

namespace Pylon.ModLoader;

/// <summary>
/// 游戏把 <c>type &gt; 753</c> / <c>type &gt;= 754</c> 编进了 ARM64 指令里，反射与 hook 都改不到这些常量，
/// 只能让内核改写立即数。名单按 1.4.5.8.6 的扫描结果核对过。
/// </summary>
internal static class LimitPatch
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

    /// <summary>Install the pinned offline profile after loaded ELF identity verification.</summary>
    internal static int Apply(int total, IModLog log) => StaticLimitInstaller.Apply(total, log);

    internal static int ApplyLegacyMethod(string method, int total, IModLog log)
    {
        foreach (var entry in Entries)
        {
            if (entry.Type + "$$" + entry.Method != method) continue;
            var target = (uint)(total + entry.Offset);
            var count = PatchNative.CompareLimit(entry.Type, entry.Method, entry.Argc, entry.Old, target,
                entry.BelowToo ? 1 : 0, entry.Shifted ? 1 : 0);
            log.Warn($"[arm64] legacy limit path retained: {method} count={count}");
            return count > 0 ? 0 : 1;
        }
        return 1;
    }

    /// <summary>Legacy native string scanner. Kept to avoid losing unsupported comparisons during migration.</summary>
    internal static int ApplyLegacy(int total, IModLog log)
    {
        if (total <= TileCount) return 0;

        var failed = 0;
        foreach (var entry in Entries)
        {
            var target = (uint)(total + entry.Offset);
            var patched = PatchNative.CompareLimit(entry.Type, entry.Method, entry.Argc, entry.Old, target,
                entry.BelowToo ? 1 : 0, entry.Shifted ? 1 : 0);

            if (patched > 0) continue;

            failed++;
            var reason = patched == 0 ? "指令里没找到 cmp" : "内核报错：" + LastError();
            log.Warn($"[tile] {entry.Type}.{entry.Method} 的上限 {entry.Old} → {target} 没改成（{reason}）");
        }

        if (failed == 0) log.Info($"[tile] 编译期上限已顶到 {total}（{Entries.Length} 个方法）");
        return failed;
    }

    /// <summary>
    /// Install one explicit offline method profile after the host has authenticated the loaded image.
    /// The host must exclude execution of this method throughout all writes. No live bytes become expected.
    /// </summary>
    internal static bool ApplyProfile(CompareLimitMethodProfile profile, string expectedBuildId,
        string authenticatedBuildId, ulong imageBase, int total, bool callerGuaranteesQuiescence, IModLog log)
    {
        if (!callerGuaranteesQuiescence || string.IsNullOrWhiteSpace(expectedBuildId) ||
            !string.Equals(expectedBuildId, authenticatedBuildId, StringComparison.Ordinal) ||
            imageBase > ulong.MaxValue - profile.MethodRva ||
            !CompareLimitPolicy.Plan(profile, total, out var planned, out _)) return false;
        var methodAddress = imageBase + profile.MethodRva;
        var bytes = profile.ExpectedMethod.ToArray();
        var words = new uint[bytes.Length / 4];
        for (var i = 0; i < words.Length; i++)
            words[i] = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4, 4));
        // Pre-plan every site before the first write. Native v2 must recognize prior registered writes
        // in the same method while validating this original immutable complete-method fingerprint.
        var transactions = new List<Arm64PatchTransaction>();
        foreach (var replacement in planned!.Replacements)
        {
            var decoded = AsmArm64.Arm64Instruction.Decode(replacement.Expected);
            var register = ((AsmArm64.Arm64RegisterOperand)decoded.GetOperand(0)).Value;
            if (register.Kind != AsmArm64.Arm64RegisterKind.W) return false;
            var target = AsmArm64.Arm64Instruction.Decode(replacement.Replacement);
            var immediate = ((AsmArm64.Arm64ImmediateOperand)target.GetOperand(1)).Value;
            using var builder = new Arm64PatchBuilder(methodAddress + replacement.Offset);
            var shift = target.OperandCount > 2 && target.GetOperand(2).Kind == AsmArm64.Arm64OperandKind.Shift
                ? ((AsmArm64.Arm64ShiftOperand)target.GetOperand(2)).Amount : 0;
            builder.CMP((AsmArm64.Arm64RegisterW)register, (ushort)immediate, shift);
            var site = new CodeSiteProfile(expectedBuildId, "terraria-1.4.5.8.6-arm64-v8a", methodAddress,
                profile.MethodRva, replacement.Offset, (uint)bytes.Length, "LimitPatch",
                profile.Method + "/" + replacement.Offset, Array.AsReadOnly(words), Array.AsReadOnly(bytes));
            if (!Arm64PatchTransaction.TryPlan(site, authenticatedBuildId, builder, out var transaction, out _, fingerprintWordCount: 4)) return false;
            transactions.Add(transaction!);
        }
        foreach (var transaction in transactions)
        {
            if (transaction.TryApply(callerGuaranteesQuiescence, out var diagnostic)) continue;
            log.Warn($"[tile] static profile failed: {profile.Method}: {diagnostic.Status} address=0x{diagnostic.Address:x} offset=0x{diagnostic.ByteOffset:x} expected={diagnostic.Expected:x8} actual={diagnostic.Actual:x8}; prior sites may already be installed");
            return false;
        }
        return true;
    }

    private static string LastError()
    {
        var pointer = PatchNative.LastError();
        return pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
    }
}
