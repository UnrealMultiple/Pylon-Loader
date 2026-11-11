using System;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Il2CppInterop.RuntimeDetour;

internal static partial class HookStub
{
    internal static Delegate BuildOrig(HookPlan plan, int slot)
    {
        var managedParams = (plan.IsStatic ? Type.EmptyTypes : new[] { plan.DeclaringType }).Concat(plan.ParamTypes).ToArray();
        var method = new DynamicMethod(OrigPrefix + plan.Label, plan.ReturnType, managedParams, typeof(HookStub).Module, true);
        var il = method.GetILGenerator();
        var refs = new LocalBuilder[plan.ParamTypes.Length];
        var result = plan.NativeReturn == typeof(void) ? null : il.DeclareLocal(plan.NativeReturn);
        var arg = 0;
        if (!plan.IsStatic)
        {
            il.Emit(OpCodes.Ldarg, arg++);
            il.Emit(OpCodes.Call, ToPtr);
        }
        for (var i = 0; i < plan.ParamTypes.Length; i++, arg++)
        {
            var managed = plan.ParamTypes[i];
            var native = plan.NativeParams[arg];
            if (managed.IsByRef)
            {
                var element = managed.GetElementType()!;
                if (managed == native) il.Emit(OpCodes.Ldarg, arg);
                else if (HookPlan.IsBoxedValueType(element))
                {
                    // A native-sized scratch value implements CLR ref/out semantics. Never unbox
                    // null, and never alias an unrelated ref local to the caller's original slot.
                    var mirror = MirrorTypes.For(element).Mirror;
                    var local = il.DeclareLocal(mirror);
                    refs[i] = local;
                    if (!plan.ParamIsOut[i])
                    {
                        il.Emit(OpCodes.Ldarg, arg);
                        il.Emit(OpCodes.Ldobj, element);
                        EmitManagedToNative(il, element, mirror);
                        il.Emit(OpCodes.Stloc, local);
                    }
                    EmitAliasedLocalAddress(il, plan, i, refs);
                    il.Emit(OpCodes.Conv_I);
                }
                else
                {
                    var nativeElement = native.GetElementType()!;
                    var local = il.DeclareLocal(nativeElement);
                    refs[i] = local;
                    if (!plan.ParamIsOut[i])
                    {
                        il.Emit(OpCodes.Ldarg, arg);
                        il.Emit(OpCodes.Ldobj, element);
                        EmitManagedToNative(il, element, nativeElement);
                        il.Emit(OpCodes.Stloc, local);
                    }
                    EmitAliasedLocalAddress(il, plan, i, refs);
                }
            }
            else if (HookPlan.IsBoxedValueType(managed) && native == typeof(IntPtr))
            {
                // Preserve the caller's invisible-reference storage where available, but copy the
                // actual orig(value) argument into it first. This also honors handler replacements.
                var mirror = MirrorTypes.For(managed).Mirror;
                var local = il.DeclareLocal(mirror);
                il.Emit(OpCodes.Ldarg, arg);
                EmitManagedToNative(il, managed, mirror);
                il.Emit(OpCodes.Stloc, local);
                var raw = il.DeclareLocal(typeof(IntPtr));
                il.Emit(OpCodes.Ldc_I4, slot);
                il.Emit(OpCodes.Ldc_I4, i);
                il.Emit(OpCodes.Call, GetIndirectMethod);
                il.Emit(OpCodes.Stloc, raw);
                var haveRaw = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, raw);
                il.Emit(OpCodes.Brtrue, haveRaw);
                il.Emit(OpCodes.Ldloca, local);
                il.Emit(OpCodes.Conv_I);
                il.Emit(OpCodes.Stloc, raw);
                il.MarkLabel(haveRaw);
                il.Emit(OpCodes.Ldloc, raw);
                il.Emit(OpCodes.Ldloc, local);
                il.Emit(OpCodes.Stobj, mirror);
                il.Emit(OpCodes.Ldloc, raw);
            }
            else
            {
                il.Emit(OpCodes.Ldarg, arg);
                EmitManagedToNative(il, managed, native);
            }
        }
        il.Emit(OpCodes.Ldc_I8, plan.MethodInfoPtr.ToInt64());
        il.Emit(OpCodes.Conv_I);
        il.Emit(OpCodes.Ldc_I4, slot);
        il.Emit(OpCodes.Call, OrigPointerGetter);
        il.EmitCalli(OpCodes.Calli, CallingConvention.Cdecl, plan.NativeReturn, plan.NativeParams.Concat(new[] { typeof(IntPtr) }).ToArray());
        if (result is not null) il.Emit(OpCodes.Stloc, result);
        arg = plan.IsStatic ? 0 : 1;
        for (var i = 0; i < plan.ParamTypes.Length; i++, arg++)
        {
            if (refs[i] is null) continue;
            var element = plan.ParamTypes[i].GetElementType()!;
            il.Emit(OpCodes.Ldarg, arg);
            EmitAliasedLocalAddress(il, plan, i, refs);
            il.Emit(OpCodes.Ldobj, refs[i].LocalType);
            EmitNativeToManaged(il, element, refs[i].LocalType);
            il.Emit(OpCodes.Stobj, element);
        }
        if (result is not null)
        {
            il.Emit(OpCodes.Ldloc, result);
            EmitNativeToManaged(il, plan.ReturnType, plan.NativeReturn);
        }
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate(plan.OrigType);
    }
}
