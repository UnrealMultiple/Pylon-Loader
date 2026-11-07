using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>Entry stub: native ABI -> managed (the game's native entry point jumps here via Dobby).</summary>
internal static partial class HookStub
{
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
        // For ref returns the native result is the element data pointer; the managed handler
        // returns the element value, so we keep two locals: pointer + element value.
        LocalBuilder? resultLocal = null;
        LocalBuilder? refPointerLocal = null;
        if (plan.IsByRefReturn)
        {
            refPointerLocal = il.DeclareLocal(typeof(IntPtr));
            resultLocal = il.DeclareLocal(plan.ReturnElementNative);
        }
        else if (plan.NativeReturn != typeof(void))
        {
            resultLocal = il.DeclareLocal(plan.NativeReturn);
        }

        il.BeginExceptionBlock();

        // handler = (HandlerType)HookState.s_handlers[slot];
        il.Emit(OpCodes.Ldsfld, HandlersField);
        il.Emit(OpCodes.Ldc_I4, slot);
        il.Emit(OpCodes.Callvirt, HandlersGetter);
        il.Emit(OpCodes.Castclass, plan.HandlerType);
        il.Emit(OpCodes.Stloc, handlerLocal);

        // orig = (OrigType)HookState.s_origDelegates[slot];
        il.Emit(OpCodes.Ldsfld, OrigDelegatesField);
        il.Emit(OpCodes.Ldc_I4, slot);
        il.Emit(OpCodes.Callvirt, HandlersGetter);
        il.Emit(OpCodes.Castclass, plan.OrigType);
        il.Emit(OpCodes.Stloc, origLocal);

        // D-014: 先记录所有">16 字节结构体"实参的原始数据指针（指向调用者栈副本）。
        // orig 桩调用原函数时直接转发该指针，游戏在函数内部对结构体的写回才能落回调用者副本；
        // 若转发托管 box 的数据指针，写回会丢（tooltip 整体不显示的根因）。
        for (var i = 0; i < plan.ParamTypes.Length; i++)
        {
            var managed = plan.ParamTypes[i];
            if (managed.IsByRef || !HookPlan.IsBoxedValueType(managed)) continue;
            if (plan.NativeParams[plan.IsStatic ? i : i + 1] != typeof(IntPtr)) continue;

            il.Emit(OpCodes.Ldarg, plan.IsStatic ? i : i + 1);
            il.Emit(OpCodes.Ldc_I4, slot);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Call, RecordIndirectMethod);
        }

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
                EmitEntryByRefArgument(il, plan, i, argIndex, native, byrefLocals, boxedByrefLocals);
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
        if (plan.IsByRefReturn)
        {
            // handler returned the element value; convert to the element slot form.
            EmitManagedToNative(il, plan.ReturnType, plan.ReturnElementNative);
            il.Emit(OpCodes.Stloc, resultLocal!);
        }
        else if (resultLocal is not null)
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
                il.Emit(OpCodes.Call, ToPtr);
                il.Emit(OpCodes.Call, ObjectUnbox);          // source data pointer
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
        il.Emit(OpCodes.Call, ReportExceptionMethod);
        if (resultLocal is not null)
        {
            il.Emit(OpCodes.Ldloca, resultLocal);
            il.Emit(OpCodes.Initobj, plan.IsByRefReturn ? plan.ReturnElementNative : plan.NativeReturn);
        }
        il.EndExceptionBlock();

        if (plan.IsByRefReturn)
        {
            // Write the handler's element value through the original data pointer, then hand the
            // pointer back to the game unchanged: the ref-return contract is preserved.
            il.Emit(OpCodes.Ldloc, refPointerLocal!);
            il.Emit(OpCodes.Ldloc, resultLocal!);
            EmitStoreThroughPointer(il, plan.ReturnType, plan.ReturnElementNative);
            il.Emit(OpCodes.Ldloc, refPointerLocal!);
            il.Emit(OpCodes.Ret);
        }
        else if (resultLocal is not null)
        {
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Ret);
        }
        else
        {
            il.Emit(OpCodes.Ret);
        }

        var delegateType = NativeDelegateFactory.Create(plan.NativeReturn, nativeParams, CallingConvention.Cdecl);
        var stub = method.CreateDelegate(delegateType);
        HookState.s_keepAlive.Add(stub);
        var entry = Marshal.GetFunctionPointerForDelegate(stub);
        if (entry == IntPtr.Zero)
            throw new InvalidOperationException($"{plan.Label}: the entry stub has no function pointer");
        return (stub, entry);
    }

    /// <summary>
    ///     One ref/out parameter of the entry stub: native hands a raw pointer, the managed handler
    ///     wants a byref; boxed value types go through a proxy object (boxedByrefLocals).
    /// </summary>
    private static void EmitEntryByRefArgument(ILGenerator il, HookPlan plan, int paramIndex, int argIndex,
                                               Type native, LocalBuilder[] byrefLocals, LocalBuilder[] boxedByrefLocals)
    {
        var managed = plan.ParamTypes[paramIndex];
        var element = managed.GetElementType()!;

        if (HookPlan.IsBoxedValueType(element) && native == typeof(IntPtr))
        {
            // ref/out boxed value type: native hands a data pointer, managed side wants ref proxy object
            var local = il.DeclareLocal(element);
            boxedByrefLocals[paramIndex] = local;
            if (plan.ParamIsOut[paramIndex])
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
            return;
        }

        if (native == managed)
        {
            // value type ref: the native pointer is the variable itself, pass it through
            // (handler changes are immediately visible)
            il.Emit(OpCodes.Ldarg, argIndex);
            return;
        }

        var nativeElement = native.GetElementType()!;
        var copy = il.DeclareLocal(element);
        byrefLocals[paramIndex] = copy;

        if (plan.ParamIsOut[paramIndex])
        {
            il.Emit(OpCodes.Ldloca, copy);
            il.Emit(OpCodes.Initobj, element);
        }
        else
        {
            il.Emit(OpCodes.Ldarg, argIndex);
            il.Emit(OpCodes.Ldobj, nativeElement);
            EmitNativeToManaged(il, element, nativeElement);
            il.Emit(OpCodes.Stloc, copy);
        }

        il.Emit(OpCodes.Ldloca, copy);
    }
}
