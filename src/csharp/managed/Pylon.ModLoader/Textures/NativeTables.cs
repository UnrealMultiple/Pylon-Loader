using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace Pylon.ModLoader.Textures;

/// <summary>
/// 原生原语的薄封装：按名字找类/字段、读写成静态数组、把数组扩长。
/// 这里<b>不</b>知道任何具体内容类型 —— 扩哪一类表由各管线自己的 <see cref="TableSet"/> 描述，
/// 引擎见 <see cref="TableGrower"/>。
/// </summary>
internal static unsafe class NativeTables
{
    private const uint FieldStatic = 0x0010;    // FieldAttributes.Static
    private const uint FieldLiteral = 0x0040;   // FieldAttributes.Literal（const）
    private const int TypeSzArray = 0x1D;       // Il2CppTypeEnum.SZARRAY（T[]，含交错数组）

    private static IntPtr _image;

    /// <summary>Assembly-CSharp 的 il2cpp image（清单里的类都在这里）。</summary>
    private static IntPtr Image
    {
        get
        {
            if (_image != IntPtr.Zero) return _image;
            try
            {
                var item = Il2CppClassPointerStore<Terraria.Item>.NativeClassPtr;
                if (item != IntPtr.Zero) _image = IL2CPP.il2cpp_class_get_image(item);
            }
            catch { /* 下面还有按程序集遍历的兜底 */ }

            if (_image == IntPtr.Zero) _image = FindImage("Assembly-CSharp.dll");
            return _image;
        }
    }

    /// <summary>
    /// 目录表用的字段级指针访问（只给"按类名 + 字段名找表"这段老逻辑用；管线扩容走 <see cref="ArrayGrow"/>）。
    /// 偏移见 refs/il2cpp.h。
    /// </summary>
    internal static unsafe class Meta
    {
        private const int ClassFields = 0x80;
        private const int ClassStaticFields = 0xB8;
        private const int FieldParentOff = 0x10;
        private const int ArrLength = 0x18;
        private const int ArrDataOff = 0x20;
        private const int ThreadStatic = -1;

        private static byte* B(IntPtr p) => (byte*)p;

        internal static IntPtr ParentOf(IntPtr field) => field == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)(B(field) + FieldParentOff);


        /// <summary>字段类型是不是 T[]（走 il2cpp 的 type 枚举，不自己解 Il2CppType.bits）。</summary>
        internal static bool IsSzArray(IntPtr field) =>
            IL2CPP.il2cpp_type_get_type(IL2CPP.il2cpp_field_get_type(field)) == TypeSzArray;

        internal static bool IsPlainStatic(IntPtr field)
        {
            var attrs = (uint)IL2CPP.il2cpp_field_get_flags(field);
            if ((attrs & FieldStatic) == 0 || (attrs & FieldLiteral) != 0) return false;
            return unchecked((int)IL2CPP.il2cpp_field_get_offset(field)) != ThreadStatic;
        }

        internal static bool NameIs(IntPtr field, string text) =>
            string.Equals(IL2CPP.il2cpp_field_get_name_(field), text, StringComparison.Ordinal);


        /// <summary>
        /// 读静态字段。走 il2cpp 自己的 API（<c>field_static_get_value</c>）：
        /// 它内部处理 static_fields 基址与 thread-static 两种存储，比自己算地址稳。
        /// </summary>
        internal static unsafe IntPtr ReadStaticPtr(IntPtr klass, IntPtr field)
        {
            if (field == IntPtr.Zero) return IntPtr.Zero;
            var value = IntPtr.Zero;
            IL2CPP.il2cpp_field_static_get_value(field, &value);
            return value;
        }

        /// <summary>写静态字段（同样交给 il2cpp，引用类型的写屏障由它负责）。</summary>
        internal static unsafe void WriteStaticPtr(IntPtr klass, IntPtr field, IntPtr value)
        {
            if (field == IntPtr.Zero) return;
            IL2CPP.il2cpp_field_static_set_value(field, (void*)value);
        }

        internal static int Items(IntPtr array) => array == IntPtr.Zero ? 0 : (int)*(long*)(B(array) + ArrLength);
        internal static byte* Data(IntPtr array) => array == IntPtr.Zero ? null : B(array) + ArrDataOff;
    }

    /// <summary>按名字在域里的程序集找 image（GetIl2CppImage 是私有的，这里自己走一遍）。</summary>
    private static unsafe IntPtr FindImage(string imageName)
    {
        try
        {
            var domain = IL2CPP.il2cpp_domain_get();
            if (domain == IntPtr.Zero) return IntPtr.Zero;
            uint count = 0;
            var assemblies = IL2CPP.il2cpp_domain_get_assemblies(domain, ref count);
            if (assemblies == null) return IntPtr.Zero;
            for (var i = 0; i < count; i++)
            {
                var image = IL2CPP.il2cpp_assembly_get_image(assemblies[i]);
                if (image == IntPtr.Zero) continue;
                if (string.Equals(IL2CPP.il2cpp_image_get_name_(image), imageName, StringComparison.Ordinal))
                    return image;
            }
        }
        catch { /* il2cpp 镜像列表还不稳定：扫不到就当没找到，调用方会再试 */ }
        return IntPtr.Zero;
    }

    /// <summary>按 (命名空间, 类名, 嵌套名) 找原生 class；找不到返回 0。</summary>
    internal static IntPtr FindClass(TableClass spec)
    {
        try
        {
            var image = Image;
            if (image == IntPtr.Zero) return IntPtr.Zero;
            var cls = IL2CPP.il2cpp_class_from_name(image, spec.Ns, spec.Name);
            if (cls == IntPtr.Zero) return IntPtr.Zero;
            if (!string.IsNullOrEmpty(spec.Nested)) cls = FindNested(cls, spec.Nested);
            if (cls != IntPtr.Zero && !string.IsNullOrEmpty(spec.Nested2)) cls = FindNested(cls, spec.Nested2);
            return cls;
        }
        catch { return IntPtr.Zero; }
    }

    internal static IntPtr FindNested(IntPtr parent, string name)
    {
        var iter = IntPtr.Zero;
        while (true)
        {
            var nested = IL2CPP.il2cpp_class_get_nested_types(parent, ref iter);
            if (nested == IntPtr.Zero) return IntPtr.Zero;
            if (string.Equals(IL2CPP.il2cpp_class_get_name_(nested), name, StringComparison.Ordinal))
                return nested;
        }
    }

    /// <summary>字段类型是不是 T[] 一维数组（多维 [,] 是 ARRAY，不算）。</summary>
    private static bool IsArrayField(IntPtr field) => Meta.IsSzArray(field);

    internal static bool IsSkipped(TableClass spec, IntPtr field)
    {
        var skip = spec.Skip;
        if (skip is not { Length: > 0 }) return false;
        foreach (var candidate in skip)
            if (Meta.NameIs(field, candidate)) return true;
        return false;
    }

    /// <summary>
    /// 收集这个类的全部"静态 T[] 字段"。走 il2cpp 自己的字段枚举迭代器 —— 它会先跑类初始化、
    /// 也会过滤掉 <c>klass-&gt;fields</c> 里那些还没定型的槽位；自己按下标解 <c>klass-&gt;fields</c>
    /// 曾经在真机上扫到过非 FieldInfo 的指针，扩表直接把内存写坏（fault addr 都是 ASCII 字符串）。
    /// </summary>
    internal static void CollectFields(IntPtr cls, TableClass spec, List<IntPtr> into)
    {
        var iter = IntPtr.Zero;
        while (true)
        {
            var field = IL2CPP.il2cpp_class_get_fields(cls, ref iter);
            if (field == IntPtr.Zero) return;
            if (!Meta.IsPlainStatic(field)) continue;
            if (IsSkipped(spec, field)) continue;
            if (!IsArrayField(field)) continue;
            into.Add(field);
        }
    }

    /// <summary>读静态字段里的数组对象指针（0 = 还没建 / 不是引用）。</summary>
    internal static IntPtr ReadStatic(IntPtr field) =>
        Meta.ReadStaticPtr(Meta.ParentOf(field), field);

    /// <summary>写回静态字段（落值 + GC 写屏障）。</summary>
    internal static void WriteStatic(IntPtr field, IntPtr array) =>
        Meta.WriteStaticPtr(Meta.ParentOf(field), field, array);

    internal static int ArrayLength(IntPtr array) => Meta.Items(array);

    /// <summary>
    /// 值类型元素按 <c>il2cpp_class_value_size</c> 整块拷；引用元素逐个走 GC 写屏障。
    /// <para>
    /// 引用元素<b>不能</b>用 memcpy 整块搬：那等于绕过增量 GC 的写屏障。新数组如果在标记
    /// 阶段被分配（表一大就会分配很多次，必然撞上），它已经被标黑、不会再被扫描，此时用
    /// memcpy 塞进去的指针没人去标记，下一轮回收就可能把那个对象收走 —— 现场表现就是十几
    /// 秒后一段完全无关的游戏代码读到野指针 SIGSEGV。<c>TypeTables::growArray</c>
    /// 对引用元素改用 <c>Array.Copy</c> / <c>Array.SetValue</c>，要的就是这个屏障。
    /// </para>
    /// <paramref name="fillWithMode"/> 时尾部填"原数组出现最多的值"（方块/墙壁必须用它：
    /// 填 0 会把新方块变成"实心泥土"）。
    /// </summary>
    internal static IntPtr GrowArray(IntPtr oldArray, int newCapacity, bool fillWithMode = false) =>
        ArrayGrow.Grow(oldArray, newCapacity, fillWithMode);


}
