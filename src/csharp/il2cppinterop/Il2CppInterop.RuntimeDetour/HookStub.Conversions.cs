using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>
///     The slot conversion emitters shared by the entry and orig stubs. Every helper transforms
///     the value on top of the IL evaluation stack in place (native slot form &lt;-&gt; managed form).
/// </summary>
internal static partial class HookStub
{
    // ---------------- native -> managed ----------------

    /// <summary>The value on top of the stack is a native slot value; convert it to the managed value.</summary>
    private static void EmitNativeToManaged(ILGenerator il, Type managed, Type native)
    {
        if (managed == native) return;

        if (HookPlan.IsBoxedValueType(managed))
        {
            // 大结构（>16 字节）：native 传来的是隐藏指针（指向调用者的值副本），直接 box 成 proxy。
            if (native == typeof(IntPtr))
            {
                EmitBoxFromPointer(il, managed);
                return;
            }

            // native struct passed by value: store into a mirror local -> box -> wrap in a proxy object
            var info = MirrorTypes.For(managed);
            var local = il.DeclareLocal(info.Mirror);
            il.Emit(OpCodes.Stloc, local);
            EmitBoxFromLocal(il, managed, local);
            return;
        }

        if (managed == typeof(bool))
        {
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Cgt_Un);
            return;
        }

        if (managed == typeof(char))
        {
            il.Emit(OpCodes.Conv_U2);
            return;
        }

        if (managed.IsEnum) return; // already on the stack as the underlying integer

        if (managed == typeof(string))
        {
            EmitCallStringConverter(il, typeof(IntPtr), Il2CppToManagedField, StringToManagedInvoke);
            return;
        }

        if (typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).IsAssignableFrom(managed))
        {
            EmitWrapReference(il, managed);
            return;
        }

        throw new NotSupportedException($"No native -> managed conversion: ${native?.Name} -> {managed?.Name}");
    }

    /// <summary>The value on top of the stack is a managed value; convert it to the native slot value.</summary>
    private static void EmitManagedToNative(ILGenerator il, Type managed, Type native)
    {
        if (managed == native) return;

        if (HookPlan.IsBoxedValueType(managed))
        {
            // 大结构（>16 字节）：ARM64 il2cpp 按值传参走隐藏指针（memory class），
            // 槽位就是"指向值副本的指针"。这里把代理对象 unbox 出数据指针原样交给 orig（零拷贝）。
            // 不生成任何分支：proxy -> ToPtr -> unbox 两步直达，避免合流点栈深不一致导致 InvalidProgramException。
            if (native == typeof(IntPtr))
            {
                EmitUnboxToPointer(il, managed);
                return;
            }

            // Proxy object -> unbox -> copy into a mirror local -> hand the struct by value.
            // A null proxy (e.g. a missing Nullable<Color> argument in GUIPanel.Draw):
            // ToPtr(null) = 0 -> unbox(0) would NRE on the null data pointer; the native side expects
            // a zeroed struct (hasValue = false).
            var info = MirrorTypes.For(managed);
            var mirror = il.DeclareLocal(info.Mirror);

            var end = il.DefineLabel();
            var nullLabel = il.DefineLabel();
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Beq, nullLabel);

            // non-null: unbox -> cpblk into the mirror
            var data = il.DeclareLocal(typeof(IntPtr));
            EmitUnboxToPointer(il, managed);
            il.Emit(OpCodes.Stloc, data);
            il.Emit(OpCodes.Ldloca, mirror);
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Ldloc, data);
            il.Emit(OpCodes.Ldc_I4, info.Size);
            il.Emit(OpCodes.Cpblk);
            il.Emit(OpCodes.Br, end);

            // null: zero the mirror (the default hasValue=false value is a run of zeros)
            il.MarkLabel(nullLabel);
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldloca, mirror);
            il.Emit(OpCodes.Initobj, info.Mirror);

            il.MarkLabel(end);
            il.Emit(OpCodes.Ldloc, mirror);
            return;
        }

        if (managed == typeof(bool))
        {
            il.Emit(OpCodes.Conv_U1);
            return;
        }

        if (managed == typeof(char))
        {
            il.Emit(OpCodes.Conv_U2);
            return;
        }

        if (managed.IsEnum)
        {
            EmitConvToUnderlying(il, managed);
            return;
        }

        if (managed == typeof(string))
        {
            EmitCallStringConverter(il, typeof(string), ManagedToIl2CppField, ManagedToStringInvoke);
            return;
        }

        if (typeof(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).IsAssignableFrom(managed))
        {
            il.Emit(OpCodes.Call, ToPtr);
            return;
        }

        throw new NotSupportedException($"No managed -> native conversion: ${managed?.Name} -> {native?.Name}");
    }

    /// <summary>
    ///     String conversion shape: the top of the stack is the string/IntPtr argument; store it to a
    ///     temp, then push the injected converter field + argument and callvirt Invoke.
    /// </summary>
    private static void EmitCallStringConverter(ILGenerator il, Type stackType, FieldInfo converterField, MethodInfo invoke)
    {
        var temp = il.DeclareLocal(stackType);
        il.Emit(OpCodes.Stloc, temp);
        il.Emit(OpCodes.Ldsfld, converterField);
        il.Emit(OpCodes.Ldloc, temp);
        il.Emit(OpCodes.Callvirt, invoke);
    }

    /// <summary>Top of the stack is an IntPtr; wrap it in the interop wrapper (zero -> null).</summary>
    private static void EmitWrapReference(ILGenerator il, Type managedType)
    {
        var nullLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Beq, nullLabel);

        il.Emit(OpCodes.Newobj, managedType.GetConstructor(new[] { typeof(IntPtr) })
                                 ?? throw new NotSupportedException($"{managedType.Name} has no ctor(IntPtr)"));
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldnull);
        il.MarkLabel(endLabel);
    }

    /// <summary>Boxes a mirror local into a proxy object (top of the stack).</summary>
    private static void EmitBoxFromLocal(ILGenerator il, Type boxed, LocalBuilder mirror)
    {
        il.Emit(OpCodes.Ldtoken, boxed);
        il.Emit(OpCodes.Call, GetTypeFromHandle);
        il.Emit(OpCodes.Call, GetClassPointer);
        il.Emit(OpCodes.Ldloca, mirror);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Call, ValueBox);
        EmitWrapReference(il, boxed);
    }

    /// <summary>
    ///     Top of the stack is a native data pointer (the "invisible reference" of a &gt;16 byte value
    ///     type); box it into a proxy object (top of the stack). A null pointer must never reach
    ///     il2cpp_value_box: that is memmove(dst, NULL, size). It becomes a zeroed value instead.
    /// </summary>
    private static void EmitBoxFromPointer(ILGenerator il, Type boxed)
    {
        var data = il.DeclareLocal(typeof(IntPtr));
        il.Emit(OpCodes.Stloc, data);

        var nullLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldloc, data);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Beq, nullLabel);

        // non-null: box straight from the native data pointer
        il.Emit(OpCodes.Ldtoken, boxed);
        il.Emit(OpCodes.Call, GetTypeFromHandle);
        il.Emit(OpCodes.Call, GetClassPointer);
        il.Emit(OpCodes.Ldloc, data);
        il.Emit(OpCodes.Call, ValueBox);
        EmitWrapReference(il, boxed);
        il.Emit(OpCodes.Br, endLabel);

        // null: produce a default (all-zero) value of the same type
        il.MarkLabel(nullLabel);
        var info = MirrorTypes.For(boxed);
        var mirror = il.DeclareLocal(info.Mirror);
        il.Emit(OpCodes.Ldloca, mirror);
        il.Emit(OpCodes.Initobj, info.Mirror);
        EmitBoxFromLocal(il, boxed, mirror);
        il.MarkLabel(endLabel);
    }

    /// <summary>Top of the stack is a proxy object; unbox it to a data pointer (top of the stack).</summary>
    private static void EmitUnboxToPointer(ILGenerator il, Type boxed)
    {
        il.Emit(OpCodes.Call, ToPtr);
        il.Emit(OpCodes.Call, ObjectUnbox);
    }

    // ---------------- byref-return helpers ----------------

    /// <summary>
    ///     Top of the stack is an element data pointer; load the element through it as the slot value
    ///     (or wrapped managed value) on top of the stack. Used by the orig stub for ref returns.
    /// </summary>
    private static void EmitLoadFromPointer(ILGenerator il, Type managed, Type slot)
    {
        var pointer = il.DeclareLocal(typeof(IntPtr));
        il.Emit(OpCodes.Stloc, pointer);
        il.Emit(OpCodes.Ldloc, pointer);
        il.Emit(OpCodes.Ldobj, slot);
        EmitNativeToManaged(il, managed, slot);
    }

    /// <summary>
    ///     Top of the stack is (pointer, element value already in slot form); store the value
    ///     through the pointer. Used by the entry stub to write a ref-return handler's value
    ///     back into the il2cpp storage the original ref pointed at.
    /// </summary>
    private static void EmitStoreThroughPointer(ILGenerator il, Type managed, Type slot)
    {
        if (slot.IsValueType)
        {
            il.Emit(OpCodes.Stobj, slot);        // pointer + value-type slot value
        }
        else if (slot == typeof(IntPtr))
        {
            il.Emit(OpCodes.Stind_I);           // pointer + raw pointer value
        }
        else
        {
            il.Emit(OpCodes.Stind_Ref);         // pointer + reference value
        }
    }

    private static void EmitConvToUnderlying(ILGenerator il, Type enumType)
    {
        var target = Enum.GetUnderlyingType(enumType);
        if (target == typeof(byte) || target == typeof(sbyte)) il.Emit(OpCodes.Conv_U1);
        else if (target == typeof(short) || target == typeof(ushort)) il.Emit(OpCodes.Conv_I2);
        else if (target == typeof(int) || target == typeof(uint)) il.Emit(OpCodes.Conv_I4);
        else if (target == typeof(long) || target == typeof(ulong)) il.Emit(OpCodes.Conv_I8);
    }
}