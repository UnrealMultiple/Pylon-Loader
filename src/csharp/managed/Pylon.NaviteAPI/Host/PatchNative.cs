using System;
using System.Runtime.InteropServices;

namespace Pylon.NaviteAPI.Host;

/// <summary>内核指令改写出口：把游戏里写死的 tile 上限立即数改掉，并回读上一次错误。</summary>
internal static partial class PatchNative
{
    [LibraryImport("pylon", EntryPoint = "PylonMod_PatchCompareLimit", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int CompareLimit(string type, string method, int argc, uint oldLimit, uint newLimit,
        int belowToo, int shifted);

    [LibraryImport("pylon", EntryPoint = "PylonMod_PatchInstruction")]
    internal static partial int PatchInstruction(IntPtr body, int byteOffset, uint expected, uint replacement);

    [LibraryImport("pylon", EntryPoint = "PylonMod_CallWithStatics")]
    internal static partial int CallWithStatics(IntPtr fn, IntPtr statics, IntPtr arg0, IntPtr arg1);

    [LibraryImport("pylon", EntryPoint = "PylonMod_LastError")]
    internal static partial IntPtr LastError();
}
