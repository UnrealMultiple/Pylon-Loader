using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Il2CppInterop.Runtime;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>Metadata about a boxed value type ("class proxy that is a native value type").</summary>
internal sealed class MirrorInfo
{
    internal MirrorInfo(Type boxed, Type mirror, int size, IntPtr classPointer)
    {
        Boxed = boxed;
        Mirror = mirror;
        Size = size;
        ClassPointer = classPointer;
    }

    public Type Boxed { get; }
    public Type Mirror { get; }
    public int Size { get; }
    public IntPtr ClassPointer { get; }
}

/// <summary>
///     Provides the "mirror structs" used to pass boxed value types by value.
///     Il2CppInterop generates generic value types (Il2CppSystem.Nullable&lt;T&gt; etc.) as classes,
///     but IL2CPP compiles the native signatures with the struct passed by value. The bridge is:
///       entry: mirror struct receives the value -> il2cpp_value_box(class, data) -> wrap in proxy object
///       orig:  proxy object -> il2cpp_object_unbox -> copy into mirror struct -> calli by value
///     The mirror struct only cares about "size + integer ABI": explicit layout with
///     ClassSize = il2cpp_class_value_size and a single byte field so HFA classification
///     never applies (Nullable always contains a bool, which is not an HFA anyway).
/// </summary>
internal static class MirrorTypes
{
    private static readonly Dictionary<Type, MirrorInfo> s_byBoxed = new();
    private static readonly Dictionary<int, Type> s_bySize = new();
    private static readonly object s_gate = new();

    private static readonly ModuleBuilder s_module = AssemblyBuilder
        .DefineDynamicAssembly(new AssemblyName("Pylon.BoxedValues"), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("Pylon.BoxedValues");

    internal static MirrorInfo For(Type boxed)
    {
        lock (s_gate)
        {
            if (s_byBoxed.TryGetValue(boxed, out var cached)) return cached;

            var classPointer = Il2CppClassPointerStore.GetNativeClassPointer(boxed);
            if (classPointer == IntPtr.Zero)
                throw new NotSupportedException($"Unable to get the il2cpp class pointer for {boxed.FullName}");

            uint align = 0;
            var size = IL2CPP.il2cpp_class_value_size(classPointer, ref align);
            if (size <= 0)
                throw new NotSupportedException($"{boxed.FullName} has an invalid il2cpp value size of {size}");

            if (!s_bySize.TryGetValue(size, out var mirror))
            {
                mirror = DefineMirror(size);
                s_bySize[size] = mirror;
            }

            var info = new MirrorInfo(boxed, mirror, size, classPointer);
            s_byBoxed[boxed] = info;
            return info;
        }
    }

    private static Type DefineMirror(int size)
    {
        // Explicit layout + ClassSize: no HFA classification, values larger than 16 bytes are
        // naturally passed by memory.
        var builder = s_module.DefineType(
            "PylonBoxedValue_" + size,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.ExplicitLayout,
            typeof(ValueType),
            size);
        var field = builder.DefineField("data", typeof(byte), FieldAttributes.Public);
        field.SetOffset(0);
        return builder.CreateTypeInfo().AsType();
    }
}
