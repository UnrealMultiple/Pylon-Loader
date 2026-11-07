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
///     Constructs the stubs of a hook. Split across partial files by responsibility:
///     <para>· this file — shared member caches, injectable string conversions, the passthrough stub;</para>
///     <para>· <see cref="HookStubEntry"/> (HookStub.Entry.cs) — the entry stub (native ABI -> managed);</para>
///     <para>· <see cref="HookStubOrig"/> (HookStub.Orig.cs) — the orig stub (managed -> native ABI);</para>
///     <para>· HookStub.Conversions.cs — the slot conversion emitters shared by both stubs.</para>
///
///     Signature mapping (mirrors <see cref="HookPlan.NativeSlot"/>):
///       reference/string -> IntPtr; bool -> byte; char -> ushort; enum -> underlying integer;
///       real value types (Rectangle/Vector2...) by value; ref/out -> byref or IntPtr;
///       boxed value types (Il2CppSystem.Nullable&lt;T&gt; etc., class proxies passed by value)
///       -> mirror struct + il2cpp_value_box/unbox.
///
///     Both sides carry the hidden trailing MethodInfo*; per ECMA-335, calli pushes arguments
///     first and the function pointer last.
/// </summary>
internal static partial class HookStub
{
    internal const string EntryPrefix = "(pylon -> managed) ";
    internal const string OrigPrefix = "(managed -> pylon) ";
    private const string PassthroughPrefix = "(pylon passthrough) ";

    // ---------------- shared member caches (used by every partial part) ----------------

    internal static readonly MethodInfo HandlersGetter = typeof(List<Delegate>).GetProperty("Item")!.GetGetMethod()!;
    internal static readonly MethodInfo PointersGetter = typeof(List<IntPtr>).GetProperty("Item")!.GetGetMethod()!;
    internal static readonly FieldInfo HandlersField =
        typeof(HookState).GetField(nameof(HookState.s_handlers), BindingFlags.Static | BindingFlags.NonPublic)!;
    internal static readonly FieldInfo OrigDelegatesField =
        typeof(HookState).GetField(nameof(HookState.s_origDelegates), BindingFlags.Static | BindingFlags.NonPublic)!;
    internal static readonly FieldInfo OrigPointersField =
        typeof(HookState).GetField(nameof(HookState.s_origPointers), BindingFlags.Static | BindingFlags.NonPublic)!;
    internal static readonly FieldInfo ManagedToIl2CppField =
        typeof(HookStub).GetField(nameof(ManagedToIl2Cpp), BindingFlags.Static | BindingFlags.NonPublic)!;
    internal static readonly FieldInfo Il2CppToManagedField =
        typeof(HookStub).GetField(nameof(Il2CppToManaged), BindingFlags.Static | BindingFlags.NonPublic)!;

    internal static readonly MethodInfo ReportExceptionMethod =
        typeof(HookState).GetMethod(nameof(HookState.ReportException), BindingFlags.Static | BindingFlags.NonPublic)!;

    internal static readonly MethodInfo ToPtr = typeof(IL2CPP).GetMethod(nameof(IL2CPP.Il2CppObjectBaseToPtr))!;
    internal static readonly MethodInfo GetTypeFromHandle = typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!;
    internal static readonly MethodInfo GetClassPointer = typeof(Il2CppClassPointerStore)
        .GetMethod(nameof(Il2CppClassPointerStore.GetNativeClassPointer), new[] { typeof(Type) })!;
    internal static readonly MethodInfo ValueBox = typeof(IL2CPP).GetMethod(nameof(IL2CPP.il2cpp_value_box))!;
    internal static readonly MethodInfo ObjectUnbox = typeof(IL2CPP).GetMethod(nameof(IL2CPP.il2cpp_object_unbox))!;
    internal static readonly MethodInfo RecordIndirectMethod =
        typeof(HookStub).GetMethod(nameof(RecordIndirectArg), BindingFlags.Static | BindingFlags.NonPublic)!;
    internal static readonly MethodInfo GetIndirectMethod =
        typeof(HookState).GetMethod(nameof(HookState.GetIndirectArg), BindingFlags.Static | BindingFlags.NonPublic)!;
    internal static readonly MethodInfo StringToManagedInvoke =
        typeof(Func<IntPtr, string?>).GetMethod("Invoke")!;
    internal static readonly MethodInfo ManagedToStringInvoke =
        typeof(Func<string, IntPtr>).GetMethod("Invoke")!;

    /// <summary>Injectable string conversions (real IL2CPP on device; the desktop probe substitutes fakes).</summary>
    internal static Func<string, IntPtr> ManagedToIl2Cpp = IL2CPP.ManagedStringToIl2Cpp;
    internal static Func<IntPtr, string?> Il2CppToManaged = IL2CPP.Il2CppStringToManaged;

    /// <summary>D-014：entry 桩记录一个"&gt;16 字节结构体"实参的原始数据指针。</summary>
    private static void RecordIndirectArg(IntPtr pointer, int slot, int paramIndex) =>
        HookState.SetIndirectArg(slot, paramIndex, pointer);

    /// <summary>
    ///     Builds an "as-is forwarding" handler, used when the head of a chain is undone: the Dobby
    ///     hook stays in place but the slot only forwards to the next hop.
    /// </summary>
    internal static Delegate BuildPassthrough(HookPlan plan)
    {
        var handlerInvoke = plan.HandlerType.GetMethod("Invoke")!;
        var parameters = handlerInvoke.GetParameters().Select(p => p.ParameterType).ToArray();
        var method = new DynamicMethod(PassthroughPrefix + plan.Label, handlerInvoke.ReturnType, parameters,
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
}
