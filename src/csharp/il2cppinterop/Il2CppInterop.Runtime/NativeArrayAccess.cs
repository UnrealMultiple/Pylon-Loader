using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Il2CppInterop.Runtime;

/// <summary>Explicit unsafe boundary for proven blittable inline storage. Not for heap/reference arrays.</summary>
public static class NativeArrayAccess
{
    // The caller validates native layout, index, and owner lifetime before invoking this method.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe ref T AsRef<T>(IntPtr address) where T : unmanaged
    {
        if (address == IntPtr.Zero) throw new NullReferenceException();
        return ref Unsafe.AsRef<T>(address.ToPointer());
    }

    public static int ReadLength(IntPtr address) => Marshal.ReadInt32(address);
}
