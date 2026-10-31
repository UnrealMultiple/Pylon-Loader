using Il2CppInterop.RuntimeDetour;

namespace Pylon.Managed.Interop;

/// <summary>唯一的 hook 引擎：日志接到宿主，原生后端接到内核 Dobby。</summary>
internal static class HookEngine
{
    /// <summary>必须在任何 On.* 订阅之前调用。</summary>
    public static void Attach()
    {
        HookLog.Info = HostLog.Write;
        HookLog.Error = CrashGuard.Report; // 引擎已捕获 handler 异常，这里只负责记录与去重
        DetourBackend.Current = new DobbyDetourBackend();
    }
}
