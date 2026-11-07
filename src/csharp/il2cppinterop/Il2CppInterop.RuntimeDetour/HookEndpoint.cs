using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>
///     The runtime endpoint of one generated hook (On.X.Y). The generated event only forwards
///     subscribers here: the first Add resolves the target method and applies a <see cref="Hook"/>,
///     the last Remove undoes it. The event itself is strongly typed, so mod authors neither write
///     reflection strings nor manage hook lifetimes.
/// </summary>
public sealed class HookEndpoint
{
    private readonly Type _declaringType;
    private readonly string _methodName;
    private readonly Type[] _parameterTypes;
    private readonly Type _handlerType;
    private readonly List<Entry> _entries = new();
    private readonly object _gate = new();
    private MethodInfo _target = null!;

    private struct Entry
    {
        public Delegate Handler;
        public Hook Hook;
    }

    private int _parameterCount;

    private HookEndpoint(Type declaringType, string methodName, Type[] parameterTypes, Type handlerType)
    {
        _declaringType = declaringType;
        _methodName = methodName;
        _parameterTypes = parameterTypes;
        _handlerType = handlerType;
    }

    /// <summary>Binds an On.X.Y to a concrete method for the generated assemblies (exact parameter type match).</summary>
    public static HookEndpoint Bind(Type declaringType, string methodName, Type[] parameterTypes,
                                    Type origType, Type handlerType)
    {
        if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));
        if (methodName is null) throw new ArgumentNullException(nameof(methodName));
        if (handlerType is null) throw new ArgumentNullException(nameof(handlerType));
        var endpoint = new HookEndpoint(declaringType, methodName, parameterTypes ?? Type.EmptyTypes, handlerType);
        endpoint._target = Resolve(declaringType, methodName, endpoint._parameterTypes);
        return endpoint;
    }

    /// <summary>
    ///     Binds the On.X.Y of a generic member (a method on a generic type, or a generic method).
    ///     <paramref name="typeArgs"/> is the set of generic arguments that close over game types
    ///     (e.g. typeof(On.X&lt;T&gt;) for T). The hooked MethodInfo is resolved by name after
    ///     MakeGenericType(typeArgs); when resolution fails, the open definition is kept and the
    ///     closed method is instantiated when Add receives a concrete delegate.
    /// </summary>
    public static HookEndpoint Bind(Type declaringType, string methodName, Type[] parameterTypes,
                                    Type[] typeArgs, Type origType, Type handlerType)
    {
        if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));
        if (methodName is null) throw new ArgumentNullException(nameof(methodName));
        var endpoint = new HookEndpoint(declaringType, methodName, parameterTypes ?? Type.EmptyTypes, handlerType);
        endpoint._target = Resolve(declaringType, methodName, endpoint._parameterTypes, typeArgs);
        return endpoint;
    }

    /// <summary>
    ///     Creates the endpoint on first use (thread-safe lazy initialization), used by the generated
    ///     event add accessors so every endpoint skips its own null check.
    /// </summary>
    public static HookEndpoint BindOnce(ref HookEndpoint field, Type declaringType, string methodName,
                                         Type[] parameterTypes, Type origType, Type handlerType)
    {
        return LazyInitializer.EnsureInitialized(ref field,
            () => Bind(declaringType, methodName, parameterTypes, origType, handlerType));
    }

    /// <summary>
    ///     Compact bind used by the generated add accessors when the method name is unambiguous
    ///     (no overloads): the target resolves by name + parameter count alone, so the accessor
    ///     emits no parameter-type array IL at all (smaller proxy assemblies). Overloaded methods
    ///     keep the full <see cref="BindOnce(ref HookEndpoint, Type, string, Type[], Type, Type)"/> path.
    /// </summary>
    public static HookEndpoint BindOnce(ref HookEndpoint field, Type declaringType, string methodName,
                                         int parameterCount, Type origType, Type handlerType)
    {
        return LazyInitializer.EnsureInitialized(ref field,
            () =>
            {
                var endpoint = new HookEndpoint(declaringType, methodName, null, handlerType)
                {
                    _parameterCount = parameterCount
                };
                endpoint._target = Resolve(declaringType, methodName, parameterCount);
                return endpoint;
            });
    }

    /// <summary>
    ///     Endpoint initialization for generic (generic type / generic method) events.
    ///     <paramref name="typeArgs"/> is the set of generic arguments that close over game types;
    ///     null or empty is equivalent to <see cref="BindOnce(ref HookEndpoint, Type, string, Type[], Type, Type)"/>.
    /// </summary>
    public static HookEndpoint BindOnce(ref HookEndpoint field, Type declaringType, string methodName,
                                         Type[] parameterTypes, Type[] typeArgs, Type origType, Type handlerType)
    {
        return LazyInitializer.EnsureInitialized(ref field,
            () => Bind(declaringType, methodName, parameterTypes, typeArgs, origType, handlerType));
    }

    /// <summary>Label: Type.Method.</summary>
    public string Label => $"{_declaringType.Name}.{_methodName}";

    /// <summary>The resolved game method.</summary>
    public MethodInfo Target => _target;

    /// <summary>Number of currently subscribed handlers.</summary>
    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public void Add(Delegate handler)
    {
        if (handler is null) return;
        if (!_handlerType.IsInstanceOfType(handler))
        {
            throw new ArgumentException(
                $"{Label} requires a {_handlerType.Name} handler, got {handler.GetType().Name}", nameof(handler));
        }

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
                _entries[i].Hook.Dispose();
                _entries.RemoveAt(i);
                return;
            }
        }
    }

    /// <summary>Undoes every subscription on this endpoint (host shutdown / tests).</summary>
    public void UndoAll()
    {
        lock (_gate)
        {
            for (var i = _entries.Count - 1; i >= 0; i--) _entries[i].Hook.Dispose();
            _entries.Clear();
        }
    }

    private static MethodInfo Resolve(Type type, string name, Type[] parameterTypes) =>
        Resolve(type, name, parameterTypes, null);

    private static MethodInfo Resolve(Type type, string name, Type[] parameterTypes, Type[]? typeArgs)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var declaringType = typeArgs is { Length: > 0 } && type.IsGenericTypeDefinition
            ? type.MakeGenericType(typeArgs)
            : type;

        // Constructor events: resolve the ConstructorInfo and present it as a synthetic MethodInfo.
        if (name == ".ctor")
        {
            var ctor = declaringType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(c => MatchesParameters(c, parameterTypes));
            if (ctor is null)
                throw new MissingMethodException(
                    $"{declaringType.FullName}.ctor({parameterTypes.Length} params) was not found in the proxy assembly; the generated On.* does not match the current game's interop/generated assemblies?");
            return ConstructorTarget.Wrap(ctor);
        }

        MethodInfo? match = null;
        foreach (var method in declaringType.GetMethods(flags))
        {
            if (method.Name != name) continue;
            var parameters = method.GetParameters();
            if (parameters.Length != parameterTypes.Length) continue;
            var same = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].ParameterType != parameterTypes[i]) { same = false; break; }
            }
            if (!same) continue;
            match = method;
            break;
        }

        if (match is null)
        {
            // Generic methods: match the open definition by name (parameters contain generic
            // parameters, so exact comparison is impossible). Keeps the last match, as before.
            foreach (var method in declaringType.GetMethods(flags))
            {
                if (method.Name != name) continue;
                if (method.GetParameters().Length != parameterTypes.Length) continue;
                if (!method.IsGenericMethodDefinition) continue;
                match = method;
            }
        }

        if (match is null)
        {
            throw new MissingMethodException(
                $"{declaringType.FullName}.{name}({parameterTypes.Length} params) was not found in the proxy assembly; " +
                "the generated On.* does not match the current game's interop/generated assemblies?");
        }
        return match;
    }

    /// <summary>Unambiguous-name resolution: name + parameter count (generated fast path).</summary>
    private static MethodInfo Resolve(Type type, string name, int parameterCount)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var method in type.GetMethods(flags))
        {
            if (method.Name == name && method.GetParameters().Length == parameterCount) return method;
        }
        throw new MissingMethodException(
            $"{type.FullName}.{name}({parameterCount} params) was not found in the proxy assembly; " +
            "the generated On.* does not match the current game's interop/generated assemblies?");
    }

    /// <summary>Constructor parameter match: the requested list is (self + ctor parameters).</summary>
    private static bool MatchesParameters(ConstructorInfo constructor, Type[] parameterTypes)
    {
        if (parameterTypes.Length != constructor.GetParameters().Length + 1) return false;
        for (var i = 0; i < constructor.GetParameters().Length; i++)
        {
            if (constructor.GetParameters()[i].ParameterType != parameterTypes[i + 1]) return false;
        }
        return parameterTypes[0] == constructor.DeclaringType;
    }
}