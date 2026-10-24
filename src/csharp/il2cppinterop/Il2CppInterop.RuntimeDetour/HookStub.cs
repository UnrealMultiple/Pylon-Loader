using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Il2CppInterop.Runtime;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>Strongly typed managed dispatch and the native boundary's conversion helpers.</summary>
internal static partial class HookStub
{
    internal const string EntryPrefix = "(pylon -> managed) ";
    internal const string OrigPrefix = "(managed -> pylon) ";
    private static MethodInfo StateMethod(string name) => typeof(HookState).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
    internal static readonly MethodInfo HandlerGetter = StateMethod(nameof(HookState.GetHandler));
    internal static readonly MethodInfo OrigDelegateGetter = StateMethod(nameof(HookState.GetOrigDelegate));
    internal static readonly MethodInfo OrigPointerGetter = StateMethod(nameof(HookState.GetOrig));
    internal static readonly MethodInfo EnterMethod = StateMethod(nameof(HookState.EnterInvocation));
    internal static readonly MethodInfo ExitMethod = StateMethod(nameof(HookState.ExitInvocation));
    internal static readonly MethodInfo EnterNativeMethod = StateMethod(nameof(HookState.EnterNative));
    internal static readonly MethodInfo ExitNativeMethod = StateMethod(nameof(HookState.ExitNative));
    internal static readonly MethodInfo ReportThrowableMethod = StateMethod(nameof(HookState.ReportThrowable));
    internal static readonly FieldInfo ManagedToIl2CppField = typeof(HookStub).GetField(nameof(ManagedToIl2Cpp), BindingFlags.Static | BindingFlags.NonPublic)!;
    internal static readonly FieldInfo Il2CppToManagedField = typeof(HookStub).GetField(nameof(Il2CppToManaged), BindingFlags.Static | BindingFlags.NonPublic)!;
    internal static readonly MethodInfo ToPtr = typeof(IL2CPP).GetMethod(nameof(IL2CPP.Il2CppObjectBaseToPtr))!;
    internal static readonly MethodInfo GetTypeFromHandle = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!;
    internal static readonly MethodInfo GetClassPointer = typeof(Il2CppClassPointerStore).GetMethod(nameof(Il2CppClassPointerStore.GetNativeClassPointer), new[] { typeof(Type) })!;
    internal static readonly MethodInfo ValueBox = typeof(IL2CPP).GetMethod(nameof(IL2CPP.il2cpp_value_box))!;
    internal static readonly MethodInfo ObjectUnbox = typeof(IL2CPP).GetMethod(nameof(IL2CPP.il2cpp_object_unbox))!;
    internal static readonly MethodInfo RecordIndirectMethod = typeof(HookStub).GetMethod(nameof(RecordIndirectArg), BindingFlags.Static | BindingFlags.NonPublic)!;
    internal static readonly MethodInfo GetIndirectMethod = StateMethod(nameof(HookState.GetIndirectArg));
    internal static readonly MethodInfo StringToManagedInvoke = typeof(Func<IntPtr, string?>).GetMethod("Invoke")!;
    internal static readonly MethodInfo ManagedToStringInvoke = typeof(Func<string, IntPtr>).GetMethod("Invoke")!;
    internal static Func<string, IntPtr> ManagedToIl2Cpp = IL2CPP.ManagedStringToIl2Cpp;
    internal static Func<IntPtr, string?> Il2CppToManaged = IL2CPP.Il2CppStringToManaged;
    private static void RecordIndirectArg(IntPtr pointer, int slot, int paramIndex) => HookState.SetIndirectArg(slot, paramIndex, pointer);

    /// <summary>Loads the first converted local whose source byref aliases this argument.</summary>
    private static void EmitAliasedLocalAddress(ILGenerator il, HookPlan plan, int index, LocalBuilder[] locals)
    {
        var argument = (plan.IsStatic ? 0 : 1) + index;
        var end = il.DefineLabel();
        for (var previous = 0; previous < index; previous++)
        {
            if (locals[previous] is null || locals[previous].LocalType != locals[index].LocalType) continue;
            var different = il.DefineLabel();
            il.Emit(OpCodes.Ldarg, argument);
            il.Emit(OpCodes.Ldarg, (plan.IsStatic ? 0 : 1) + previous);
            il.Emit(OpCodes.Bne_Un, different);
            il.Emit(OpCodes.Ldloca, locals[previous]);
            il.Emit(OpCodes.Br, end);
            il.MarkLabel(different);
        }
        il.Emit(OpCodes.Ldloca, locals[index]);
        il.MarkLabel(end);
    }

    internal static Delegate BuildDispatcher(HookChain site)
    {
        var invoke = site.Plan.HandlerType.GetMethod("Invoke")!;
        var parameters = new[] { typeof(HookChain) }.Concat(invoke.GetParameters().Select(p => p.ParameterType)).ToArray();
        var method = new DynamicMethod("hook dispatch " + site.Plan.Label, invoke.ReturnType, parameters, typeof(HookStub).Module, true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(HookChain).GetMethod(nameof(HookChain.ReadDispatch), BindingFlags.Instance | BindingFlags.NonPublic)!);
        il.Emit(OpCodes.Castclass, site.Plan.OrigType);
        // The handler ABI has an orig argument; dispatch itself reads the latest chain instead.
        for (var i = 2; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, i);
        il.Emit(OpCodes.Callvirt, site.Plan.OrigType.GetMethod("Invoke")!);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate(site.Plan.HandlerType, site);
    }

    internal static Delegate BuildNext(HookPlan plan, HookNode node, Type siteOrigType)
    {
        var invoke = plan.OrigType.GetMethod("Invoke")!;
        var parameters = new[] { typeof(HookNode) }.Concat(invoke.GetParameters().Select(p => p.ParameterType)).ToArray();
        var method = new DynamicMethod("hook next " + plan.Label, invoke.ReturnType, parameters, typeof(HookStub).Module, true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(HookNode).GetMethod(nameof(HookNode.ReadNext), BindingFlags.Instance | BindingFlags.NonPublic)!);
        il.Emit(OpCodes.Castclass, siteOrigType);
        for (var i = 1; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, i);
        il.Emit(OpCodes.Callvirt, siteOrigType.GetMethod("Invoke")!);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate(plan.OrigType, node);
    }

    internal static Delegate BuildManagedCall(HookPlan plan, HookNode node, Type siteOrigType)
    {
        var invoke = plan.OrigType.GetMethod("Invoke")!;
        var parameters = new[] { typeof(HookNode) }.Concat(invoke.GetParameters().Select(p => p.ParameterType)).ToArray();
        var method = new DynamicMethod("hook handler " + plan.Label, invoke.ReturnType, parameters, typeof(HookStub).Module, true);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeof(HookNode).GetField(nameof(HookNode.Handler), BindingFlags.Instance | BindingFlags.NonPublic)!);
        il.Emit(OpCodes.Castclass, plan.HandlerType);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, typeof(HookNode).GetField(nameof(HookNode.Original), BindingFlags.Instance | BindingFlags.NonPublic)!);
        il.Emit(OpCodes.Castclass, plan.OrigType);
        for (var i = 1; i < parameters.Length; i++) il.Emit(OpCodes.Ldarg, i);
        il.Emit(OpCodes.Callvirt, plan.HandlerType.GetMethod("Invoke")!);
        il.Emit(OpCodes.Ret);
        return method.CreateDelegate(siteOrigType, node);
    }
}
