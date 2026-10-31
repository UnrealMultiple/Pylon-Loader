using System.Runtime.InteropServices;

namespace Pylon.Managed.Interop;

/// <summary>内核 Dobby 的 hook 出口。</summary>
internal static partial class HookNative
{
    [LibraryImport("pylon", EntryPoint = "PylonMod_HookAvailable")]
    internal static partial int IsAvailable();

    [LibraryImport("pylon", EntryPoint = "PylonMod_HookApply")]
    internal static partial int Apply(IntPtr target, IntPtr detour, out IntPtr trampoline);

    [LibraryImport("pylon", EntryPoint = "PylonMod_HookRemove")]
    internal static partial int Remove(IntPtr target);
}
