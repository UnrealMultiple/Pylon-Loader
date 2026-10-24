using Pylon.NaviteAPI.Host;

namespace Pylon.ModLoader.Patching;

public readonly record struct PatchWriteDiagnostic(PatchStatus Status, int SystemError,
    string BuildId, string Owner, string SiteId, ulong MethodRva, ulong SiteRva,
    ulong Address, uint ByteOffset, uint Expected, uint Actual, bool WasWritten);

/// <summary>
/// Single-instruction installation with a complete, static method fingerprint.
/// The caller authenticates the actual image BuildID and guarantees quiescence of the
/// whole code page while a write is applied.
/// Plans never infer expected bytes from live code. No multiword transaction is exposed.
/// </summary>
public sealed class Arm64PatchTransaction
{
    private readonly CodeSiteProfile _profile;
    private readonly string _authenticatedBuildId;
    private readonly uint[] _expected;
    private readonly Arm64PatchInstruction _replacement;
    private uint _fingerprintWordCount;

    private Arm64PatchTransaction(CodeSiteProfile profile, string buildId, Arm64PatchInstruction replacement)
    {
        _expected = profile.ExpectedWords.ToArray();
        _profile = profile with
        {
            ExpectedWords = Array.AsReadOnly(_expected),
            ExpectedFingerprint = Array.AsReadOnly(profile.ExpectedFingerprint.ToArray())
        };
        _authenticatedBuildId = buildId;
        _replacement = replacement;
    }

    public string Preview => $"{_profile.BuildId}/{_profile.Profile} {_profile.Owner}/{_profile.SiteId} " +
        $"methodRva=0x{_profile.MethodRva:x} siteRva=0x{(ulong)_profile.MethodRva + _profile.ByteOffset:x} " +
        $"0x{_replacement.Address:x}: {_replacement.Assembly}";

    public static bool TryPlan(CodeSiteProfile profile, string authenticatedBuildId,
        Arm64PatchBuilder assembly, out Arm64PatchTransaction? plan,
        out IReadOnlyList<Arm64AssemblyDiagnostic> diagnostics, uint fingerprintWordCount = 0)
    {
        plan = null;
        if (!CodeSite.TryValidateProfile(profile with { ByteOffset = 0 }, authenticatedBuildId, out var error))
        {
            diagnostics = new[] { new Arm64AssemblyDiagnostic(error.Status.ToString(), "Invalid static method profile.") };
            return false;
        }
        if ((ulong)profile.ExpectedWords.Count * 4 != profile.MethodSize || profile.MethodRva > profile.MethodAddress ||
            (profile.ByteOffset & 3) != 0 || profile.ByteOffset >= profile.MethodSize)
        {
            diagnostics = new[] { new Arm64AssemblyDiagnostic("OutOfRange", "A complete method fingerprint and aligned site are required.") };
            return false;
        }
        if (!assembly.TryBuild(out var instructions, out diagnostics)) return false;
        if (instructions.Count != 1 || instructions[0].Address != profile.SiteAddress ||
            !Arm64Decoder.TryDecode(instructions[0].Address, instructions[0].Word, out _, out _))
        {
            diagnostics = new[] { new Arm64AssemblyDiagnostic("UnsupportedInstruction", "Exactly one valid instruction at the CodeSite address is required.") };
            return false;
        }
        if (fingerprintWordCount != 0 && ((ulong)fingerprintWordCount * 4 > profile.MethodSize - profile.ByteOffset))
        {
            diagnostics = new[] { new Arm64AssemblyDiagnostic("OutOfRange", "Static fingerprint exceeds method bounds.") };
            return false;
        }
        plan = new(profile, authenticatedBuildId, instructions[0]) { _fingerprintWordCount = fingerprintWordCount };
        return true;
    }

    public bool TryValidate(out PatchWriteDiagnostic diagnostic) => Execute(false, false, out diagnostic);

    /// <param name="callerGuaranteesQuiescence">No competing writer may change the target word for the entire call. Execution does not need to stop: native writes one aligned word through /proc/self/mem and never changes page permissions.</param>
    public bool TryApply(bool callerGuaranteesQuiescence, out PatchWriteDiagnostic diagnostic) =>
        Execute(true, callerGuaranteesQuiescence, out diagnostic);

    private unsafe bool Execute(bool write, bool quiescent, out PatchWriteDiagnostic diagnostic)
    {
        var expectedBuild = Identity(_profile.BuildId);
        var actualBuild = Identity(_authenticatedBuildId);
        fixed (uint* expected = _expected)
        {
            var request = new NativePatchRequestV2
            {
                StructSize = Arm64Native.RequestSizeV2,
                AbiVersion = Arm64Native.AbiVersionV2,
                ImageBase = _profile.MethodAddress - _profile.MethodRva,
                MethodRva = _profile.MethodRva,
                MethodByteLength = _profile.MethodSize,
                ByteOffset = _profile.ByteOffset,
                ExpectedBuildIdentityLow = expectedBuild.Low,
                ExpectedBuildIdentityHigh = expectedBuild.High,
                AuthenticatedBuildIdentityLow = actualBuild.Low,
                AuthenticatedBuildIdentityHigh = actualBuild.High,
                OwnerId = Identity(_profile.Owner).Low,
                SiteId = Identity(_profile.Owner + "/" + _profile.SiteId).Low,
                ExpectedWords = (ulong)expected,
                ExpectedWordCount = (uint)_expected.Length,
                ReplacementWordCount = 1,
                Replacement = _replacement.Word,
                FingerprintByteOffset = _fingerprintWordCount != 0 ? _profile.ByteOffset : 0,
                FingerprintWordCount = _fingerprintWordCount,
                Flags = Arm64Native.BuildAuthenticatedV2 | (quiescent ? Arm64Native.QuiescentV2 : 0)
            };
            var status = write ? Arm64Native.TryWritePatchV2(in request, out var native) :
                Arm64Native.TryValidateCodeSiteV2(in request, out native);
            diagnostic = new((PatchStatus)native.Status, native.SystemError, _profile.BuildId,
                _profile.Owner, _profile.SiteId, native.MethodRva, native.SiteRva, native.Address,
                native.ByteOffset, native.Expected, native.Actual, native.WriteState != 0);
            return status == 0;
        }
    }

    private static (ulong Low, ulong High) Identity(string value)
    {
        // Registry tokens, not authenticity proofs. BuildID equality is checked
        // against the loaded ELF before this call. Avoid Android crypto JNI here.
        ulong low = 14695981039346656037UL;
        ulong high = 7809847782465536322UL;
        foreach (var character in value)
        {
            low = unchecked((low ^ character) * 1099511628211UL);
            high = unchecked((high ^ character) * 14029467366897019727UL);
        }
        return (low == 0 ? 1 : low, high == 0 ? 1 : high);
    }
}
