using System.Runtime.InteropServices;

namespace Pylon.NaviteAPI.Host;

internal static partial class CodePageNative
{
    internal const int MaxBytes = 0x10000;
    internal const int ReachBytes = 120 * 1024 * 1024;
    internal const int PageBytes = 0x400;

    [LibraryImport("pylon", EntryPoint = "PylonMod_ReserveCodePage")]
    internal static partial int Reserve(ulong nearAddress, uint byteSize, out ulong pageAddress);

    [LibraryImport("pylon", EntryPoint = "PylonMod_SealCodePage")]
    internal static unsafe partial int Seal(ulong pageAddress, byte* bytes, uint byteLength);

    [LibraryImport("pylon", EntryPoint = "PylonMod_ReleaseCodePage")]
    internal static partial int Release(ulong pageAddress);
}
