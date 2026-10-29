#nullable disable
// 层：Hook 引擎 —— "class 代理但原生是值类型"（Il2CppSystem.ValueType 派生）的镜像结构。
//
// Il2CppInterop 把泛型值类型（Il2CppSystem.Nullable<T> 等）生成为 class，托管签名里是对象；
// 但 IL2CPP 编译出来的原生方法按值（寄存器/栈）接收结构体。两边要靠：
//   入口：镜像结构按值收下 → il2cpp_value_box(class, data) → 包装成代理对象
//   orig：代理对象 → il2cpp_object_unbox → 拷进镜像结构 → 按值 calli
// 镜像结构只关心"大小 + 整数类 ABI"：显式布局、ClassSize = il2cpp_class_value_size，
// 单个 byte 字段保证不被判成 HFA（Nullable 里总有 bool，本来也不是 HFA）。
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Il2CppInterop.Runtime;

namespace Il2CppInterop.RuntimeDetour
{
    internal sealed class MirrorInfo
    {
        public Type Boxed;
        public Type Mirror;
        public int Size;
        public IntPtr ClassPointer;
    }

    internal static class MirrorTypes
    {
        private static readonly Dictionary<Type, MirrorInfo> ByBoxed = new();
        private static readonly Dictionary<int, Type> BySize = new();
        private static readonly object Gate = new();

        private static readonly ModuleBuilder Module = AssemblyBuilder
            .DefineDynamicAssembly(new AssemblyName("Pylon.BoxedValues"), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("Pylon.BoxedValues");

        internal static MirrorInfo For(Type boxed)
        {
            lock (Gate)
            {
                if (ByBoxed.TryGetValue(boxed, out var cached)) return cached;

                var classPointer = Il2CppClassPointerStore.GetNativeClassPointer(boxed);
                if (classPointer == IntPtr.Zero)
                    throw new NotSupportedException($"拿不到 {boxed.FullName} 的 il2cpp class 指针");

                uint align = 0;
                var size = IL2CPP.il2cpp_class_value_size(classPointer, ref align);
                if (size <= 0)
                    throw new NotSupportedException($"{boxed.FullName} 的 il2cpp 值大小是 {size}");

                if (!BySize.TryGetValue(size, out var mirror))
                {
                    mirror = DefineMirror(size);
                    BySize[size] = mirror;
                }

                var info = new MirrorInfo
                {
                    Boxed = boxed,
                    Mirror = mirror,
                    Size = size,
                    ClassPointer = classPointer,
                };
                ByBoxed[boxed] = info;
                return info;
            }
        }

        private static Type DefineMirror(int size)
        {
            // 显式布局 + ClassSize：不引入任何 HFA 分类，>16 字节自然走内存传参。
            var builder = Module.DefineType(
                "PylonBoxedValue_" + size,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.ExplicitLayout,
                typeof(ValueType),
                size);
            var field = builder.DefineField("data", typeof(byte), FieldAttributes.Public);
            field.SetOffset(0);
            return builder.CreateType();
        }
    }
}


