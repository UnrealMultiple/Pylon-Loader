using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>
///     A synthetic <see cref="MethodInfo"/> that forwards to a real <see cref="ConstructorInfo"/>.
///     The hook engine is MethodInfo-shaped end to end (handler validation, stub emission, plan
///     caching); instead of duplicating every branch, a constructor target is presented as an
///     instance method named ".ctor" whose only parameter is the self object.
/// </summary>
internal sealed class ConstructorTarget : MethodInfo
{
    private static readonly ConstructorInfo[] s_empty = Array.Empty<ConstructorInfo>();
    private static ConstructorInfo[]? s_wrapped;   // mirrors GetConstructors() of the cache below
    private static readonly object s_gate = new();

    private static readonly Dictionary<ConstructorInfo, ConstructorTarget> s_cache = new();

    private readonly ConstructorInfo _constructor;
    private readonly ParameterInfo[] _parameters;

    private ConstructorTarget(ConstructorInfo constructor)
    {
        _constructor = constructor;
        _parameters = new ParameterInfo[] { new SelfParameter(constructor.DeclaringType!) };
    }

    /// <summary>Wraps a constructor (one synthetic instance per real constructor, cached).</summary>
    internal static MethodInfo Wrap(ConstructorInfo constructor)
    {
        if (constructor is null) throw new ArgumentNullException(nameof(constructor));
        if (constructor.DeclaringType is null)
            throw new NotSupportedException("The constructor has no declaring type");

        lock (s_gate)
        {
            if (s_cache.TryGetValue(constructor, out var cached)) return cached;
            cached = new ConstructorTarget(constructor);
            s_cache[constructor] = cached;
            return cached;
        }
    }

    /// <summary>Returns the real constructor behind a (possibly wrapped) target.</summary>
    internal static ConstructorInfo Unwrap(MethodInfo method) =>
        method is ConstructorTarget wrapped ? wrapped._constructor : throw new ArgumentException("Not a constructor target", nameof(method));

    internal static bool IsConstructorTarget(MethodInfo method) => method is ConstructorTarget;

    // ---------------- forwarded identity ----------------

    public override MethodImplAttributes GetMethodImplementationFlags() => _constructor.GetMethodImplementationFlags();

    public override string Name => ".ctor";
    public override Type DeclaringType => _constructor.DeclaringType!;
    public override Type ReflectedType => _constructor.ReflectedType ?? _constructor.DeclaringType!;
    public override RuntimeMethodHandle MethodHandle => _constructor.MethodHandle;
    public override MethodAttributes Attributes => MethodAttributes.Public | MethodAttributes.HideBySig;
    public override MemberTypes MemberType => MemberTypes.Method;
    public override ICustomAttributeProvider ReturnTypeCustomAttributes => EmptyProvider.Instance;

    public override bool IsDefined(Type attributeType, bool inherit) => _constructor.IsDefined(attributeType, inherit);
    public override object[] GetCustomAttributes(bool inherit) => _constructor.GetCustomAttributes(inherit);
    public override object[] GetCustomAttributes(Type attributeType, bool inherit) => _constructor.GetCustomAttributes(attributeType, inherit);

    public override MethodInfo GetBaseDefinition() => this;
    public override ParameterInfo[] GetParameters() => _parameters;
    public override Type ReturnType => typeof(void);

    public override MethodInfo MakeGenericMethod(params Type[] typeArguments) =>
        throw new NotSupportedException("Constructor targets cannot be made generic");

    public override MethodInfo GetGenericMethodDefinition() =>
        throw new NotSupportedException("Constructor targets cannot be generic");

    /// <summary>Forwarded: pointer-field resolution decodes the proxy constructor's IL body.</summary>
    public override MethodBody? GetMethodBody() => _constructor.GetMethodBody();

    /// <summary>Invoking the synthetic method allocates the il2cpp object through the real constructor.</summary>
    public override object? Invoke(object? obj, BindingFlags invokeAttr, Binder? binder, object?[]? parameters, CultureInfo? culture) =>
        _constructor.Invoke(obj, invokeAttr, binder, parameters, culture);

    // ---------------- the single synthetic "self" parameter ----------------

    private sealed class SelfParameter : ParameterInfo
    {
        private readonly Type _selfType;

        public SelfParameter(Type selfType) => _selfType = selfType;

        public override string Name => "self";
        public override Type ParameterType => _selfType;
        public override int Position => 0;
        public override ParameterAttributes Attributes => ParameterAttributes.None;
        public override MemberInfo Member => _selfType;
        public override bool IsDefined(Type attributeType, bool inherit) => false;
        public override object[] GetCustomAttributes(bool inherit) => Array.Empty<object>();
        public override object[] GetCustomAttributes(Type attributeType, bool inherit) => Array.Empty<object>();
    }

    private sealed class EmptyProvider : ICustomAttributeProvider
    {
        internal static readonly EmptyProvider Instance = new();
        public object[] GetCustomAttributes(bool inherit) => Array.Empty<object>();
        public object[] GetCustomAttributes(Type attributeType, bool inherit) => Array.Empty<object>();
        public bool IsDefined(Type attributeType, bool inherit) => false;
    }
}
