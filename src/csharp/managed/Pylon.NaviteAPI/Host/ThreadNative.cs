using System.Runtime.InteropServices;

namespace Pylon.NaviteAPI.Host;

/// <summary>内核线程身份：判断"当前是不是游戏线程"，以及排查用的 tid。</summary>
internal static partial class ThreadNative
{
    [LibraryImport("pylon", EntryPoint = "PylonMod_IsGameThread")]
    internal static partial int IsGameThread();

    [LibraryImport("pylon", EntryPoint = "PylonMod_GameThreadId")]
    internal static partial long GameThreadId();

    [LibraryImport("pylon", EntryPoint = "PylonMod_CurrentThreadId")]
    internal static partial long CurrentThreadId();
}
