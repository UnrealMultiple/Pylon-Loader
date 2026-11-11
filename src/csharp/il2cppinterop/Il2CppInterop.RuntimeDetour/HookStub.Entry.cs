using System;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Il2CppInterop.RuntimeDetour;

internal static partial class HookStub
{
    internal static (Delegate stub, IntPtr entry) BuildEntry(HookPlan plan, int slot)
    {
        var nativeParams = plan.NativeParams.Concat(new[] { typeof(IntPtr) }).ToArray();
        var method = new DynamicMethod(EntryPrefix + plan.Label, plan.NativeReturn, nativeParams, typeof(HookStub).Module, true);
        var il = method.GetILGenerator();
        var byrefs = new LocalBuilder[plan.ParamTypes.Length];
        var boxedRefs = new LocalBuilder[plan.ParamTypes.Length];
        var result = plan.NativeReturn == typeof(void) ? null : il.DeclareLocal(plan.NativeReturn);
        var needsFrame = plan.ParamTypes.Where((t, i) => !t.IsByRef && HookPlan.IsBoxedValueType(t) && plan.NativeParams[plan.IsStatic ? i : i + 1] == typeof(IntPtr)).Any();

        // Outer catch contains conversion, handler and write-back exceptions. Inner finally always
        // restores the invocation frame, including recursive and concurrent native callbacks.
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldc_I4, slot);
        il.Emit(OpCodes.Call, EnterNativeMethod);
        il.BeginExceptionBlock();
        var managedComplete = il.DefineLabel();
        if (plan.Target.IsGenericMethod || plan.DeclaringType.IsGenericType)
        {
            // IL2CPP may share one native code address between generic instantiations. Only
            // the subscribed MethodInfo context may enter the typed handler; all other callers
            // bypass it, preserving their exact native arguments and hidden MethodInfo pointer.
            var matchingContext = il.DefineLabel();
            il.Emit(OpCodes.Ldarg, nativeParams.Length - 1);
            il.Emit(OpCodes.Ldc_I8, plan.MethodInfoPtr.ToInt64());
            il.Emit(OpCodes.Conv_I);
            il.Emit(OpCodes.Beq, matchingContext);
            for (var i = 0; i < nativeParams.Length; i++) il.Emit(OpCodes.Ldarg, i);
            il.Emit(OpCodes.Ldc_I4, slot);
            il.Emit(OpCodes.Call, OrigPointerGetter);
            il.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, plan.NativeReturn, nativeParams);
            if (result is not null) il.Emit(OpCodes.Stloc, result);
            il.Emit(OpCodes.Br, managedComplete);
            il.MarkLabel(matchingContext);
        }
        if (needsFrame)
        {
            il.Emit(OpCodes.Ldc_I4, slot);
            il.Emit(OpCodes.Ldc_I4, plan.ParamTypes.Length);
            il.Emit(OpCodes.Call, EnterMethod);
            il.BeginExceptionBlock();
            for (var i = 0; i < plan.ParamTypes.Length; i++)
            {
                var managed = plan.ParamTypes[i];
                if (managed.IsByRef || !HookPlan.IsBoxedValueType(managed) || plan.NativeParams[plan.IsStatic ? i : i + 1] != typeof(IntPtr)) continue;
                il.Emit(OpCodes.Ldarg, plan.IsStatic ? i : i + 1);
                il.Emit(OpCodes.Ldc_I4, slot);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Call, RecordIndirectMethod);
            }
        }

        il.Emit(OpCodes.Ldc_I4, slot);
        il.Emit(OpCodes.Call, HandlerGetter);
        il.Emit(OpCodes.Castclass, plan.HandlerType);
        il.Emit(OpCodes.Ldc_I4, slot);
        il.Emit(OpCodes.Call, OrigDelegateGetter);
        il.Emit(OpCodes.Castclass, plan.OrigType);
        var arg = 0;
        if (!plan.IsStatic)
        {
            il.Emit(OpCodes.Ldarg, arg++);
            EmitNativeToManaged(il, plan.DeclaringType, typeof(IntPtr));
        }
        for (var i = 0; i < plan.ParamTypes.Length; i++, arg++)
        {
            var managed = plan.ParamTypes[i];
            var native = plan.NativeParams[arg];
            if (!managed.IsByRef)
            {
                il.Emit(OpCodes.Ldarg, arg);
                EmitNativeToManaged(il, managed, native);
                continue;
            }
            var element = managed.GetElementType()!;
            if (HookPlan.IsBoxedValueType(element))
            {
                var local = il.DeclareLocal(element);
                boxedRefs[i] = local;
                if (!plan.ParamIsOut[i])
                {
                    il.Emit(OpCodes.Ldarg, arg);
                    EmitBoxFromPointer(il, element);
                    il.Emit(OpCodes.Stloc, local);
                }
                EmitAliasedLocalAddress(il, plan, i, boxedRefs);
            }
            else if (managed == native) il.Emit(OpCodes.Ldarg, arg);
            else
            {
                var local = il.DeclareLocal(element);
                byrefs[i] = local;
                if (!plan.ParamIsOut[i])
                {
                    il.Emit(OpCodes.Ldarg, arg);
                    il.Emit(OpCodes.Ldobj, native.GetElementType()!);
                    EmitNativeToManaged(il, element, native.GetElementType()!);
                    il.Emit(OpCodes.Stloc, local);
                }
                EmitAliasedLocalAddress(il, plan, i, byrefs);
            }
        }
        il.Emit(OpCodes.Callvirt, plan.HandlerType.GetMethod("Invoke")!);
        if (result is not null)
        {
            EmitManagedToNative(il, plan.ReturnType, plan.NativeReturn);
            il.Emit(OpCodes.Stloc, result);
        }
        arg = plan.IsStatic ? 0 : 1;
        for (var i = 0; i < plan.ParamTypes.Length; i++, arg++)
        {
            if (boxedRefs[i] is not null)
            {
                var element = plan.ParamTypes[i].GetElementType()!;
                var mirror = MirrorTypes.For(element).Mirror;
                il.Emit(OpCodes.Ldarg, arg);
                EmitAliasedLocalAddress(il, plan, i, boxedRefs);
                il.Emit(OpCodes.Ldobj, boxedRefs[i].LocalType);
                EmitManagedToNative(il, element, mirror);
                il.Emit(OpCodes.Stobj, mirror);
            }
            else if (byrefs[i] is not null)
            {
                var element = plan.ParamTypes[i].GetElementType()!;
                var nativeElement = plan.NativeParams[arg].GetElementType()!;
                il.Emit(OpCodes.Ldarg, arg);
                EmitAliasedLocalAddress(il, plan, i, byrefs);
                il.Emit(OpCodes.Ldobj, byrefs[i].LocalType);
                EmitManagedToNative(il, element, nativeElement);
                il.Emit(OpCodes.Stobj, nativeElement);
            }
        }
        if (needsFrame)
        {
            il.BeginFinallyBlock();
            il.Emit(OpCodes.Call, ExitMethod);
            il.EndExceptionBlock();
        }
        il.MarkLabel(managedComplete);
        il.BeginFinallyBlock();
        il.Emit(OpCodes.Ldc_I4, slot);
        il.Emit(OpCodes.Call, ExitNativeMethod);
        il.EndExceptionBlock();
        il.BeginCatchBlock(typeof(Exception));
        var error = il.DeclareLocal(typeof(object));
        il.Emit(OpCodes.Stloc, error);
        il.Emit(OpCodes.Ldstr, "entry:" + plan.Label);
        il.Emit(OpCodes.Ldloc, error);
        il.Emit(OpCodes.Call, ReportThrowableMethod);
        if (result is not null)
        {
            il.Emit(OpCodes.Ldloca, result);
            il.Emit(OpCodes.Initobj, plan.NativeReturn);
        }
        il.EndExceptionBlock();
        if (result is not null) il.Emit(OpCodes.Ldloc, result);
        il.Emit(OpCodes.Ret);
        var stub = method.CreateDelegate(NativeDelegateFactory.Create(plan.NativeReturn, nativeParams, CallingConvention.Cdecl));
        var entry = Marshal.GetFunctionPointerForDelegate(stub);
        if (entry == IntPtr.Zero) throw new InvalidOperationException($"{plan.Label}: entry stub has no function pointer");
        return (stub, entry);
    }
}
