#if HOOK_TEST_SHIM
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.RuntimeDetour;

internal static unsafe class BoxedTests
{
    private static readonly List<object> Roots=new();
    internal static void Run()
    {
        DetourBackend.Current=new Backend();
        try
        {
            OutNative native=(p,mi)=>Marshal.WriteInt64(p,40);
            SetPointers(native);
            using(var hook=new Hook(typeof(Targets).GetMethod(nameof(Targets.Make))!,new OutHook((OutOrig orig,out SmallBox value)=>{orig(out value);Check(value.Value==40,"orig boxed out refresh");value.Value=42;})))
            {
                long value=-1;Call(hook,(IntPtr)(&value));Check(value==42,"boxed out post-orig writeback");
            }
            using(var hook=new Hook(typeof(Targets).GetMethod(nameof(Targets.Make))!,new OutHook((OutOrig orig,out SmallBox value)=>value=SmallBox.Create(77))))
            {
                long value=-1;Call(hook,(IntPtr)(&value));Check(value==77,"boxed out replacement without orig");
            }
            OutNative bump=(p,mi)=>Marshal.WriteInt64(p,Marshal.ReadInt64(p)+2);
            SetPointers(bump);
            using(var hook=new Hook(typeof(Targets).GetMethod(nameof(Targets.Bump))!,new RefHook((RefOrig orig,ref SmallBox value)=>{value.Value+=10;orig(ref value);Check(value.Value==17,"boxed ref orig refresh");value.Value++;})))
            {
                long value=5;Call(hook,(IntPtr)(&value));Check(value==18,"boxed ref full writeback");
            }
            LargeNative large=(p,mi)=>{var value=Marshal.ReadInt64(p);Marshal.WriteInt64(p,value+1);return value;};
            SetPointers(large);
            Hook? active=null;var depth=0;
            using(var hook=new Hook(typeof(Targets).GetMethod(nameof(Targets.Large))!,new LargeHook((orig,value)=>
            {
                if(depth++==0){long* inner=stackalloc long[3];inner[0]=20;inner[1]=0;inner[2]=0;Check(CallLarge(active!,(IntPtr)inner)==30,"nested large replacement");}
                depth--;value.Value+=10;return orig(value);
            })))
            {
                active=hook;long* outer=stackalloc long[3];outer[0]=1;outer[1]=0;outer[2]=0;
                Check(CallLarge(hook,(IntPtr)outer)==11 && outer[0]==12,"outer indirect storage restored and replacement honored");
            }
            AliasNative alias=(left,right,mi)=>{Marshal.WriteInt64(left,1);Marshal.WriteInt64(right,2);return Marshal.ReadInt64(left);};
            SetPointers(alias);
            using(var hook=new Hook(typeof(Targets).GetMethod(nameof(Targets.Alias))!,new AliasHook((AliasOrig orig,ref SmallBox left,ref SmallBox right)=>orig(ref left,ref right))))
            {
                long value=0;var entry=(delegate* unmanaged[Cdecl]<IntPtr,IntPtr,IntPtr,long>)hook.EntryForTest();
                Check(entry((IntPtr)(&value),(IntPtr)(&value),IntPtr.Zero)==2 && value==2,"boxed aliased ref across entry and orig");
            }
            using(var hook=new Hook(typeof(Targets).GetMethod(nameof(Targets.Alias))!,new AliasHook((AliasOrig orig,ref SmallBox left,ref SmallBox right)=>{left=SmallBox.Create(1);right=SmallBox.Create(2);return left.Value;})))
            {
                long value=0;var entry=(delegate* unmanaged[Cdecl]<IntPtr,IntPtr,IntPtr,long>)hook.EntryForTest();
                Check(entry((IntPtr)(&value),(IntPtr)(&value),IntPtr.Zero)==2 && value==2,"boxed aliased ref replacement in handler");
            }
            Console.WriteLine("PASS BoxedTests (test shim; not native ABI certification)");
        }
        finally{HookPlan.PointersOverride=null;IL2CPP.Release();}
    }
    private static void SetPointers(Delegate native){Roots.Add(native);var p=Marshal.GetFunctionPointerForDelegate(native);HookPlan.PointersOverride=_=>(IntPtr.Zero,p);}
    private static void Call(Hook hook,IntPtr p)=>((delegate* unmanaged[Cdecl]<IntPtr,IntPtr,void>)hook.EntryForTest())(p,IntPtr.Zero);
    private static long CallLarge(Hook hook,IntPtr p)=>((delegate* unmanaged[Cdecl]<IntPtr,IntPtr,long>)hook.EntryForTest())(p,IntPtr.Zero);
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
    public sealed class SmallBox:Il2CppSystem.ValueType
    {
        public SmallBox(IntPtr p):base(p){}
        public long Value{get=>Marshal.ReadInt64(Pointer);set=>Marshal.WriteInt64(Pointer,value);}
        public static SmallBox Create(long value){long data=value;return new(IL2CPP.il2cpp_value_box(new IntPtr(8),(IntPtr)(&data)));}
    }
    public sealed class LargeBox:Il2CppSystem.ValueType
    {
        public LargeBox(IntPtr p):base(p){}
        public long Value{get=>Marshal.ReadInt64(Pointer);set=>Marshal.WriteInt64(Pointer,value);}
    }
    private static class Targets{public static void Make(out SmallBox value)=>throw new NotImplementedException();public static void Bump(ref SmallBox value){}public static long Large(LargeBox value)=>0;public static long Alias(ref SmallBox left,ref SmallBox right)=>0;}
    private delegate long AliasOrig(ref SmallBox left,ref SmallBox right);
    private delegate long AliasHook(AliasOrig orig,ref SmallBox left,ref SmallBox right);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate long AliasNative(IntPtr left,IntPtr right,IntPtr mi);
    private delegate void OutOrig(out SmallBox value);
    private delegate void OutHook(OutOrig orig,out SmallBox value);
    private delegate void RefOrig(ref SmallBox value);
    private delegate void RefHook(RefOrig orig,ref SmallBox value);
    private delegate long LargeOrig(LargeBox value);
    private delegate long LargeHook(LargeOrig orig,LargeBox value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void OutNative(IntPtr p,IntPtr mi);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate long LargeNative(IntPtr p,IntPtr mi);
    private sealed class Backend:DetourBackend{public override bool IsAvailable=>true;public override int Apply(IntPtr t,IntPtr d,out IntPtr o){o=t;return 0;}public override int Remove(IntPtr t)=>0;}
}
#endif
