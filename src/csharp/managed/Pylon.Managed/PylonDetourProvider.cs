using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.Injection;

namespace Pylon.Managed
{
    /// <summary>内核 hook 出口的 P/Invoke。C# 不允许泛型类型带 DllImport，所以单独放这里。</summary>
    internal static class PylonHookNative
    {
        [DllImport("pylon")]
        internal static extern int PylonMod_HookAvailable();

        [DllImport("pylon")]
        internal static extern int PylonMod_HookApply(IntPtr target, IntPtr detour, out IntPtr trampoline);

        [DllImport("pylon")]
        internal static extern int PylonMod_HookRemove(IntPtr target);
    }

    internal sealed class PylonDetourProvider : IDetourProvider
    {
        public IDetour Create<TDelegate>(IntPtr original, TDelegate target, bool specialReturnBuffer)
            where TDelegate : Delegate
            => new PylonDetour<TDelegate>(original, target);
    }

    internal sealed class PylonDetour<TDelegate> : IDetour where TDelegate : Delegate
    {
        // 必须保活：被 GC 掉的话跳板会跳进已释放的桩。
        private readonly TDelegate _target;
        private bool _disposed;

        public PylonDetour(IntPtr original, TDelegate target)
        {
            _target = target;
            Target = original;
            Detour = Marshal.GetFunctionPointerForDelegate(target);

            if (PylonHookNative.PylonMod_HookAvailable() == 0)
            {
                throw new NotSupportedException("内核 Dobby 不可用（libdobby.so 未加载）");
            }
            var rc = PylonHookNative.PylonMod_HookApply(original, Detour, out var trampoline);
            if (rc != 0 || trampoline == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"挂钩失败 rc={rc} 跳板=0x{trampoline.ToInt64():x}（目标 0x{original.ToInt64():x}）");
            }
            OriginalTrampoline = trampoline;
            Entrypoint.Log($"[hook] 已挂 0x{original.ToInt64():x} 跳板=0x{trampoline.ToInt64():x}");
        }

        public IntPtr Target { get; }

        public IntPtr Detour { get; }

        public IntPtr OriginalTrampoline { get; }

        /// <summary>Dobby 在构造时就已生效，此接口方法无需动作。</summary>
        public void Apply() { }

        public T GenerateTrampoline<T>() where T : Delegate
            => Marshal.GetDelegateForFunctionPointer<T>(OriginalTrampoline);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            PylonHookNative.PylonMod_HookRemove(Target);
            GC.KeepAlive(_target);
        }
    }
}
