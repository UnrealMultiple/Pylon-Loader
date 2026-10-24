using System;
using System.Runtime.InteropServices;

namespace Pylon.NaviteAPI.Host;

/// <summary>内核 x27 注入调用出口（TileSavePipeline 的静态块注入）。</summary>
internal static partial class PatchNative
{
    [LibraryImport("pylon", EntryPoint = "PylonMod_CallWithStatics")]
    internal static partial int CallWithStatics(IntPtr fn, IntPtr statics, IntPtr arg0, IntPtr arg1);
}
