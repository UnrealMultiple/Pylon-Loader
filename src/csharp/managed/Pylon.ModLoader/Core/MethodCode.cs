using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Pylon.NaviteAPI.Host;

namespace Pylon.ModLoader;

/// <summary>
/// 方法体解析与逐条指令补丁（PatchInstruction 的托管面）：klass 来自强类型 store，
/// 方法走 il2cpp 名字 API（解析不到返回 null，没有 GetIl2CppMethod 的假桩问题）；
/// 体指针读 MethodInfo 头部，边界用"同类第一个更靠后的方法"启发式。
/// </summary>
internal static class MethodCode
{
    private const int MaxMethodBytes = 512 * 1024; // 找不到后继方法时的天花板

    /// <summary>强类型类的 klass 指针（未就绪返回 Zero）。</summary>
    public static IntPtr KlassOf<T>() => Il2CppClassPointerStore<T>.NativeClassPtr;

    /// <summary>按运行时类型解析 klass（阶段表里存 System.Type；走 interop 自己的解析面）。</summary>
    public static IntPtr KlassOf(System.Type type) =>
        type is null ? IntPtr.Zero : Il2CppClassPointerStore.GetNativeClassPointer(type);

    /// <summary>解析方法体 [body, end)。解析不到返回 false。</summary>
    public static unsafe bool TryResolve(IntPtr klass, string name, int argc, out IntPtr body, out IntPtr end)
    {
        body = end = IntPtr.Zero;
        if (klass == IntPtr.Zero) return false;

        var method = IL2CPP.il2cpp_class_get_method_from_name(klass, name, argc);
        if (method == IntPtr.Zero) return false;

        var start = Marshal.ReadIntPtr(method); // MethodInfo 头部第一个指针宽字段 = methodPointer
        if (start == IntPtr.Zero) return false;

        var last = start + MaxMethodBytes;
        var iter = IntPtr.Zero;
        IntPtr other;
        while ((other = IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
        {
            var at = Marshal.ReadIntPtr(other);
            if (at > start && at < last) last = at;
        }

        body = start;
        end = last;
        return true;
    }

    /// <summary>换方法体里所有 movz wN, #from 的立即数为 to；返回条数（条数即版本指纹，调用方核对）。</summary>
    public static unsafe int PatchMovzImm16(IntPtr body, IntPtr end, ushort from, ushort to)
    {
        var patched = 0;
        for (var p = (uint*)body; (IntPtr)p < end; p++)
        {
            if ((*p & 0xFFE00000u) != 0x52800000u || ((*p >> 5) & 0xFFFFu) != from) continue;
            if (PatchNative.PatchInstruction((IntPtr)p, 0, *p, (*p & ~(0xFFFFu << 5)) | ((uint)to << 5)) == 0) patched++;
        }

        return patched;
    }

    /// <summary>
    /// 换循环上限：cmp xN, #from + 条件分支（exitOnEqual 只认 b.eq，否则认 b.ne/b.lt/b.lo）。
    /// 返回改写条数，调用方核对。
    /// </summary>
    public static unsafe int PatchLoopEnd(IntPtr body, IntPtr end, int from, int to, bool exitOnEqual)
    {
        var patched = 0;
        for (var p = (uint*)body; (IntPtr)(p + 1) < end; p++)
        {
            if ((*p & 0xFFC0001Fu) != 0xF100001Fu || ((*p >> 10) & 0xFFFu) != (uint)from) continue;
            var next = p[1];
            if ((next & 0xFF000010u) != 0x54000000u) continue;

            var cond = next & 0xFu;
            var keep = exitOnEqual ? cond == 0x0 : cond == 0x1 || cond == 0xB || cond == 0x3;
            if (!keep) continue;

            if (PatchNative.PatchInstruction((IntPtr)p, 0, *p, (*p & ~(0xFFFu << 10)) | ((uint)to << 10)) == 0)
                patched++;
        }

        return patched;
    }
}
