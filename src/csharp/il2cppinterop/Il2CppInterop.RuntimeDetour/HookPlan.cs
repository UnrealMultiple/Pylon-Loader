using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime.InteropTypes;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>
///     The "signature plan" of a single hook. It translates the managed signature into native
///     slot types (reference/string -&gt; IntPtr, bool -&gt; byte, char -&gt; ushort, enum -&gt; underlying
///     integer, directly passable value types by value) and validates the handler shape.
///     Planning is pure analysis: no IL is emitted and no memory is touched here.
/// </summary>
internal sealed class HookPlan
{
    private HookPlan()
    {
    }

    public MethodInfo Target { get; private set; } = null!;
    public Type DeclaringType { get; private set; } = null!;
    public bool IsStatic { get; private set; }
    public Type ReturnType { get; private set; } = null!;
    public Type[] ParamTypes { get; private set; } = null!;

    /// <summary>Out parameters (entry stubs do not pre-read them; they hand out default values).</summary>
    public bool[] ParamIsOut { get; private set; } = null!;

    /// <summary>Native slot types: the entry stub's parameter table (excluding the hidden methodInfo).</summary>
    public Type[] NativeParams { get; private set; } = null!;

    /// <summary>Native return type.</summary>
    public Type NativeReturn { get; private set; } = null!;

    /// <summary>Handler delegate type: (orig, self?, params...).</summary>
    public Type HandlerType { get; private set; } = null!;

    /// <summary>Orig delegate type: the target method's own managed signature.</summary>
    public Type OrigType { get; private set; } = null!;

    /// <summary>il2cpp MethodInfo* (passed as the hidden trailing argument of native calls).</summary>
    public IntPtr MethodInfoPtr { get; private set; }

    /// <summary>Native code address of the target method (what Dobby patches).</summary>
    public IntPtr FunctionPtr { get; private set; }

    public string Label => $"{DeclaringType.Name}.{Target.Name}";

    /// <summary>Test hook: bypasses il2cpp pointer resolution and supplies (MethodInfo*, code address) directly.</summary>
    internal static Func<MethodInfo, (IntPtr methodInfo, IntPtr function)>? PointersOverride;

    internal static HookPlan Create(MethodBase target, Delegate handler)
    {
        if (target is not MethodInfo method)
            throw new NotSupportedException(
                "Only MethodInfo targets are supported (resolve constructors/property accessors to a concrete method first)");
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        if (method.DeclaringType is null)
            throw new NotSupportedException("The target method has no declaring type");
        if (method.DeclaringType.ContainsGenericParameters)
            throw new NotSupportedException("Methods on open generic types are not supported");
        if (method.IsGenericMethodDefinition || method.ContainsGenericParameters)
            throw new NotSupportedException("Open generic methods are not supported; pass the closed concrete method");

        var parameters = method.GetParameters();
        var plan = new HookPlan
        {
            Target = method,
            DeclaringType = method.DeclaringType,
            IsStatic = method.IsStatic,
            ReturnType = method.ReturnType,
            ParamTypes = parameters.Select(p => p.ParameterType).ToArray(),
            ParamIsOut = parameters.Select(p => p.IsOut).ToArray(),
        };

        // handler: (orig, self?, params...)
        var handlerParameters = handler.Method.GetParameters();
        if (handlerParameters.Length == 0 || !typeof(Delegate).IsAssignableFrom(handlerParameters[0].ParameterType))
            throw new ArgumentException("The first handler parameter must be the orig delegate (e.g. Action<NPC>)", nameof(handler));

        plan.HandlerType = handler.GetType();
        plan.OrigType = handlerParameters[0].ParameterType;
        plan.ValidateHandler(handlerParameters);
        plan.CheckSupportedTypes();

        plan.NativeParams = BuildNativeParams(plan);
        plan.NativeReturn = NativeSlot(plan.ReturnType, $"{plan.Label} return value");

        plan.ResolveNativePointers();
        return plan;
    }

    /// <summary>The handler's parameter table must equal (orig, self?, params...) with matching types.</summary>
    private void ValidateHandler(ParameterInfo[] handlerParameters)
    {
        var invoke = OrigType.GetMethod("Invoke");
        if (invoke is null) throw new ArgumentException("orig must be a delegate type", nameof(handlerParameters));

        var expected = new List<Type>();
        if (!IsStatic) expected.Add(DeclaringType);
        expected.AddRange(ParamTypes);

        var actual = invoke.GetParameters().Select(p => p.ParameterType).ToArray();
        var expectedArray = expected.ToArray();

        var same = actual.Length == expectedArray.Length &&
                   invoke.ReturnType == ReturnType &&
                   !actual.Where((t, i) => t != expectedArray[i]).Any();
        if (!same)
        {
            throw new ArgumentException(
                $"The orig delegate signature does not match the target: expected {Describe(ReturnType, expectedArray)}, got {Describe(invoke.ReturnType, actual)}",
                nameof(handlerParameters));
        }

        var expectedCount = 1 + expectedArray.Length;
        if (handlerParameters.Length != expectedCount)
        {
            throw new ArgumentException(
                $"The handler needs {expectedCount} parameters (orig + {(IsStatic ? "target parameters" : "self + target parameters")}), got {handlerParameters.Length}",
                nameof(handlerParameters));
        }
    }

    /// <summary>Capability check: anything expressible as a native slot passes; unsupported shapes throw loudly.</summary>
    private void CheckSupportedTypes()
    {
        foreach (var parameter in ParamTypes)
        {
            var type = parameter.IsByRef ? parameter.GetElementType() : parameter;
            if (type is null) throw new NotSupportedException($"{Label} has an unresolvable parameter type");
            if (type.IsPointer)
                throw new NotSupportedException($"{Label} has a pointer parameter, which is not supported");
            if (type.IsByRef)
                throw new NotSupportedException($"{Label} has a nested byref parameter, which is not supported");
            // Run the slot mapping once; a failure throws with an explanatory message.
            NativeSlot(type, $"{Label} parameter {type.Name}");
        }

        if (ReturnType.IsByRef)
            throw new NotSupportedException($"{Label} returns byref, which is not supported");

        NativeSlot(ReturnType, $"{Label} return value");

        if (!IsStatic && !typeof(Il2CppObjectBase).IsAssignableFrom(DeclaringType))
        {
            throw new NotSupportedException(
                $"{Label} is an instance method but its declaring type {DeclaringType.Name} is not an Il2CppObjectBase derivative (not a game type?)");
        }
    }

    private static Type[] BuildNativeParams(HookPlan plan)
    {
        var list = new List<Type>();
        if (!plan.IsStatic) list.Add(typeof(IntPtr)); // self
        foreach (var parameter in plan.ParamTypes) list.Add(NativeSlot(parameter, $"{plan.Label} parameter"));
        return list.ToArray();
    }

    /// <summary>Managed type -> native slot type.</summary>
    internal static Type NativeSlot(Type type, string what)
    {
        if (type is null) throw new NotSupportedException($"{what}: type is null");
        if (type.IsByRef)
        {
            var element = type.GetElementType();
            if (element is null) throw new NotSupportedException($"{what}: byref element is null");
            if (IsBoxedValueType(element)) return typeof(IntPtr);                 // ref/out boxed value type: data pointer
            if (IsReferenceLike(element)) return typeof(IntPtr).MakeByRefType(); // ref/out reference: pointer slot
            return NativeSlot(element, what).MakeByRefType();                     // value type: raw pointer
        }
        if (type == typeof(string)) return typeof(IntPtr);
        if (IsBoxedValueType(type)) return MirrorTypes.For(type).Mirror;          // boxed value type passed by value
        if (typeof(Il2CppObjectBase).IsAssignableFrom(type)) return typeof(IntPtr);
        if (type == typeof(bool)) return typeof(byte);                 // il2cpp bool is 1 byte
        if (type == typeof(char)) return typeof(ushort);
        if (type.IsEnum) return NativeSlot(Enum.GetUnderlyingType(type), what);
        if (type.IsPrimitive) return type;
        if (type == typeof(IntPtr) || type == typeof(UIntPtr)) return type;

        if (type.IsArray)
            throw new NotSupportedException($"{what}: {type.Name} is a CLR array (should not appear in il2cpp proxies), not supported");
        if (type.IsPointer)
            throw new NotSupportedException($"{what}: pointer types are not supported");
        if (type.IsGenericType && type.GetGenericTypeDefinition() != typeof(Nullable<>))
            throw new NotSupportedException($"{what}: generic value type {type.Name} has an unpredictable layout, not supported");
        if (IsBlittableValue(type)) return type;

        throw new NotSupportedException(
            $"{what}: {type.Name} is not a directly passable value type (contains reference fields or has unknown layout)");
    }

    internal static bool IsReferenceLike(Type type) =>
        type == typeof(string) || (typeof(Il2CppObjectBase).IsAssignableFrom(type) && !IsBoxedValueType(type));

    /// <summary>
    ///     "Class proxy passed by value" value types: Il2CppInterop generates generic value types
    ///     (Il2CppSystem.Nullable&lt;T&gt; etc.) as Il2CppSystem.ValueType derivatives, but IL2CPP compiles
    ///     the native signatures with the struct passed by value. Such parameters are bridged with a
    ///     mirror struct + boxing.
    /// </summary>
    internal static bool IsBoxedValueType(Type type)
    {
        if (type is null || type.IsValueType) return false;
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.FullName == "Il2CppSystem.ValueType") return true;
        }
        return false;
    }

    /// <summary>Whether a type can be passed by value directly: only primitive value types/enums/nested blittable structs.</summary>
    internal static bool IsBlittableValue(Type type)
    {
        if (!type.IsValueType) return false;
        if (type.IsPrimitive) return true;
        if (type.IsEnum) return true;
        if (type == typeof(IntPtr) || type == typeof(UIntPtr)) return true;
        if (type.IsPointer || type.ContainsGenericParameters) return false;
        if (type.IsGenericType)
        {
            if (type.GetGenericTypeDefinition() != typeof(Nullable<>)) return false;
            return IsBlittableValue(type.GetGenericArguments()[0]);
        }

        var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        foreach (var field in fields)
        {
            if (field.FieldType.IsPointer) return false;
            if (!field.FieldType.IsValueType) return false;
            if (!IsBlittableValue(field.FieldType)) return false;
        }
        return true;
    }

    /// <summary>Managed MethodInfo -&gt; il2cpp MethodInfo* and code address.</summary>
    private void ResolveNativePointers()
    {
        if (PointersOverride is not null)
        {
            (MethodInfoPtr, FunctionPtr) = PointersOverride(Target);
            return;
        }

        var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(Target);
        if (field is null)
        {
            var accessor = Il2CppInteropUtils.GetIl2CppFieldInfoPointerFieldForGeneratedFieldAccessor(Target);
            if (accessor is not null)
                throw new NotSupportedException($"{Label} is a field accessor and cannot be hooked");
            throw new NotSupportedException($"{Label} has no il2cpp method pointer field (not an interop-generated proxy method?)");
        }

        var methodInfoValue = field.GetValue(null);
        if (methodInfoValue is null)
            throw new InvalidOperationException($"{Label} has an empty MethodInfo* field (type not initialized yet?)");
        MethodInfoPtr = (IntPtr)methodInfoValue;
        if (MethodInfoPtr == IntPtr.Zero)
            throw new InvalidOperationException($"{Label} has an empty MethodInfo* (type not initialized yet?)");

        // The first pointer-width field of IL2CPP's MethodInfo is methodPointer (same convention as
        // the kernel's ModApi.cpp).
        FunctionPtr = Marshal.ReadIntPtr(MethodInfoPtr);
        if (FunctionPtr == IntPtr.Zero)
            throw new InvalidOperationException($"{Label} has an empty methodPointer (method not compiled?)");
    }

    internal static string Describe(Type returnType, Type[] parameters)
    {
        var text = new System.Text.StringBuilder();
        text.Append(returnType?.Name).Append(" (");
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0) text.Append(", ");
            text.Append(parameters[i].Name);
        }
        return text.Append(')').ToString();
    }
}