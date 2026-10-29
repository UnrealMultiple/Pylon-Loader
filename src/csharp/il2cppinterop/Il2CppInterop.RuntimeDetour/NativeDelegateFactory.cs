#nullable disable
// 层：Hook 引擎 —— 原生签名的委托类型工厂。
//
// 为什么要自己造委托类型：Marshal.GetFunctionPointerForDelegate 要求委托签名能直接当原生 ABI 用。
// 入口桩的签名因此是"原生槽位类型"（IntPtr / 基础类型 / 可直传的值类型），不能直接拿游戏方法的
// 托管签名（里面是 interop 包装类）。同一个签名只造一次。
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;

namespace Il2CppInterop.RuntimeDetour
{
    /// <summary>按签名缓存生成的 Cdecl 委托类型。</summary>
    internal static class NativeDelegateFactory
    {
        private static readonly Dictionary<string, Type> Cache = new(StringComparer.Ordinal);

        private static readonly ModuleBuilder Module = AssemblyBuilder
            .DefineDynamicAssembly(new AssemblyName("Pylon.Hook.Stubs"), AssemblyBuilderAccess.Run)
            .DefineDynamicModule("Pylon.Hook.Stubs");

        private static int _next;

        internal static Type Create(Type returnType, Type[] paramTypes, CallingConvention convention)
        {
            var key = BuildKey(returnType, paramTypes, convention);
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var cached)) return cached;

                var name = "PylonNativeDelegate_" + (_next++).ToString("x");
                var builder = Module.DefineType(name,
                    TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, typeof(MulticastDelegate));

                // MulticastDelegate 要求：ctor(object, native int) + Invoke(签名)
                var ctor = builder.DefineConstructor(
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.RTSpecialName,
                    CallingConventions.Standard, new[] { typeof(object), typeof(IntPtr) });
                ctor.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

                var invoke = builder.DefineMethod("Invoke",
                    MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot |
                    MethodAttributes.Virtual, returnType, paramTypes);
                invoke.SetImplementationFlags(MethodImplAttributes.Runtime | MethodImplAttributes.Managed);

                var attributeCtor = typeof(UnmanagedFunctionPointerAttribute)
                    .GetConstructor(new[] { typeof(CallingConvention) });
                if (attributeCtor != null)
                {
                    builder.SetCustomAttribute(new CustomAttributeBuilder(attributeCtor, new object[] { convention }));
                }

                var created = builder.CreateType();
                Cache[key] = created;
                return created;
            }
        }

        private static string BuildKey(Type returnType, Type[] paramTypes, CallingConvention convention)
        {
            var text = new StringBuilder();
            text.Append((int)convention).Append('|').Append(returnType.FullName);
            if (paramTypes != null)
            {
                foreach (var type in paramTypes) text.Append('|').Append(type.FullName);
            }
            return text.ToString();
        }
    }
}



