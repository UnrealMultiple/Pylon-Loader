using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.Injection;

namespace Pylon.Managed.Interop;

/// <summary>Il2CppInterop 内部 detour 的出口：走内核 Dobby，不引入 Harmony。</summary>
internal sealed class PylonDetourProvider : IDetourProvider
{
    public IDetour Create<TDelegate>(IntPtr original, TDelegate target, bool specialReturnBuffer)
        where TDelegate : Delegate => new PylonDetour<TDelegate>(original, target);
}
