using System.Runtime.InteropServices;

namespace Pylon.NaviteAPI.Host;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCodeSite
{
    internal ulong Address;
    internal uint ByteLength;
    internal uint Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePatchDiagnostic
{
    internal int Status;
    internal int SystemError;
    internal ulong Address;
    internal uint Expected;
    internal uint Actual;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeArm64Instruction
{
    internal uint Word;
    internal uint Kind;
    internal long Displacement;
    internal ulong Target;
}

// V2 identities are opaque 128-bit values authenticated by managed policy against
// a trusted build. Native compares them; it does not establish image authenticity.
[StructLayout(LayoutKind.Sequential, Size = 120)]
internal struct NativePatchRequestV2
{
    internal uint StructSize;
    internal uint AbiVersion;
    internal ulong ImageBase;
    internal ulong MethodRva;
    internal uint MethodByteLength;
    internal uint ByteOffset;
    internal ulong ExpectedBuildIdentityLow;
    internal ulong ExpectedBuildIdentityHigh;
    internal ulong AuthenticatedBuildIdentityLow;
    internal ulong AuthenticatedBuildIdentityHigh;
    internal ulong OwnerId;
    internal ulong SiteId;
    internal ulong ExpectedWords; // pinned uint buffer covering the complete method
    internal uint ExpectedWordCount;
    internal uint ReplacementWordCount; // must be 1; no multiword transaction
    internal uint Replacement;
    internal uint Flags;
    internal uint FingerprintByteOffset;
    internal uint FingerprintWordCount;
    internal ulong Reserved;
}

[StructLayout(LayoutKind.Sequential, Size = 64)]
internal struct NativePatchDiagnosticV2
{
    internal uint StructSize;
    internal uint AbiVersion;
    internal int Status;
    internal int SystemError;
    internal ulong MethodRva;
    internal ulong SiteRva;
    internal ulong Address;
    internal uint ByteOffset;
    internal uint Expected;
    internal uint Actual;
    internal uint WriteState; // 0 = not written, 1 = written, even on restore failure
    internal ulong Reserved;
}

[StructLayout(LayoutKind.Sequential, Size = 80)]
internal unsafe struct NativeCodeImage
{
    internal ulong ImageBase;
    internal uint BuildIdLength;
    internal uint Reserved;
    internal fixed byte BuildId[64];
}

internal static partial class Arm64Native
{
    [LibraryImport("pylon", EntryPoint = "PylonMod_TryGetCodeImage")]
    internal static partial int TryGetCodeImage(ulong address, out NativeCodeImage image);
    internal const uint AbiVersionV2 = 2;
    internal const uint RequestSizeV2 = 120;
    internal const uint BuildAuthenticatedV2 = 1;
    internal const uint QuiescentV2 = 2;

    // Same status integers as v1. Owner/site registry reservations last for the process.
    [LibraryImport("pylon", EntryPoint = "PylonMod_TryValidateCodeSiteV2")]
    internal static partial int TryValidateCodeSiteV2(in NativePatchRequestV2 request, out NativePatchDiagnosticV2 diagnostic);

    [LibraryImport("pylon", EntryPoint = "PylonMod_TryWritePatchV2")]
    internal static partial int TryWritePatchV2(in NativePatchRequestV2 request, out NativePatchDiagnosticV2 diagnostic);
    [LibraryImport("pylon", EntryPoint = "PylonMod_CodeSiteCreate")]
    internal static partial int Create(ulong address, uint byteLength, out NativeCodeSite site, out NativePatchDiagnostic diagnostic);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Arm64Read")]
    internal static partial int Read(in NativeCodeSite site, uint byteOffset, out uint word, out NativePatchDiagnostic diagnostic);

    // Deprecated v1 decode entry points retained for binary/source compatibility.
    [LibraryImport("pylon", EntryPoint = "PylonMod_Arm64Decode")]
    internal static partial int Decode(ulong pc, uint word, out NativeArm64Instruction instruction, out NativePatchDiagnostic diagnostic);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Arm64PcRelative")]
    internal static partial int PcRelative(ulong pc, uint word, out ulong target, out NativePatchDiagnostic diagnostic);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Arm64Validate")]
    internal static unsafe partial int Validate(in NativeCodeSite site, uint byteOffset, uint* expected, uint count, out NativePatchDiagnostic diagnostic);

    [LibraryImport("pylon", EntryPoint = "PylonMod_Arm64Write")]
    internal static partial int Write(in NativeCodeSite site, uint byteOffset, uint expected, uint replacement, out NativePatchDiagnostic diagnostic);

    [LibraryImport("pylon", EntryPoint = "PylonMod_TryValidateCodeSite")]
    internal static unsafe partial int TryValidateCodeSite(in NativeCodeSite site, uint byteOffset, uint* expected, uint count, out NativePatchDiagnostic diagnostic);

    [LibraryImport("pylon", EntryPoint = "PylonMod_TryReadInstruction32")]
    internal static partial int TryReadInstruction32(in NativeCodeSite site, uint byteOffset, out uint word, out NativePatchDiagnostic diagnostic);

    [LibraryImport("pylon", EntryPoint = "PylonMod_TryReadWords")]
    internal static unsafe partial int TryReadWords(in NativeCodeSite site, uint byteOffset, uint* words, uint count, out NativePatchDiagnostic diagnostic);

    [LibraryImport("pylon", EntryPoint = "PylonMod_TryWriteInstruction32")]
    internal static partial int TryWriteInstruction32(in NativeCodeSite site, uint byteOffset, uint expected, uint replacement, out NativePatchDiagnostic diagnostic);

    [LibraryImport("pylon", EntryPoint = "PylonMod_TryWritePatch")]
    internal static partial int TryWritePatch(in NativeCodeSite site, uint byteOffset, uint expected, uint replacement, out NativePatchDiagnostic diagnostic);
}
