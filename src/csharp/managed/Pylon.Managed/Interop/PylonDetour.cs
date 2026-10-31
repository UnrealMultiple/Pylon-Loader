using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.Injection;

namespace Pylon.Managed.Interop;

/// <summary>一个 Dobby detour 的生命周期；构造即生效，Dispose 时移除。</summary>
internal sealed class PylonDetour<TDelegate> : IDetour where TDelegate : Delegate
{
    // 必须保活：委托被回收后，跳板会跳进已释放的桩。
    private readonly TDelegate _target;

    private bool _disposed;

    public PylonDetour(IntPtr original, TDelegate target)
    {
        if (HookNative.IsAvailable() == 0)
            throw new NotSupportedException("内核 Dobby 不可用（libdobby.so 未加载）");

        _target = target;
        Target = original;
        Detour = Marshal.GetFunctionPointerForDelegate(target);

        var rc = HookNative.Apply(original, Detour, out var trampoline);
        if (rc != 0 || trampoline == IntPtr.Zero)
            throw new InvalidOperationException(
                $"挂钩失败 rc={rc} 跳板=0x{trampoline.ToInt64():x}（目标 0x{original.ToInt64():x}）");

        OriginalTrampoline = trampoline;
        HostLog.Write($"[hook] 已挂 0x{original.ToInt64():x} 跳板=0x{trampoline.ToInt64():x}");
    }

    public IntPtr Target { get; }

    public IntPtr Detour { get; }

    public IntPtr OriginalTrampoline { get; }

    /// <summary>Dobby 在构造时就已生效，这里无需动作。</summary>
    public void Apply() { }

    public T GenerateTrampoline<T>() where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(OriginalTrampoline);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = HookNative.Remove(Target); // 卸载是尽力而为，失败只影响下一次挂钩
        GC.KeepAlive(_target);
    }
}
