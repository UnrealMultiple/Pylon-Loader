using System.Runtime.InteropServices;

namespace Pylon.NaviteAPI.Host;

/// <summary>内核日志出口：一行一条写进内核 logcat。</summary>
internal static partial class LogNative
{
    [LibraryImport("pylon", EntryPoint = "PylonMod_Log", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void Write(string message);
}
