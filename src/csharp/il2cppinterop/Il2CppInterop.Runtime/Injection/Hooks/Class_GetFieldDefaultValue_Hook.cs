using System;
using System.Linq;
using System.Runtime.InteropServices;
using Il2CppInterop.Common;
using Il2CppInterop.Common.Extensions;
using Il2CppInterop.Common.XrefScans;
using Il2CppInterop.Runtime.Runtime;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.Class;
using Il2CppInterop.Runtime.Runtime.VersionSpecific.FieldInfo;
using Il2CppInterop.Runtime.Startup;
using Microsoft.Extensions.Logging;

namespace Il2CppInterop.Runtime.Injection.Hooks;
internal unsafe class Class_GetFieldDefaultValue_Hook : Hook<Class_GetFieldDefaultValue_Hook.MethodDelegate>
{
    public override string TargetMethodName => "Class::GetDefaultFieldValue";
    public override MethodDelegate GetDetour() => Hook;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate byte* MethodDelegate(Il2CppFieldInfo* field, out Il2CppTypeStruct* type);

    private byte* Hook(Il2CppFieldInfo* field, out Il2CppTypeStruct* type)
    {
        if (EnumInjector.GetDefaultValueOverride(field, out IntPtr newDefaultPtr))
        {
            INativeFieldInfoStruct wrappedField = UnityVersionHandler.Wrap(field);
            INativeClassStruct wrappedParent = UnityVersionHandler.Wrap(wrappedField.Parent);
            INativeClassStruct wrappedElementClass = UnityVersionHandler.Wrap(wrappedParent.ElementClass);
            type = wrappedElementClass.ByValArg.TypePointer;
            return (byte*)newDefaultPtr;
        }
        return Original(field, out type);
    }

    private static nint FindClassGetFieldDefaultValueXref(bool forceICallMethod = false)
    {
        nint classGetDefaultFieldValue = 0;
        if (forceICallMethod)
        {
            // MonoField isn't present on 2021.2.0+
            var monoFieldType = InjectorHelpers.Il2CppMscorlib.GetTypesSafe().SingleOrDefault((x) => x.Name is "MonoField");
            if (monoFieldType == null)
                throw new Exception($"Unity {Il2CppInteropRuntime.Instance.UnityVersion} is not supported at the moment: MonoField isn't present in Il2Cppmscorlib.dll for unity version, unable to fetch icall");

            var monoFieldGetValueInternalThunk = InjectorHelpers.GetIl2CppMethodPointer(monoFieldType.GetMethod(nameof(Il2CppSystem.Reflection.MonoField.GetValueInternal)));
            Logger.Instance.LogTrace("Il2CppSystem.Reflection.MonoField::thunk_GetValueInternal: 0x{MonoFieldGetValueInternalThunkAddress}", monoFieldGetValueInternalThunk.ToInt64().ToString("X2"));

            var monoFieldGetValueInternal = XrefScannerLowLevel.JumpTargets(monoFieldGetValueInternalThunk, false, 250).Single();
            Logger.Instance.LogTrace("Il2CppSystem.Reflection.MonoField::GetValueInternal: 0x{MonoFieldGetValueInternalAddress}", monoFieldGetValueInternal.ToInt64().ToString("X2"));

            // Field::GetValueObject could be inlined with Field::GetValueObjectForThread
            var fieldGetValueObject = XrefScannerLowLevel.JumpTargets(monoFieldGetValueInternal, false, 250).Single();
            Logger.Instance.LogTrace("Field::GetValueObject: 0x{FieldGetValueObjectAddress}", fieldGetValueObject.ToInt64().ToString("X2"));

            var fieldGetValueObjectForThread = XrefScannerLowLevel.JumpTargets(fieldGetValueObject, false, 250).Last();
            Logger.Instance.LogTrace("Field::GetValueObjectForThread: 0x{FieldGetValueObjectForThreadAddress}", fieldGetValueObjectForThread.ToInt64().ToString("X2"));

            classGetDefaultFieldValue = XrefScannerLowLevel.JumpTargets(fieldGetValueObjectForThread, false, 250).ElementAt(2);
        }
        else
        {
            var getStaticFieldValueAPI = InjectorHelpers.GetIl2CppExport(nameof(IL2CPP.il2cpp_field_static_get_value));
            Logger.Instance.LogTrace("il2cpp_field_static_get_value: 0x{GetStaticFieldValueApiAddress}", getStaticFieldValueAPI.ToInt64().ToString("X2"));

            var getStaticFieldValue = XrefScannerLowLevel.JumpTargets(getStaticFieldValueAPI, false, 250).Single();
            Logger.Instance.LogTrace("Field::StaticGetValue: 0x{GetStaticFieldValueAddress}", getStaticFieldValue.ToInt64().ToString("X2"));

            var getStaticFieldValueTargets = XrefScannerLowLevel.JumpTargets(getStaticFieldValue, false, 250).ToList();

            // Sometimes the compiler can do an optimization and omit 'retn' instruction,
            // which then causes code following to grab wrong function pointer. A correct match should not contain more than 4 jumps
            // This optimization also causes Field::StaticGetValueInternal method to be located right under Field::StaticGetValue method
            // Example: https://discord.com/channels/623153565053222947/754333645199900723/1104817647171932283
            if (getStaticFieldValueTargets.Count > 4)
                return getStaticFieldValueTargets[^2];

            var getStaticFieldValueInternal = getStaticFieldValueTargets[^1];
            Logger.Instance.LogTrace("Field::StaticGetValueInternal: 0x{GetStaticFieldValueInternalAddress}", getStaticFieldValueInternal.ToInt64().ToString("X2"));

            var getStaticFieldValueInternalTargets = XrefScannerLowLevel.JumpTargets(getStaticFieldValueInternal, false, 250).ToArray();

            if (getStaticFieldValueInternalTargets.Length == 0) return FindClassGetFieldDefaultValueXref(true);

            classGetDefaultFieldValue = getStaticFieldValueInternalTargets.Length == 3 ? getStaticFieldValueInternalTargets.Last() : getStaticFieldValueInternalTargets.First();
        }

        if (!IsValidFunctionEntry(classGetDefaultFieldValue))
        {
            Logger.Instance.LogWarning("Resolved Class::GetDefaultFieldValue candidate 0x{Address} is not a valid hookable function entry point", classGetDefaultFieldValue.ToInt64().ToString("X2"));
            return IntPtr.Zero;
        }

        return classGetDefaultFieldValue;
    }

    private static bool IsValidFunctionEntry(nint address)
    {
        if (address == 0) return false;
        if ((address.ToInt64() & 3) != 0) return false;
        // On ARM64, verify that none of the first 4 instructions is RET
        for (var i = 0; i < 4; i++)
        {
            var opcode = *(uint*)(address + i * 4);
            if ((opcode & 0xFFFFFC1Fu) == 0xD65F0000u)
                return false;
        }
        return true;
    }

    public override IntPtr FindTargetMethod()
    {
        // NOTE: In some cases this pointer will be MetadataCache::GetFieldDefaultValueForField due to Field::GetDefaultFieldValue being
        // inlined but we'll treat it the same even though it doesn't receive the type parameter the RDX register
        // doesn't get cleared so we still get the same parameters
        return FindClassGetFieldDefaultValueXref();
    }
}
