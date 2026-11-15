using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader;

/// <summary>
/// 游戏把 <c>type &gt; 753</c> / <c>type &gt;= 754</c> 编进了 ARM64 指令里，反射与 hook 都改不到这些常量，
/// 只能让内核改写立即数。名单按 1.4.5.8.6 的扫描结果核对过。
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

    /// <summary>Rollback switch: false = pinned offline JSON profile.</summary>
    internal static bool UseRuntimeInstaller = true;

    /// <summary>Install the runtime-resolved profile; no JSON addresses are required.</summary>
    internal static int Apply(int total, IModLog log)
    {
        if (total <= TileCount) return 0;
        if (!UseRuntimeInstaller) return StaticLimitInstaller.Apply(total, log);
        var failed = RuntimeLimitInstaller.Apply(total, log);
        if (failed != 0)
            log.Warn($"[limit] runtime installer failed={failed}; JSON profile was not applied " +
                     "(rollback: LimitPatch.UseRuntimeInstaller=false)");
        return failed;
    }

    /// <summary>
    /// Install one explicit offline method profile after the host has authenticated the loaded image.
    /// The host must exclude competing writers throughout all writes; execution may continue because
    /// native writes one aligned word through /proc/self/mem without changing page permissions.
    /// No live bytes become expected.
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
}
