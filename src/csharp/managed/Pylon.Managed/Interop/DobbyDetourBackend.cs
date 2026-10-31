using Il2CppInterop.RuntimeDetour;

namespace Pylon.Managed.Interop;

/// <summary>把 hook 引擎的 Apply / Remove 接到内核 Dobby。</summary>
internal sealed class DobbyDetourBackend : DetourBackend
{
    public override bool IsAvailable
    {
        get
        {
            try
            {
                return HookNative.IsAvailable() != 0;
            }
            catch (Exception e)
            {
                // 探测接口约定永不抛出：Dobby 未加载时这里会失败，按"不可用"处理即可。
                HostLog.Write("内核 hook 出口不可用：" + e.Message);
                return false;
            }
        }
    }

    public override int Apply(IntPtr target, IntPtr detour, out IntPtr trampoline) =>
        HookNative.Apply(target, detour, out trampoline);

    public override int Remove(IntPtr target) => HookNative.Remove(target);
}
