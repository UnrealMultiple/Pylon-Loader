using System;
using System.Linq;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using Il2CppInterop.Common.XrefScans;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Startup;
using Microsoft.Extensions.Logging;

namespace Il2CppInterop.Runtime.Injection.Hooks
{
    internal unsafe class GenericMethod_GetMethod_Hook : Hook<GenericMethod_GetMethod_Hook.MethodDelegate>
    {
        public override string TargetMethodName => "GenericMethod::GetMethod";
        public override MethodDelegate GetDetour() => Hook;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate Il2CppMethodInfo* MethodDelegate(Il2CppGenericMethod* gmethod, bool copyMethodPtr);

        private Il2CppMethodInfo* Hook(Il2CppGenericMethod* gmethod, bool copyMethodPtr)
        {
            if (gmethod == null || gmethod->methodDefinition == null)
                return Original(gmethod, copyMethodPtr);

            if (ClassInjector.InflatedMethodFromContextDictionary.TryGetValue((IntPtr)gmethod->methodDefinition, out var methods))
            {
                var instancePointer = gmethod->context.method_inst;
                if (methods.Item2.TryGetValue((IntPtr)instancePointer, out var inflatedMethodPointer))
                    return (Il2CppMethodInfo*)inflatedMethodPointer;

                var typeArguments = new Type[instancePointer->type_argc];
                for (var i = 0; i < instancePointer->type_argc; i++)
                    typeArguments[i] = ClassInjector.SystemTypeFromIl2CppType(instancePointer->type_argv[i]);
                var inflatedMethod = methods.Item1.MakeGenericMethod(typeArguments);
                Logger.Instance.LogTrace("Inflated method: {InflatedMethod}", inflatedMethod.Name);
                inflatedMethodPointer = (IntPtr)ClassInjector.ConvertMethodInfo(inflatedMethod,
                    UnityVersionHandler.Wrap(UnityVersionHandler.Wrap(gmethod->methodDefinition).Class));
                methods.Item2.Add((IntPtr)instancePointer, inflatedMethodPointer);

                return (Il2CppMethodInfo*)inflatedMethodPointer;
            }

            return Original(gmethod, copyMethodPtr);
        }

        public override IntPtr FindTargetMethod()
        {
            // If the shim variant throws (e.g. unexpected jump-target pattern), retry without it.
            try
            {
                return TryFindTargetMethod();
            }
            catch
            {
                if (UnityVersionHandler.HasShimForGetMethod)
                {
                    var retry = TryFindTargetMethod(tryShim: false);
                    if (retry != IntPtr.Zero)
                        return retry;
                }
                throw;
            }
        }

        private IntPtr TryFindTargetMethod(bool tryShim = true)
        {
            var getVirtualMethodAPI = InjectorHelpers.GetIl2CppExport(nameof(IL2CPP.il2cpp_object_get_virtual_method));
            Logger.Instance.LogTrace("il2cpp_object_get_virtual_method: 0x{GetVirtualMethodApiAddress}", getVirtualMethodAPI.ToInt64().ToString("X2"));

            var getVirtualMethod = XrefScannerLowLevel.JumpTargets(getVirtualMethodAPI, false, 250).First();
            Logger.Instance.LogTrace("Object::GetVirtualMethod: 0x{GetVirtualMethodAddress}", getVirtualMethod.ToInt64().ToString("X2"));

            var getVirtualMethodXrefs = XrefScannerLowLevel.JumpTargets(getVirtualMethod, false, 250).ToArray();

            // If the game is built with IL2CPP Master setting, this will return 0 entries, so we do another xref scan with retn instructions ignored.
            if (getVirtualMethodXrefs.Length == 0)
            {
                return XrefScannerLowLevel.JumpTargets(getVirtualMethod, true, 250).Last();
            }

            // U2021.2.0+, there's additional shim that takes 3 parameters
            // On U2020.3.41+ there is also a shim, which gets inlined with one added in U2021.2.0+ in release builds
            if (UnityVersionHandler.HasShimForGetMethod && tryShim)
            {
                var shim = getVirtualMethodXrefs.Last();

                var shimXrefs = XrefScannerLowLevel.JumpTargets(shim, false, 250).ToArray();

                // If the xref count is 1, it probably means the target is after ret
                if (Il2CppInteropRuntime.Instance.UnityVersion.Major == 2020 && shimXrefs.Length == 1)
                {
                    shimXrefs = XrefScannerLowLevel.JumpTargets(shim, true, 250).ToArray();
                }

                return shimXrefs.Take(2).Last();
            }

            return getVirtualMethodXrefs.Last();
        }
    }
}
