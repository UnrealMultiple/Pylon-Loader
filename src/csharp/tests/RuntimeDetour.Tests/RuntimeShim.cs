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
    public sealed class Nullable<T>:ValueType where T:struct {public Nullable(IntPtr p):base(p){}}
}
namespace Il2CppInterop.Runtime
{
    public static unsafe class RawValueBox
    {
        public static T Wrap<T>(IntPtr klass, IntPtr data, int size) where T:InteropTypes.Il2CppObjectBase
        {
            var p=IL2CPP.Allocate(size);
            Buffer.MemoryCopy((void*)data,(void*)p,size,size);
            return (T)Activator.CreateInstance(typeof(T),p)!;
        }
    }
    public static class Il2CppClassPointerStore
    {
        public static IntPtr GetNativeClassPointer(Type type)
        {
            if(type.IsGenericType && type.GetGenericTypeDefinition()==typeof(Il2CppSystem.Nullable<>))
            {
                var klass=new IntPtr(Marshal.SizeOf(type.GetGenericArguments()[0])+4);
                IL2CPP.NullableClasses.Add(klass);
                return klass;
            }
            return new(type.Name=="LargeBox"?24:8);
        }
    }
    public static class IL2CPP
    {
        private static readonly List<IntPtr> Blocks=new();
        internal static readonly HashSet<IntPtr> NullableClasses=new();
        public static IntPtr Il2CppObjectBaseToPtr(InteropTypes.Il2CppObjectBase? value)=>value?.Pointer??IntPtr.Zero;
        public static IntPtr ManagedStringToIl2Cpp(string? value)=>IntPtr.Zero;
        public static string? Il2CppStringToManaged(IntPtr value)=>null;
        public static IntPtr il2cpp_object_unbox(IntPtr value)=>value;
        public static int il2cpp_class_value_size(IntPtr klass,ref uint alignment){alignment=8;return (int)klass;}
        public static unsafe IntPtr il2cpp_value_box(IntPtr klass,IntPtr data)
        {
            // IL2CPP boxes Nullable<T> as T (or null), never as a Nullable<T> object.
            if(NullableClasses.Contains(klass))
            {
                if(Marshal.ReadByte(data)==0)return IntPtr.Zero;
                var size=(int)klass;
                var nullableResult=Allocate(size); // Extra poison bytes make a wrong Nullable read deterministic.
                for(var i=0;i<size;i++)Marshal.WriteByte(nullableResult,i,0xCC);
                Buffer.MemoryCopy((byte*)data+4,(void*)nullableResult,size,size-4);
                return nullableResult;
            }
            var p=Allocate((int)klass);
            Buffer.MemoryCopy((void*)data,(void*)p,(int)klass,(int)klass);
            return p;
        }
        internal static IntPtr Allocate(int size)
        {
            var p=Marshal.AllocHGlobal(size);
            lock(Blocks)Blocks.Add(p);
            return p;
        }
        public static void Release(){lock(Blocks){foreach(var p in Blocks)Marshal.FreeHGlobal(p);Blocks.Clear();}}
    }
}
#endif
