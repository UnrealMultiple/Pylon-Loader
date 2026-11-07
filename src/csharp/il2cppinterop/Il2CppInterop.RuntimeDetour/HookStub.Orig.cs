using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Reflection.Emit;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>Orig stub: managed signature -> native ABI (the orig delegate; calls the next hop through calli).</summary>
internal static partial class HookStub
{
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
            il.Emit(OpCodes.Call, ToPtr);
        }

        for (var i = 0; i < plan.ParamTypes.Length; i++)
        {
            var managed = plan.ParamTypes[i];
            var native = plan.NativeParams[argIndex];

            if (managed.IsByRef)
            {
                EmitOrigByRefArgument(il, plan, i, argIndex, native, byrefLocals);
            }
            else if (HookPlan.IsBoxedValueType(managed) && native == typeof(IntPtr))
            {
                // D-014: 转发 entry 桩记录的原始数据指针（指向调用者的栈副本）。
                // 这样 orig 内部对结构体的写回会落回调用者副本；托管 box 只是 handler 侧的快照。
                EmitForwardIndirectArg(il, plan, slot, i, argIndex);
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
        il.Emit(OpCodes.Ldsfld, OrigPointersField);
        il.Emit(OpCodes.Ldc_I4, slot);
        il.Emit(OpCodes.Callvirt, PointersGetter);

        var nativeParams = plan.NativeParams.Concat(new[] { typeof(IntPtr) }).ToArray();
        il.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, plan.NativeReturn, nativeParams);

        if (nativeResultLocal is not null) il.Emit(OpCodes.Stloc, nativeResultLocal);

        // ref/out write-back：void 方法也要执行，否则原版写出来的值回不到托管侧（D-007）。
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

        if (plan.IsByRefReturn)
        {
            // nativeResultLocal holds the element data pointer; load the element value through it.
            EmitLoadFromPointer(il, plan.ReturnType, plan.ReturnElementNative);
        }
        else if (nativeResultLocal is not null)
        {
            il.Emit(OpCodes.Ldloc, nativeResultLocal);
            EmitNativeToManaged(il, plan.ReturnType, plan.NativeReturn);   // IntPtr -> string etc.
        }

        il.Emit(OpCodes.Ret);

        var orig = method.CreateDelegate(plan.OrigType);
        HookState.s_keepAlive.Add(orig);
        return orig;
    }

    /// <summary>
    ///     One ref/out parameter of the orig stub: the managed byref is converted to the native
    ///     pointer form; boxed value types are unboxed straight to a data pointer.
    /// </summary>
    private static void EmitOrigByRefArgument(ILGenerator il, HookPlan plan, int paramIndex, int argIndex,
                                              Type native, LocalBuilder[] byrefLocals)
    {
        var managed = plan.ParamTypes[paramIndex];
        var element = managed.GetElementType()!;

        if (HookPlan.IsBoxedValueType(element) && native == typeof(IntPtr))
        {
            // ref/out boxed value type: take the proxy object, unbox to a data pointer
            il.Emit(OpCodes.Ldarg, argIndex);
            il.Emit(OpCodes.Ldobj, element);
            EmitUnboxToPointer(il, element);
            return;
        }

        if (native == managed)
        {
            il.Emit(OpCodes.Ldarg, argIndex);
            return;
        }

        var nativeElement = native.GetElementType()!;
        var local = il.DeclareLocal(nativeElement);
        byrefLocals[paramIndex] = local;

        il.Emit(OpCodes.Ldarg, argIndex);   // managed ref
        il.Emit(OpCodes.Ldobj, element);    // read the current value
        EmitManagedToNative(il, element, nativeElement);
        il.Emit(OpCodes.Stloc, local);
        il.Emit(OpCodes.Ldloca, local);     // native pointer
    }

    /// <summary>
    ///     D-014: forward the raw data pointer recorded by the entry stub (points at the caller's
    ///     stack copy) so writes made by the original function land on the caller's copy; the
    ///     managed box is only the handler-side snapshot. Falls back to unboxing the managed box
    ///     when nothing was recorded (theoretically unreachable).
    /// </summary>
    private static void EmitForwardIndirectArg(ILGenerator il, HookPlan plan, int slot, int paramIndex, int argIndex)
    {
        var managed = plan.ParamTypes[paramIndex];
        var raw = il.DeclareLocal(typeof(IntPtr));
        var haveLabel = il.DefineLabel();
        var endLabel = il.DefineLabel();

        il.Emit(OpCodes.Ldc_I4, slot);
        il.Emit(OpCodes.Ldc_I4, paramIndex);
        il.Emit(OpCodes.Call, GetIndirectMethod);
        il.Emit(OpCodes.Stloc, raw);
        il.Emit(OpCodes.Ldloc, raw);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Bne_Un, haveLabel);

        // 未记录（理论上不可达）：退回 unbox 托管 box
        il.Emit(OpCodes.Ldarg, argIndex);
        EmitUnboxToPointer(il, managed);
        il.Emit(OpCodes.Br, endLabel);

        il.MarkLabel(haveLabel);
        il.Emit(OpCodes.Ldloc, raw);
        il.MarkLabel(endLabel);
    }
}
