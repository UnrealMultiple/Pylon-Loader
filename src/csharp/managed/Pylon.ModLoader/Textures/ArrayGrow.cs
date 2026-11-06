using System;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace Pylon.ModLoader.Textures;

/// <summary>
/// 扩表工具：把一张 il2cpp 数组扩到目标长度，就这一件事。
/// <para>
/// 不按名字找字段、不扫类、不建 interop 包装对象、不走 System.Reflection —— 要扩哪张表由调用方
/// （各管线）自己用 interop 生成的静态属性取出来传入，跟
/// <c>WaterStylesLoader.Grow(ref GameArray&lt;GameObject&gt;, int)</c> 一个形状。
/// </para>
/// <para>
/// 元素按"值类型整块 memcpy / 引用元素逐格走 GC 写屏障"两条路走（
/// <c>TypeTables::growArray</c> 对引用元素用 Array.Copy 要的就是这个屏障）。
/// 分配用 <c>il2cpp_array_new(elementClass, n)</c>，不用 <c>array_new_specific</c>：
/// 前者会把数组类的 GC 描述符按元素类型配好。
/// </para>
/// </summary>
internal static unsafe class ArrayGrow
{
    // Il2CppArray：klass 0x00, monitor 0x08, bounds 0x10, max_length 0x18, 元素区 0x20
    private const int ArrData = 0x20;

    /// <summary>数组当前长度（0 = 还没建 / 不是数组）。走 il2cpp 官方 API，不自己算偏移。</summary>
    public static int Length(IntPtr array) =>
        array == IntPtr.Zero ? 0 : (int)IL2CPP.il2cpp_array_length(array);

    /// <summary>数组元素区起点：对象头（2 指针）+ bounds + max_length = 4 个指针。</summary>
    public static byte* Data(IntPtr array) => array == IntPtr.Zero ? null : (byte*)array + ArrData;

    /// <summary>数组的元素类。同样走官方 API（object_get_class + class_get_element_class）。</summary>
    public static IntPtr ElementClassOf(IntPtr array)
    {
        if (array == IntPtr.Zero) return IntPtr.Zero;
        var klass = IL2CPP.il2cpp_object_get_class(array);
        return klass == IntPtr.Zero ? IntPtr.Zero : IL2CPP.il2cpp_class_get_element_class(klass);
    }

    /// <summary>
    /// 把 <paramref name="oldArray"/> 扩到 <paramref name="total"/> 长，返回新数组；失败返回 0。
    /// 已经够长直接返回 0（调用方据此判断"没动"）。
    /// </summary>
    /// <param name="fillWithMode">
    /// 尾部填"原数组里出现最多的那个值"而不是 [0]：方块/墙壁表必须用 —— 填 0 会让新方块
    /// 变成实心泥土、不发光、不参与边框。
    /// </param>
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

    /// <summary>引用元素表：<c>Asset&lt;Texture2D&gt;[]</c> / <c>TextureMask[]</c> 这类。</summary>
    public static bool Grow<T>(ref Il2CppReferenceArray<T>? table, int total) where T : Il2CppObjectBase
    {
        var old = table?.Pointer ?? IntPtr.Zero;
        var grown = Grow(old, total);
        if (grown == IntPtr.Zero) return false;
        table = new Il2CppReferenceArray<T>(grown);
        return true;
    }

    /// <summary>值类型元素表：<c>bool[]</c> / <c>int[]</c> / <c>Nullable&lt;T&gt;[]</c> 这类。</summary>
    public static bool Grow<T>(ref Il2CppStructArray<T>? table, int total) where T : unmanaged
    {
        var old = table?.Pointer ?? IntPtr.Zero;
        var grown = Grow(old, total);
        if (grown == IntPtr.Zero) return false;
        table = new Il2CppStructArray<T>(grown);
        return true;
    }

    // ===================== 拷贝 =====================

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

    /// <summary>元素字节里出现次数最多的下标（一趟哈希计数，O(n)）。</summary>
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
