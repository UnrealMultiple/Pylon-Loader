using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.Runtime;

namespace Il2CppInterop.Runtime;

/// <summary>Preserves the native Nullable payload that ordinary IL2CPP boxing unwraps.</summary>
public static unsafe class RawValueBox
{
    private sealed class Allocation
    {
        internal int Wrappers;
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<IntPtr, Allocation> Owned = new();

    /// <summary>Creates a rooted proxy with the original value type and complete payload.</summary>
    public static T Wrap<T>(IntPtr klass, IntPtr data, int size) where T : Il2CppObjectBase
    {
        if (klass == IntPtr.Zero) throw new ArgumentException("A native class is required", nameof(klass));
        uint align = 0;
        if (!IL2CPP.il2cpp_class_is_valuetype(klass) || IL2CPP.il2cpp_class_value_size(klass, ref align) != size || size <= 0)
            throw new ArgumentException("The payload must match the native value type", nameof(size));
        // The fixed GC allocation is scanned for embedded references and released with its last proxy.
        // Both value_box and object_new erase Nullable's hasValue prefix in this runtime.
        var header = IntPtr.Size * 2;
        var bytes = checked(header + size);
        var address = IL2CPP.il2cpp_gc_alloc_fixed((nuint)bytes);
        if (address == IntPtr.Zero) throw new OutOfMemoryException();
        new Span<byte>(address.ToPointer(), bytes).Clear();
        *(IntPtr*)address = klass;
        if (data != IntPtr.Zero) Buffer.MemoryCopy(data.ToPointer(), (byte*)address + header, size, size);
        lock (Gate) Owned.Add(address, new Allocation());
        try
        {
            return Il2CppObjectPool.Get<T>(address);
        }
        catch
        {
            lock (Gate)
            {
                if (Owned.TryGetValue(address, out var allocation) && allocation.Wrappers == 0)
                {
                    Owned.Remove(address);
                    IL2CPP.il2cpp_gc_free_fixed(address);
                }
            }
            throw;
        }
    }

    internal static IntPtr Retain(IntPtr address)
    {
        lock (Gate)
        {
            if (!Owned.TryGetValue(address, out var allocation)) return IntPtr.Zero;
            allocation.Wrappers++;
            return address;
        }
    }

    internal static void Release(IntPtr address)
    {
        if (address == IntPtr.Zero) return;
        lock (Gate)
        {
            if (!Owned.TryGetValue(address, out var allocation) || --allocation.Wrappers != 0) return;
            Owned.Remove(address);
            IL2CPP.il2cpp_gc_free_fixed(address);
        }
    }
}
