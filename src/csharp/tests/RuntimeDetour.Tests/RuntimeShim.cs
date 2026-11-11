#if HOOK_TEST_SHIM
// Test-only IL2CPP storage shim. Exercises CURRENT emitted IL and write-back semantics, not the
// real IL2CPP GC or ARM64 ABI. The normal test project never compiles these definitions.
using System.Runtime.InteropServices;
namespace Il2CppInterop.Common
{
    public static class Il2CppInteropUtils
    {
        public static System.Reflection.FieldInfo? GetIl2CppMethodInfoPointerFieldForGeneratedMethod(System.Reflection.MethodBase m)=>null;
        public static System.Reflection.FieldInfo? GetIl2CppFieldInfoPointerFieldForGeneratedFieldAccessor(System.Reflection.MethodBase m)=>null;
    }
}
namespace Il2CppInterop.Runtime.InteropTypes
{
    public class Il2CppObjectBase
    {
        public Il2CppObjectBase(IntPtr pointer)=>Pointer=pointer;
        public IntPtr Pointer{get;}
    }
}
namespace Il2CppSystem
{
    public class ValueType:Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase {public ValueType(IntPtr p):base(p){}}
}
namespace Il2CppInterop.Runtime
{
    public static class Il2CppClassPointerStore
    {
        public static IntPtr GetNativeClassPointer(Type type)=>new(type.Name=="LargeBox"?24:8);
    }
    public static class IL2CPP
    {
        private static readonly List<IntPtr> Blocks=new();
        public static IntPtr Il2CppObjectBaseToPtr(InteropTypes.Il2CppObjectBase? value)=>value?.Pointer??IntPtr.Zero;
        public static IntPtr ManagedStringToIl2Cpp(string? value)=>IntPtr.Zero;
        public static string? Il2CppStringToManaged(IntPtr value)=>null;
        public static IntPtr il2cpp_object_unbox(IntPtr value)=>value;
        public static int il2cpp_class_value_size(IntPtr klass,ref uint alignment){alignment=8;return (int)klass;}
        public static unsafe IntPtr il2cpp_value_box(IntPtr klass,IntPtr data)
        {
            var p=Marshal.AllocHGlobal((int)klass);
            lock(Blocks)Blocks.Add(p);
            Buffer.MemoryCopy((void*)data,(void*)p,(int)klass,(int)klass);
            return p;
        }
        public static void Release(){lock(Blocks){foreach(var p in Blocks)Marshal.FreeHGlobal(p);Blocks.Clear();}}
    }
}
#endif
