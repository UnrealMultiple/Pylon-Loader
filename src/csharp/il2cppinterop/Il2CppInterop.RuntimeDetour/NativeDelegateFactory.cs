using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Il2CppInterop.RuntimeDetour;

/// <summary>
///     Creates and caches the Cdecl delegate types used by native entry stubs.
///     Marshal.GetFunctionPointerForDelegate requires a delegate whose signature is directly
///     usable as a native ABI, so stubs are typed with "native slot types" (IntPtr, primitives,
///     directly passable value types) instead of the game method's managed signature (which uses
///     interop wrapper classes). One delegate type is created per distinct signature.
/// </summary>
internal static class NativeDelegateFactory
{
    private static readonly Dictionary<SignatureKey, Type> s_cache = new();

    private static readonly ModuleBuilder s_module = AssemblyBuilder
        .DefineDynamicAssembly(new AssemblyName("Pylon.Hook.Stubs"), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("Pylon.Hook.Stubs");

    private static int s_next;

    internal static Type Create(Type returnType, Type[] parameterTypes, CallingConvention convention)
    {
        if (returnType is null) throw new ArgumentNullException(nameof(returnType));
        if (parameterTypes is null) throw new ArgumentNullException(nameof(parameterTypes));
        var key = new SignatureKey(returnType, parameterTypes, convention);
        lock (s_cache)
        {
            if (s_cache.TryGetValue(key, out var cached)) return cached;

            var name = "PylonNativeDelegate_" + s_next++.ToString("x");
            var builder = s_module.DefineType(name,
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, typeof(MulticastDelegate));

            // MulticastDelegate requires: ctor(object, native int) + Invoke(signature)
            var ctor = builder.DefineConstructor(
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.RTSpecialName,
                CallingConventions.Standard, new[] { typeof(object), typeof(IntPtr) });
            ctor.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

            var invoke = builder.DefineMethod("Invoke",
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot |
                MethodAttributes.Virtual, returnType, parameterTypes);
            invoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

            var attributeCtor = typeof(UnmanagedFunctionPointerAttribute)
                .GetConstructor(new[] { typeof(CallingConvention) });
            if (attributeCtor is not null)
                builder.SetCustomAttribute(new CustomAttributeBuilder(attributeCtor, new object[] { convention }));

            var created = builder.CreateTypeInfo().AsType();
            s_cache[key] = created;
            return created;
        }
    }

    private sealed class SignatureKey : IEquatable<SignatureKey>
    {
        private readonly Type _returnType;
        private readonly Type[] _parameterTypes;
        private readonly CallingConvention _convention;
        private readonly int _hash;

        internal SignatureKey(Type returnType, Type[] parameterTypes, CallingConvention convention)
        {
            _returnType = returnType;
            _parameterTypes = (Type[])parameterTypes.Clone();
            _convention = convention;
            var hash = new HashCode();
            hash.Add(returnType);
            hash.Add(convention);
            foreach (var parameter in _parameterTypes)
            {
                if (parameter is null) throw new ArgumentException("Parameter type cannot be null", nameof(parameterTypes));
                hash.Add(parameter);
            }
            _hash = hash.ToHashCode();
        }

        public bool Equals(SignatureKey? other)
        {
            if (other is null || _returnType != other._returnType || _convention != other._convention ||
                _parameterTypes.Length != other._parameterTypes.Length) return false;
            for (var i = 0; i < _parameterTypes.Length; i++)
                if (_parameterTypes[i] != other._parameterTypes[i]) return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is SignatureKey other && Equals(other);
        public override int GetHashCode() => _hash;
    }
}
