using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;

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
    private static readonly Dictionary<string, Type> s_cache = new(StringComparer.Ordinal);

    private static readonly ModuleBuilder s_module = AssemblyBuilder
        .DefineDynamicAssembly(new AssemblyName("Pylon.Hook.Stubs"), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("Pylon.Hook.Stubs");

    private static int s_next;

    internal static Type Create(Type returnType, Type[] parameterTypes, CallingConvention convention)
    {
        var key = BuildKey(returnType, parameterTypes, convention);
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

    private static string BuildKey(Type returnType, Type[] parameterTypes, CallingConvention convention)
    {
        var text = new StringBuilder();
        text.Append((int)convention).Append('|').Append(returnType.FullName);
        if (parameterTypes is not null)
            foreach (var type in parameterTypes) text.Append('|').Append(type.FullName);
        return text.ToString();
    }
}
