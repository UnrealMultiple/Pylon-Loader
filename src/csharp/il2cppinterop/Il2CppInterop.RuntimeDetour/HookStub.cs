using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>
///     Constructs the two stubs of a hook:
///     1. entry stub (native ABI -&gt; managed): the game's native entry point jumps here (via Dobby);
///     2. orig stub (managed -&gt; native ABI): the orig delegate; calls the next hop through calli.
///
///     Signature mapping (mirrors <see cref="HookPlan.NativeSlot"/>):
///       reference/string -&gt; IntPtr; bool -&gt; byte; char -&gt; ushort; enum -&gt; underlying integer;
///       real value types (Rectangle/Vector2...) by value; ref/out -&gt; byref or IntPtr;
///       boxed value types (Il2CppSystem.Nullable&lt;T&gt; etc., class proxies passed by value)
///       -&gt; mirror struct + il2cpp_value_box/unbox.
///
///     Both sides carry the hidden trailing MethodInfo*; per ECMA-335, calli pushes arguments
///     first and the function pointer last.
/// </summary>
internal static class HookStub
{
    private const string EntryPrefix = "(pylon -> managed) ";
    private const string OrigPrefix = "(managed -> pylon) ";

    private static readonly MethodInfo s_handlersGetter = typeof(List<Delegate>).GetProperty("Item")!.GetGetMethod()!;
    private static readonly MethodInfo s_pointersGetter = typeof(List<IntPtr>).GetProperty("Item")!.GetGetMethod()!;
    private static readonly FieldInfo s_handlersField =
        typeof(HookState).GetField(nameof(HookState.s_handlers), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo s_origDelegatesField =
        typeof(HookState).GetField(nameof(HookState.s_origDelegates), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo s_origPointersField =
        typeof(HookState).GetField(nameof(HookState.s_origPointers), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo s_managedToIl2CppField =
        typeof(HookStub).GetField(nameof(ManagedToIl2Cpp), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo s_il2CppToManagedField =
        typeof(HookStub).GetField(nameof(Il2CppToManaged), BindingFlags.Static | BindingFlags.NonPublic)!;

    private static readonly MethodInfo s_reportExceptionMethod =
        typeof(HookState).GetMethod(nameof(HookState.ReportException), BindingFlags.Static | BindingFlags.NonPublic)!;

    /// <summary>Injectable string conversions (real IL2CPP on device; the desktop probe substitutes fakes).</summary>
    internal static Func<string, IntPtr> ManagedToIl2Cpp = IL2CPP.ManagedStringToIl2Cpp;
    internal static Func<IntPtr, string?> Il2CppToManaged = IL2CPP.Il2CppStringToManaged;

    private static readonly MethodInfo s_stringToManagedInvoke =
        typeof(Func<IntPtr, string?>).GetMethod("Invoke")!;
    private static readonly MethodInfo s_managedToStringInvoke =
        typeof(Func<string, IntPtr>).GetMethod("Invoke")!;
    private static readonly MethodInfo s_toPtr = typeof(IL2CPP).GetMethod(nameof(IL2CPP.Il2CppObjectBaseToPtr))!;
    private static readonly MethodInfo s_getTypeFromHandle = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!;
    private static readonly MethodInfo s_getClassPointer = typeof(Il2CppClassPointerStore)
        .GetMethod(nameof(Il2CppClassPointerStore.GetNativeClassPointer), new[] { typeof(Type) })!;
    private static readonly MethodInfo s_valueBox = typeof(IL2CPP).GetMethod(nameof(IL2CPP.il2cpp_value_box))!;
    private static readonly MethodInfo s_objectUnbox = typeof(IL2CPP).GetMethod(nameof(IL2CPP.il2cpp_object_unbox))!;

    /// <summary>Builds the entry stub, returning (delegate, native entry pointer).</summary>
    internal static (Delegate stub, IntPtr entry) BuildEntry(HookPlan plan, int slot)
    {
        var nativeParams = plan.NativeParams.Concat(new[] { typeof(IntPtr) }).ToArray();
        var method = new DynamicMethod(EntryPrefix + plan.Label, plan.NativeReturn, nativeParams,
            typeof(HookStub).Module, skipVisibility: true);

        var il = method.GetILGenerator();
        var handlerLocal = il.DeclareLocal(plan.HandlerType);
        var origLocal = il.DeclareLocal(plan.OrigType);
        var byrefLocals = new LocalBuilder[plan.ParamTypes.Length];
        var boxedByrefLocals = new LocalBuilder[plan.ParamTypes.Length];

        // The return value must land in a local first: leave clears the stack, and leaving the try
        // block with a value on the stack is illegal.
        LocalBuilder? resultLocal = null;
        if (plan.NativeReturn != typeof(void)) resultLocal = il.DeclareLocal(plan.NativeReturn);

        il.BeginExceptionBlock();

        // handler = (HandlerType)HookState.s_handlers[slot];
        il.Emit(OpCodes.Ldsfld, s_handlersField);
        il.Emit(OpCodes.Ldc_I4, slot);
        il.Emit(OpCodes.Callvirt, s_handlersGetter);
        il.Emit(OpCodes.Castclass, plan.HandlerType);
        il.Emit(OpCodes.Stloc, handlerLocal);

        // orig = (OrigType)HookState.s_origDelegates[slot];
        il.Emit(OpCodes.Ldsfld, s_origDelegatesField);
        il.Emit(OpCodes.Ldc_I4, slot);
        il.Emit(OpCodes.Callvirt, s_handlersGetter);
        il.Emit(OpCodes.Castclass, plan.OrigType);
        il.Emit(OpCodes.Stloc, origLocal);

        // handler(orig, self?, parameters...)
        il.Emit(OpCodes.Ldloc, handlerLocal);
        il.Emit(OpCodes.Ldloc, origLocal);

        var argIndex = 0;
        if (!plan.IsStatic)
        {
            il.Emit(OpCodes.Ldarg, argIndex);
            EmitNativeToManaged(il, plan.DeclaringType, typeof(IntPtr));
            argIndex++;
        }

        for (var i = 0; i < plan.ParamTypes.Length; i++)
        {
            var managed = plan.ParamTypes[i];
            var native = plan.NativeParams[argIndex];

            if (managed.IsByRef)
            {
                var element = managed.GetElementType()!;
                if (HookPlan.IsBoxedValueType(element) && native == typeof(IntPtr))
                {
                    // ref/out boxed value type: native hands a data pointer, managed side wants ref proxy object
                    var local = il.DeclareLocal(element);
                    boxedByrefLocals[i] = local;
                    if (plan.ParamIsOut[i])
                    {
                        il.Emit(OpCodes.Ldloca, local);
                        il.Emit(OpCodes.Initobj, element);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldarg, argIndex);
                        EmitBoxFromPointer(il, element);
                        il.Emit(OpCodes.Stloc, local);
                    }
                    il.Emit(OpCodes.Ldloca, local);
                }
                else if (native == managed)
                {
                    // value type ref: the native pointer is the variable itself, pass it through
                    // (handler changes are immediately visible)
                    il.Emit(OpCodes.Ldarg, argIndex);
                }
                else
                {
                    var nativeElement = native.GetElementType()!;
                    var local = il.DeclareLocal(element);
                    byrefLocals[i] = local;

                    if (plan.ParamIsOut[i])
                    {
                        il.Emit(OpCodes.Ldloca, local);
                        il.Emit(OpCodes.Initobj, element);
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldarg, argIndex);
                        il.Emit(OpCodes.Ldobj, nativeElement);
                        EmitNativeToManaged(il, element, nativeElement);
                        il.Emit(OpCodes.Stloc, local);
                    }

                    il.Emit(OpCodes.Ldloca, local);
                }
            }
            else
            {
                il.Emit(OpCodes.Ldarg, argIndex);
                EmitNativeToManaged(il, managed, native);
            }

            argIndex++;
        }

        il.Emit(OpCodes.Callvirt, plan.HandlerType.GetMethod("Invoke")!);

        // managed return value -> native form, then store into the local (cannot leave with a value)
        if (resultLocal is not null)
        {
            EmitManagedToNative(il, plan.ReturnType, plan.NativeReturn);
            il.Emit(OpCodes.Stloc, resultLocal);
        }

        // ref/out write-back (value type refs are already passed through; not handled here)
        argIndex = plan.IsStatic ? 0 : 1;
        for (var i = 0; i < plan.ParamTypes.Length; i++)
        {
            if (boxedByrefLocals[i] is not null)
            {
                var element = plan.ParamTypes[i].GetElementType()!;
                var info = MirrorTypes.For(element);
                il.Emit(OpCodes.Ldarg, argIndex);            // destination data pointer
                il.Emit(OpCodes.Ldloc, boxedByrefLocals[i]);
                il.Emit(OpCodes.Call, s_toPtr);
                il.Emit(OpCodes.Call, s_objectUnbox);          // source data pointer
                il.Emit(OpCodes.Ldc_I4, info.Size);
                il.Emit(OpCodes.Cpblk);
            }
            else if (byrefLocals[i] is not null)
            {
                var element = plan.ParamTypes[i].GetElementType()!;
                var nativeElement = plan.NativeParams[argIndex].GetElementType()!;
                il.Emit(OpCodes.Ldarg, argIndex);
                il.Emit(OpCodes.Ldloc, byrefLocals[i]);
                EmitManagedToNative(il, element, nativeElement);
                il.Emit(OpCodes.Stobj, nativeElement);
            }
            argIndex++;
        }

        il.BeginCatchBlock(typeof(Exception));
        var exceptionLocal = il.DeclareLocal(typeof(Exception));
        il.Emit(OpCodes.Stloc, exceptionLocal);
        il.Emit(OpCodes.Ldstr, "entry:" + plan.Label);
        il.Emit(OpCodes.Ldloc, exceptionLocal);
        il.Emit(OpCodes.Call, s_reportExceptionMethod);
        if (resultLocal is not null)
        {
            il.Emit(OpCodes.Ldloca, resultLocal);
            il.Emit(OpCodes.Initobj, plan.NativeReturn);
        }
        il.EndExceptionBlock();

        if (resultLocal is not null) il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        var delegateType = NativeDelegateFactory.Create(plan.NativeReturn, nativeParams, CallingConvention.Cdecl);
        var stub = method.CreateDelegate(delegateType);
        HookState.s_keepAlive.Add(stub);
        var entry = Marshal.GetFunctionPointerForDelegate(stub);
        if (entry == IntPtr.Zero)
            throw new InvalidOperationException($"{plan.Label}: the entry stub has no function pointer");
        return (stub, entry);
    }

    /// <summary>Builds the orig stub (managed signature) and returns a strongly typed delegate.</summary>
    internal static Delegate BuildOrig(HookPlan plan, int slot)
    {
        var managedParams = new List<Type>();
        if (!plan.IsStatic) managedParams.Add(plan.DeclaringType);
        managedParams.AddRange(plan.ParamTypes);

        var method = new DynamicMethod(OrigPrefix + plan.Label, plan.ReturnType, managedParams.ToArray(),
            typeof(HookStub).Module, skipVisibility: true);

        var il = method.GetILGenerator();
        var byrefLocals = new LocalBuilder[plan.ParamTypes.Length];

        // The native return value must first land in a local of the native type: calli returns
        // NativeReturn (string -> IntPtr), and Stloc into a managed-typed local would produce an
        // InvalidProgramException ("CLR detected invalid program").
        var nativeResultLocal = plan.NativeReturn != typeof(void)
            ? il.DeclareLocal(plan.NativeReturn)
            : null;

        var argIndex = 0;
        if (!plan.IsStatic)
        {
            il.Emit(OpCodes.Ldarg, argIndex++);
            il.Emit(OpCodes.Call, s_toPtr);
        }

        for (var i = 0; i < plan.ParamTypes.Length; i++)
        {
            var managed = plan.ParamTypes[i];
            var native = plan.NativeParams[argIndex];

            if (managed.IsByRef)
            {
                var element = managed.GetElementType()!;
                if (HookPlan.IsBoxedValueType(element) && native == typeof(IntPtr))
                {
                    // ref/out boxed value type: take the proxy object, unbox to a data pointer
                    il.Emit(OpCodes.Ldarg, argIndex);
                    il.Emit(OpCodes.Ldobj, element);
                    EmitUnboxToPointer(il, element);
                }
                else if (native == managed)
                {
                    il.Emit(OpCodes.Ldarg, argIndex);
                }
                else
                {
                    var nativeElement = native.GetElementType()!;
                    var local = il.DeclareLocal(nativeElement);
                    byrefLocals[i] = local;

                    il.Emit(OpCodes.Ldarg, argIndex);   // managed ref
                    il.Emit(OpCodes.Ldobj, element);    // read the current value
                    EmitManagedToNative(il, element, nativeElement);
                    il.Emit(OpCodes.Stloc, local);
                    il.Emit(OpCodes.Ldloca, local);     // native pointer
                }
            }
            else
            {
                il.Emit(OpCodes.Ldarg, argIndex);
                EmitManagedToNative(il, managed, native);
            }

            argIndex++;
        }

        // hidden trailing MethodInfo*
        il.Emit(OpCodes.Ldc_I8, plan.MethodInfoPtr.ToInt64());
        il.Emit(OpCodes.Conv_I);

        // next hop address (pushed last)
        il.Emit(OpCodes.Ldsfld, s_origPointersField);
        il.Emit(OpCodes.Ldc_I4, slot);
        il.Emit(OpCodes.Callvirt, s_pointersGetter);

        var nativeParams = plan.NativeParams.Concat(new[] { typeof(IntPtr) }).ToArray();
        il.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, plan.NativeReturn, nativeParams);

        if (nativeResultLocal is not null)
        {
            il.Emit(OpCodes.Stloc, nativeResultLocal);

            // ref/out write-back (value type refs pass through directly; boxed value type changes are
            // already visible inside the box)
            argIndex = plan.IsStatic ? 0 : 1;
            for (var i = 0; i < plan.ParamTypes.Length; i++)
            {
                if (byrefLocals[i] is not null)
                {
                    var element = plan.ParamTypes[i].GetElementType()!;
                    var nativeElement = plan.NativeParams[argIndex].GetElementType()!;
                    il.Emit(OpCodes.Ldarg, argIndex);
                    il.Emit(OpCodes.Ldloc, byrefLocals[i]);
                    EmitNativeToManaged(il, element, nativeElement);
                    il.Emit(OpCodes.Stobj, element);
                }
                argIndex++;
            }

            il.Emit(OpCodes.Ldloc, nativeResultLocal);
            EmitNativeToManaged(il, plan.ReturnType, plan.NativeReturn);   // IntPtr -> string etc.
        }

        il.Emit(OpCodes.Ret);

        var orig = method.CreateDelegate(plan.OrigType);
        HookState.s_keepAlive.Add(orig);
        return orig;
    }

    /// <summary>
    ///     Builds an "as-is forwarding" handler, used when the head of a chain is undone: the Dobby
    ///     hook stays in place but the slot only forwards to the next hop.
    /// </summary>
    internal static Delegate BuildPassthrough(HookPlan plan)
    {
        var handlerInvoke = plan.HandlerType.GetMethod("Invoke")!;
        var parameters = handlerInvoke.GetParameters().Select(p => p.ParameterType).ToArray();
        var method = new DynamicMethod("(pylon passthrough) " + plan.Label, handlerInvoke.ReturnType, parameters,
            typeof(HookStub).Module, skipVisibility: true);

        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        for (var i = 1; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, i);
        il.Emit(OpCodes.Callvirt, plan.OrigType.GetMethod("Invoke")!);
        il.Emit(OpCodes.Ret);

        var passthrough = method.CreateDelegate(plan.HandlerType);
        HookState.s_keepAlive.Add(passthrough);
        return passthrough;
    }

    // ---------------- native -> managed ----------------

    /// <summary>The value on top of the stack is a native slot value; convert it to the managed value.</summary>
    private static void EmitNativeToManaged(ILGenerator il, Type managed, Type native)
    {
        if (managed == native) return;

        if (HookPlan.IsBoxedValueType(managed))
        {
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
            // Instance method call: this sits under the arguments on the stack.
            // The top of the stack is IntPtr (the argument), so store it to a temp first, then
            // push this + argument and callvirt Invoke.
            var temp = il.DeclareLocal(native ?? typeof(IntPtr));
            il.Emit(OpCodes.Stloc, temp);
            il.Emit(OpCodes.Ldsfld, s_il2CppToManagedField);
            il.Emit(OpCodes.Ldloc, temp);
            il.Emit(OpCodes.Callvirt, s_stringToManagedInvoke);
            return;
        }

        if (typeof(Il2CppObjectBase).IsAssignableFrom(managed))
        {
            EmitWrapReference(il, managed);
            return;
        }

        throw new NotSupportedException($"No native -> managed conversion: {native?.Name} -> {managed?.Name}");
    }

    /// <summary>The value on top of the stack is a managed value; convert it to the native slot value.</summary>
    private static void EmitManagedToNative(ILGenerator il, Type managed, Type native)
    {
        if (managed == native) return;

        if (HookPlan.IsBoxedValueType(managed))
        {
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
            // Same shape: the top of the stack is a string (argument); store it to a temp, then push
            // this + argument and callvirt Invoke.
            var temp = il.DeclareLocal(typeof(string));
            il.Emit(OpCodes.Stloc, temp);
            il.Emit(OpCodes.Ldsfld, s_managedToIl2CppField);
            il.Emit(OpCodes.Ldloc, temp);
            il.Emit(OpCodes.Callvirt, s_managedToStringInvoke);
            return;
        }

        if (typeof(Il2CppObjectBase).IsAssignableFrom(managed))
        {
            il.Emit(OpCodes.Call, s_toPtr);
            return;
        }

        throw new NotSupportedException($"No managed -> native conversion: {managed?.Name} -> {native?.Name}");
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
        il.Emit(OpCodes.Call, s_getTypeFromHandle);
        il.Emit(OpCodes.Call, s_getClassPointer);
        il.Emit(OpCodes.Ldloca, mirror);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Call, s_valueBox);
        EmitWrapReference(il, boxed);
    }

    /// <summary>Top of the stack is a native data pointer; box it into a proxy object (top of the stack).</summary>
    private static void EmitBoxFromPointer(ILGenerator il, Type boxed)
    {
        var data = il.DeclareLocal(typeof(IntPtr));
        il.Emit(OpCodes.Stloc, data);

        il.Emit(OpCodes.Ldtoken, boxed);
        il.Emit(OpCodes.Call, s_getTypeFromHandle);
        il.Emit(OpCodes.Call, s_getClassPointer);
        il.Emit(OpCodes.Ldloc, data);
        il.Emit(OpCodes.Call, s_valueBox);
        EmitWrapReference(il, boxed);
    }

    /// <summary>Top of the stack is a proxy object; unbox it to a data pointer (top of the stack).</summary>
    private static void EmitUnboxToPointer(ILGenerator il, Type boxed)
    {
        il.Emit(OpCodes.Call, s_toPtr);
        il.Emit(OpCodes.Call, s_objectUnbox);
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