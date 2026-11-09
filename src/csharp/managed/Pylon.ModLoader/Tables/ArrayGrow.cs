using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace Pylon.ModLoader.Tables;

/// <summary>把一张 il2cpp 数组扩到目标长度。引用元素逐格走 GC 写屏障，不能整块 memcpy。</summary>
internal static unsafe class ArrayGrow
{
    // Il2CppArray：对象头 2 指针 + bounds + max_length，元素区在 0x20
    private const int ArrData = 0x20;

    /// <summary>数组长度（0 = 还没建）。</summary>
    public static int Length(IntPtr array) =>
        array == IntPtr.Zero ? 0 : (int)IL2CPP.il2cpp_array_length(array);

    /// <summary>元素区起点。</summary>
    public static byte* Data(IntPtr array) => array == IntPtr.Zero ? null : (byte*)array + ArrData;

    /// <summary>数组的元素类。</summary>
    public static IntPtr ElementClassOf(IntPtr array)
    {
        if (array == IntPtr.Zero) return IntPtr.Zero;
        var klass = IL2CPP.il2cpp_object_get_class(array);
        return klass == IntPtr.Zero ? IntPtr.Zero : IL2CPP.il2cpp_class_get_element_class(klass);
    }

    /// <summary>扩到 total 长并返回新数组；失败或已经够长返回 0。</summary>
    /// <param name="fillWithMode">尾部填原数组的众数（方块/墙壁用；填 0 会变成实心泥土）。</param>
    public static IntPtr Grow(IntPtr oldArray, int total, bool fillWithMode = false)
    {
        if (oldArray == IntPtr.Zero || total <= 0) return IntPtr.Zero;

        var oldLength = Length(oldArray);
        if (oldLength >= total) return IntPtr.Zero;

        var elementClass = ElementClassOf(oldArray);
        if (elementClass == IntPtr.Zero) return IntPtr.Zero;

        var isValueType = IL2CPP.il2cpp_class_is_valuetype(elementClass);
        var elementSize = IntPtr.Size;
        if (isValueType)
        {
            var align = 0u;
            elementSize = IL2CPP.il2cpp_class_value_size(elementClass, ref align);
        }
        if (elementSize <= 0) return IntPtr.Zero;

        var grown = IL2CPP.il2cpp_array_new(elementClass, (ulong)total);
        if (grown == IntPtr.Zero) return IntPtr.Zero;

        return isValueType
            ? CopyPlain(Data(oldArray), grown, oldLength, total, elementSize, fillWithMode)
            : CopyRefs(oldArray, grown, oldLength, total, fillWithMode);
    }

    /// <summary>引用元素表，如 <c>Asset&lt;Texture2D&gt;[]</c>。</summary>
    public static bool Grow<T>(ref Il2CppReferenceArray<T>? table, int total) where T : Il2CppObjectBase
    {
        var old = table?.Pointer ?? IntPtr.Zero;
        var grown = Grow(old, total);
        if (grown == IntPtr.Zero) return false;
        table = new Il2CppReferenceArray<T>(grown);
        return true;
    }

    /// <summary>值类型元素表，如 <c>bool[]</c> / <c>int[]</c>。</summary>
    public static bool Grow<T>(ref Il2CppStructArray<T>? table, int total) where T : unmanaged
    {
        var old = table?.Pointer ?? IntPtr.Zero;
        var grown = Grow(old, total);
        if (grown == IntPtr.Zero) return false;
        table = new Il2CppStructArray<T>(grown);
        return true;
    }

    private static IntPtr CopyPlain(byte* src, IntPtr grown, int oldLength, int total, int elementSize, bool fillWithMode)
    {
        var dst = Data(grown);
        var bytes = (long)total * elementSize;
        var copy = (long)oldLength * elementSize;
        if (copy > 0) Buffer.MemoryCopy(src, dst, bytes, copy);

        if (total > oldLength)
        {
            var pick = fillWithMode ? ModeIndex(src, oldLength, elementSize) : 0;
            var model = src + (long)pick * elementSize;
            for (var i = oldLength; i < total; i++)
                Buffer.MemoryCopy(model, dst + (long)i * elementSize, elementSize, elementSize);
        }
        return grown;
    }

    private static IntPtr CopyRefs(IntPtr oldArray, IntPtr grown, int oldLength, int total, bool fillWithMode)
    {
        var src = Data(oldArray);
        var dst = Data(grown);
        var model = ModelValue(src, oldLength, fillWithMode);
        for (var i = 0; i < total; i++)
        {
            var value = i < oldLength ? *(IntPtr*)(src + (long)i * IntPtr.Size) : model;
            IL2CPP.il2cpp_gc_wbarrier_set_field(grown, (IntPtr)(dst + (long)i * IntPtr.Size), value);
        }
        return grown;
    }

    private static IntPtr ModelValue(byte* data, int length, bool fillWithMode)
    {
        if (length <= 0 || data == null) return IntPtr.Zero;
        var pick = fillWithMode ? ModeIndex(data, length, IntPtr.Size) : 0;
        return *(IntPtr*)(data + (long)pick * IntPtr.Size);
    }

    /// <summary>出现次数最多的元素下标。</summary>
    private static int ModeIndex(byte* data, int length, int elementSize)
    {
        if (length <= 0 || elementSize <= 0 || elementSize > 8) return 0;

        var counts = new Dictionary<ulong, int>(length);
        var first = new Dictionary<ulong, int>(length);
        var best = 0;
        var bestCount = 0;
        for (var i = 0; i < length; i++)
        {
            ulong value = 0;
            var at = data + (long)i * elementSize;
            for (var b = 0; b < elementSize; b++) value |= (ulong)at[b] << (8 * b);

            counts.TryGetValue(value, out var count);
            count++;
            counts[value] = count;
            if (count == 1) first[value] = i;
            if (count <= bestCount) continue;
            bestCount = count;
            best = first[value];
        }
        return best;
    }
}
