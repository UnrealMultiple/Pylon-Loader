// 层：宿主 —— RuntimeDetour 的原生后端：把引擎的 Apply/Remove 接到内核 Dobby。
//
// Il2CppInterop.RuntimeDetour 不知道任何平台细节；宿主（我们）实现这个抽象类，
// 内核导出 PylonMod_HookAvailable / Apply / Remove 三个函数。
using System;
using Il2CppInterop.RuntimeDetour;

namespace Pylon.Managed
{
    internal sealed class DobbyDetourBackend : DetourBackend
    {
        public override bool IsAvailable
        {
            get
            {
                try { return PylonHookNative.PylonMod_HookAvailable() != 0; }
                catch (Exception e)
                {
                    Entrypoint.Log("内核 hook 出口不可用：" + e.Message);
                    return false;
                }
            }
        }

        public override int Apply(IntPtr target, IntPtr detour, out IntPtr trampoline)
            => PylonHookNative.PylonMod_HookApply(target, detour, out trampoline);

        public override int Remove(IntPtr target) => PylonHookNative.PylonMod_HookRemove(target);
    }
}
