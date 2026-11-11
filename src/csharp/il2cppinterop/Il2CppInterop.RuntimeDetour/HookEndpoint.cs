using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>A strongly typed generated event's binding and subscription lifetime.</summary>
public sealed class HookEndpoint
{
    private readonly Type _declaringType;
    private readonly string _methodName;
    private readonly Type _handlerType;
    private readonly List<Entry> _entries = new();
    private readonly object _gate = new();
    private readonly MethodInfo _target;
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private struct Entry
    {
        public Delegate Handler;
        public Hook Hook;
    }

    private HookEndpoint(MethodBase target, Type origType, Type handlerType)
    {
        HookPlan.ValidateDelegateSignature(target, origType, handlerType);
        _declaringType = target.DeclaringType!;
        _methodName = target.Name;
        _handlerType = handlerType;
        _target = target is ConstructorInfo constructor ? ConstructorTarget.Wrap(constructor) : (MethodInfo)target;
    }

    /// <summary>Binds an exact, non-generic method or constructor using only its actual parameters (no self).</summary>
    public static HookEndpoint Bind(Type declaringType, string methodName, Type[] parameterTypes,
                                    Type origType, Type handlerType) =>
        BindGeneric(declaringType, methodName, parameterTypes, Type.EmptyTypes, Type.EmptyTypes, origType, handlerType);

    /// <summary>
    /// Legacy binding: arguments close an open declaring type; on a non-generic declaring type
    /// only, they close a generic method. Use BindGeneric when both sets of arguments are needed.
    /// </summary>
    public static HookEndpoint Bind(Type declaringType, string methodName, Type[] parameterTypes,
                                    Type[] typeArgs, Type origType, Type handlerType)
    {
        if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));
        typeArgs ??= Type.EmptyTypes;
        if (declaringType.IsGenericTypeDefinition)
            return BindGeneric(declaringType, methodName, parameterTypes, typeArgs, Type.EmptyTypes, origType, handlerType);
        if (declaringType.IsGenericType && typeArgs.Length != 0)
            throw new ArgumentException("Legacy generic binding cannot mix declaring-type and method arguments; use BindGeneric", nameof(typeArgs));
        return BindGeneric(declaringType, methodName, parameterTypes, Type.EmptyTypes, typeArgs, origType, handlerType);
    }

    public static HookEndpoint BindOnce(ref HookEndpoint field, Type declaringType, string methodName,
                                        Type[] parameterTypes, Type origType, Type handlerType) =>
        LazyInitializer.EnsureInitialized(ref field,
            () => Bind(declaringType, methodName, parameterTypes, origType, handlerType));

    /// <summary>Compact binding is allowed only for one non-generic target with this name and parameter count.</summary>
    public static HookEndpoint BindOnce(ref HookEndpoint field, Type declaringType, string methodName,
                                        int parameterCount, Type origType, Type handlerType) =>
        LazyInitializer.EnsureInitialized(ref field, () =>
        {
            ValidateTypeAndName(declaringType, methodName);
            if (parameterCount < 0) throw new ArgumentOutOfRangeException(nameof(parameterCount));
            if (declaringType.ContainsGenericParameters)
                throw new ArgumentException("Compact binding requires a closed declaring type", nameof(declaringType));
            IEnumerable<MethodBase> candidates = methodName == ".ctor"
                ? declaringType.GetConstructors(Flags).Where(c => !c.IsStatic)
                : declaringType.GetMethods(Flags).Where(m => m.Name == methodName && !m.IsGenericMethod);
            var target = Unique(candidates.Where(m => m.GetParameters().Length == parameterCount),
                declaringType, methodName);
            return new HookEndpoint(target, origType, handlerType);
        });

    public static HookEndpoint BindOnce(ref HookEndpoint field, Type declaringType, string methodName,
                                        Type[] parameterTypes, Type[] typeArgs, Type origType, Type handlerType) =>
        LazyInitializer.EnsureInitialized(ref field,
            () => Bind(declaringType, methodName, parameterTypes, typeArgs, origType, handlerType));

    /// <summary>Binds a target after independently closing its declaring type and method arguments.</summary>
    public static HookEndpoint BindGeneric(Type declaringType, string methodName, Type[] parameterTypes,
                                           Type[] declaringTypeArgs, Type[] methodArgs, Type origType, Type handlerType)
    {
        ValidateTypeAndName(declaringType, methodName);
        parameterTypes ??= Type.EmptyTypes;
        declaringTypeArgs ??= Type.EmptyTypes;
        methodArgs ??= Type.EmptyTypes;
        if (parameterTypes.Any(t => t is null || t.ContainsGenericParameters))
            throw new ArgumentException("Parameter types must be concrete", nameof(parameterTypes));
        if (declaringType.IsGenericTypeDefinition)
            declaringType = declaringType.MakeGenericType(declaringTypeArgs);
        else if (declaringTypeArgs.Length != 0)
            throw new ArgumentException("Declaring-type arguments require an open generic type definition", nameof(declaringTypeArgs));
        if (declaringType.ContainsGenericParameters)
            throw new ArgumentException("The declaring type must be fully closed", nameof(declaringType));
        if (methodArgs.Any(t => t is null || t.ContainsGenericParameters))
            throw new ArgumentException("Method arguments must be concrete", nameof(methodArgs));

        var candidates = new List<MethodBase>();
        if (methodName == ".ctor")
        {
            if (methodArgs.Length != 0)
                throw new ArgumentException("Constructors cannot have method arguments", nameof(methodArgs));
            candidates.AddRange(declaringType.GetConstructors(Flags).Where(c => !c.IsStatic && MatchesParameters(c, parameterTypes)));
        }
        else
        {
            foreach (var definition in declaringType.GetMethods(Flags))
            {
                if (definition.Name != methodName) continue;
                MethodInfo method = definition;
                if (methodArgs.Length == 0)
                {
                    if (method.IsGenericMethod) continue;
                }
                else
                {
                    if (!method.IsGenericMethodDefinition || method.GetGenericArguments().Length != methodArgs.Length) continue;
                    try { method = method.MakeGenericMethod(methodArgs); }
                    catch (ArgumentException) { continue; } // This overload's generic constraints are not satisfied.
                }
                if (!method.ContainsGenericParameters && MatchesParameters(method, parameterTypes)) candidates.Add(method);
            }
        }
        return new HookEndpoint(Unique(candidates, declaringType, methodName), origType, handlerType);
    }

    public static HookEndpoint BindGenericOnce(ref HookEndpoint field, Type declaringType, string methodName,
                                               Type[] parameterTypes, Type[] declaringTypeArgs, Type[] methodArgs,
                                               Type origType, Type handlerType) =>
        LazyInitializer.EnsureInitialized(ref field,
            () => BindGeneric(declaringType, methodName, parameterTypes, declaringTypeArgs, methodArgs, origType, handlerType));

    public string Label => $"{_declaringType.Name}.{_methodName}";
    public MethodInfo Target => _target;
    public int Count { get { lock (_gate) return _entries.Count; } }

    public void Add(Delegate handler)
    {
        if (handler is null) return;
        if (!_handlerType.IsInstanceOfType(handler))
            throw new ArgumentException($"{Label} requires a {_handlerType.Name} handler, got {handler.GetType().Name}", nameof(handler));
        lock (_gate)
        {
            var hook = new Hook(_target, handler);
            _entries.Add(new Entry { Handler = handler, Hook = hook });
        }
    }

    public void Remove(Delegate handler)
    {
        if (handler is null) return;
        lock (_gate)
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (!_entries[i].Handler.Equals(handler)) continue;
                _entries[i].Hook.Undo();
                _entries.RemoveAt(i);
                return;
            }
        }
    }

    /// <summary>Removes successful entries individually; a failed Undo remains registered for retry.</summary>
    public void UndoAll()
    {
        lock (_gate)
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                _entries[i].Hook.Undo();
                _entries.RemoveAt(i);
            }
        }
    }

    private static void ValidateTypeAndName(Type type, string name)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (name is null) throw new ArgumentNullException(nameof(name));
    }

    private static bool MatchesParameters(MethodBase method, Type[] parameterTypes) =>
        method.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameterTypes);

    private static MethodBase Unique(IEnumerable<MethodBase> candidates, Type type, string name)
    {
        MethodBase? match = null;
        foreach (var candidate in candidates)
        {
            if (match is not null)
                throw new AmbiguousMatchException($"More than one exact target matches {type.FullName}.{name}");
            match = candidate;
        }
        return match ?? throw new MissingMethodException($"{type.FullName}.{name} was not found with the requested closed signature");
    }
}
