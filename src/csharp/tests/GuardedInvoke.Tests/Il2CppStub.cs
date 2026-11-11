using System.Runtime.InteropServices;

namespace Il2CppInterop.Runtime;

// Only the metadata required by GuardedInvoke is modelled; never load a game binary.
public static unsafe class IL2CPP
{
    public static int ReturnType = 0x08;
    public static int ValueSize = 4;
    public static uint Implementation = 0x1000;
    public static IntPtr Returned;
    public static IntPtr Exception;
    public static int UnboxCalls;
    public static Action? Callback;
    public static bool IsValue = true;
    public static int ParamCountCalls;
    public static uint il2cpp_method_get_param_count(IntPtr m) { ParamCountCalls++; return 0; }
    public static uint il2cpp_method_get_flags(IntPtr m, ref uint flags) { flags = Implementation; return 0; }
    public static bool il2cpp_method_is_instance(IntPtr m) => false;
    public static IntPtr il2cpp_method_get_class(IntPtr m) => (IntPtr)1;
    public static IntPtr il2cpp_method_get_param(IntPtr m, uint i) => (IntPtr)1;
    public static IntPtr il2cpp_method_get_return_type(IntPtr m) => (IntPtr)1;
    public static bool il2cpp_type_is_byref(IntPtr t) => false;
    public static int il2cpp_type_get_type(IntPtr t) => ReturnType;
    public static IntPtr il2cpp_class_from_type(IntPtr t) => (IntPtr)1;
    public static bool il2cpp_class_is_valuetype(IntPtr c) => IsValue;
    public static bool il2cpp_class_is_enum(IntPtr c) => false;
    public static bool il2cpp_class_is_generic(IntPtr c) => false;
    public static bool il2cpp_class_is_inflated(IntPtr c) => false;
    public static int il2cpp_class_value_size(IntPtr c, ref uint align) { align = 4; return ValueSize; }
    public static IntPtr il2cpp_class_get_fields(IntPtr c, ref IntPtr it) => IntPtr.Zero;
    public static int il2cpp_field_get_flags(IntPtr f) => 0;
    public static IntPtr il2cpp_field_get_type(IntPtr f) => (IntPtr)1;
    public static IntPtr il2cpp_object_unbox(IntPtr obj) { UnboxCalls++; return obj + 16; }
    public static IntPtr il2cpp_runtime_invoke(IntPtr m, IntPtr self, void** args, ref IntPtr exc)
    {
        var callback = Callback;
        Callback = null;
        callback?.Invoke();
        exc = Exception;
        return Returned;
    }
}
