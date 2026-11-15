using Pylon.NaviteAPI.Host;

namespace Pylon.ModLoader;

public enum PatchStatus
{
    Ok = 0, InvalidArgument = 1, OutOfBounds = 2, Unmapped = 3, NotExecutable = 4,
    FingerprintMismatch = 5, Unsupported = 6, ProtectionFailed = 7,
    ProtectionRestoreFailed = 8, BuildMismatch = 9, Conflict = 10, BackendFailure = 11,
    Unaligned = 12, DuplicateInstall = 13, LifecycleFailure = 14, CallbackFailure = 15,
    UnsupportedInstruction = 16
}

public readonly record struct PatchDiagnostic(PatchStatus Status, int SystemError, ulong Address, uint Expected, uint Actual)
{
    internal static PatchDiagnostic From(NativePatchDiagnostic value) =>
        new((PatchStatus)value.Status, value.SystemError, value.Address, value.Expected, value.Actual);
}

public readonly record struct Arm64Instruction(uint Word, Arm64InstructionKind Kind, long Displacement, ulong Target);

public enum Arm64InstructionKind : uint
{
    Unknown, B, Bl, BCond, CompareBranch, TestBranch, Adr, Adrp, Literal, Nop, Ret
}

/// <summary>
/// Static profile metadata for one bounded patch site. ExpectedWords/ExpectedFingerprint
/// must cover the complete method (MethodSize bytes); ByteOffset selects the site inside it.
/// </summary>
public readonly record struct CodeSiteProfile(
    string BuildId,
    string Profile,
    ulong MethodAddress,
    uint MethodRva,
    uint ByteOffset,
    uint MethodSize,
    string Owner,
    string SiteId,
    IReadOnlyList<uint> ExpectedWords,
    IReadOnlyList<byte> ExpectedFingerprint)
{
    public ulong MethodEnd => MethodAddress + MethodSize;
    public ulong SiteAddress => MethodAddress + ByteOffset;
}

/// <summary>
/// Bounded ARM64 code region. Native validation remains authoritative.
/// </summary>
public readonly struct CodeSite
{
    private readonly NativeCodeSite _site;
    private readonly CodeSiteProfile? _profile;
    private CodeSite(NativeCodeSite site, CodeSiteProfile? profile = null) { _site = site; _profile = profile; }
    public ulong Address => _site.Address;
    public uint ByteLength => _site.ByteLength;
    public CodeSiteProfile? Profile => _profile;

    public static bool TryCreate(ulong address, uint byteLength, out CodeSite site, out PatchDiagnostic diagnostic)
    {
        var status = Arm64Native.Create(address, byteLength, out var native, out var error);
        site = new(native);
        diagnostic = PatchDiagnostic.From(error);
        return status == 0;
    }

    public static bool TryCreate(CodeSiteProfile profile, string buildId, out CodeSite site, out PatchDiagnostic diagnostic)
    {
        site = default;
        if (!TryValidateProfile(profile, buildId, out diagnostic)) return false;
        var ok = TryCreate(profile.MethodAddress, profile.MethodSize, out site, out diagnostic);
        if (ok) site = new(site._site, profile);
        return ok;
    }

    public static bool TryValidateProfile(CodeSiteProfile profile, string buildId, out PatchDiagnostic diagnostic)
    {
        diagnostic = new(PatchStatus.InvalidArgument, 0, profile.MethodAddress, 0, 0);
        if (profile.MethodAddress == 0 || string.IsNullOrWhiteSpace(profile.BuildId) ||
            string.IsNullOrWhiteSpace(profile.Profile) || string.IsNullOrWhiteSpace(profile.Owner) ||
            string.IsNullOrWhiteSpace(profile.SiteId) || profile.ExpectedWords is null ||
            profile.ExpectedFingerprint is null || profile.ExpectedWords.Count == 0) return false;
        if (!string.Equals(profile.BuildId, buildId, StringComparison.Ordinal))
        {
            diagnostic = diagnostic with { Status = PatchStatus.BuildMismatch };
            return false;
        }
        if (((profile.MethodAddress | profile.MethodSize | profile.ByteOffset) & 3) != 0)
        {
            diagnostic = diagnostic with { Status = PatchStatus.Unaligned };
            return false;
        }
        var byteCount = (ulong)profile.ExpectedWords.Count * 4;
        if (profile.MethodSize == 0 || profile.MethodAddress > ulong.MaxValue - profile.MethodSize ||
            profile.ByteOffset >= profile.MethodSize || byteCount != profile.MethodSize)
        {
            diagnostic = diagnostic with { Status = PatchStatus.OutOfBounds };
            return false;
        }
        if ((ulong)profile.ExpectedFingerprint.Count != byteCount) return false;
        for (var i = 0; i < profile.ExpectedWords.Count; i++)
            for (var b = 0; b < 4; b++)
                if (profile.ExpectedFingerprint[i * 4 + b] != (byte)(profile.ExpectedWords[i] >> (b * 8)))
                {
                    diagnostic = diagnostic with { Status = PatchStatus.FingerprintMismatch,
                        Address = profile.MethodAddress + (uint)(i * 4), Expected = profile.ExpectedWords[i] };
                    return false;
                }
        diagnostic = new(PatchStatus.Ok, 0, profile.SiteAddress, 0, 0);
        return true;
    }

    public bool TryRead(uint byteOffset, out uint word, out PatchDiagnostic diagnostic)
    {
        var status = Arm64Native.Read(in _site, byteOffset, out word, out var error);
        diagnostic = PatchDiagnostic.From(error);
        return status == 0;
    }

    public unsafe bool TryReadWords(uint byteOffset, Span<uint> words, out PatchDiagnostic diagnostic)
    {
        fixed (uint* buffer = words)
        {
            var status = Arm64Native.TryReadWords(in _site, byteOffset, buffer, (uint)words.Length, out var error);
            diagnostic = PatchDiagnostic.From(error);
            return status == 0;
        }
    }

    public static bool TryDecode(ulong pc, uint word, out Arm64Instruction instruction, out PatchDiagnostic diagnostic)
    {
        instruction = default;
        if (!Arm64Decoder.TryDecode(pc, word, out var decoded, out diagnostic)) return false;
        var kind = decoded!.Mnemonic switch
        {
            AsmArm64.Arm64Mnemonic.B => decoded.Operands.Count > 1 ? Arm64InstructionKind.BCond : Arm64InstructionKind.B,
            AsmArm64.Arm64Mnemonic.BL => Arm64InstructionKind.Bl,
            AsmArm64.Arm64Mnemonic.CBZ or AsmArm64.Arm64Mnemonic.CBNZ => Arm64InstructionKind.CompareBranch,
            AsmArm64.Arm64Mnemonic.TBZ or AsmArm64.Arm64Mnemonic.TBNZ => Arm64InstructionKind.TestBranch,
            AsmArm64.Arm64Mnemonic.ADR => Arm64InstructionKind.Adr,
            AsmArm64.Arm64Mnemonic.ADRP => Arm64InstructionKind.Adrp,
            AsmArm64.Arm64Mnemonic.LDR or AsmArm64.Arm64Mnemonic.LDRSW when decoded.PcTarget.HasValue => Arm64InstructionKind.Literal,
            AsmArm64.Arm64Mnemonic.NOP => Arm64InstructionKind.Nop,
            AsmArm64.Arm64Mnemonic.RET => Arm64InstructionKind.Ret,
            _ => Arm64InstructionKind.Unknown
        };
        instruction = new(word, kind, decoded.PcDisplacement ?? 0, decoded.PcTarget ?? 0);
        return true;
    }

    public static bool TryPcRelative(ulong pc, uint word, out ulong target, out PatchDiagnostic diagnostic)
    {
        target = 0;
        if (!Arm64Decoder.TryDecode(pc, word, out var decoded, out diagnostic)) return false;
        if (!decoded!.PcTarget.HasValue)
        {
            diagnostic = new(PatchStatus.UnsupportedInstruction, 0, pc, 0, word);
            return false;
        }
        target = decoded.PcTarget.Value;
        return true;
    }

    public unsafe bool TryValidate(uint byteOffset, ReadOnlySpan<uint> expected, out PatchDiagnostic diagnostic)
    {
        fixed (uint* words = expected)
        {
            var status = Arm64Native.Validate(in _site, byteOffset, words, (uint)expected.Length, out var error);
            diagnostic = PatchDiagnostic.From(error);
            return status == 0;
        }
    }

    public bool TryWrite(uint byteOffset, uint expected, uint replacement, out PatchDiagnostic diagnostic)
    {
        var status = Arm64Native.Write(in _site, byteOffset, expected, replacement, out var error);
        diagnostic = PatchDiagnostic.From(error);
        return status == 0;
    }
}
