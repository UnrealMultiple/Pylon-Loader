#if HOOK_TEST_SHIM
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.RuntimeDetour;
using NullableRectangleBox = Il2CppSystem.Nullable<BoxedTests.Rectangle>;

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
            NullableRectangleRoundTrip();
            Console.WriteLine("PASS BoxedTests (test shim; not native ABI certification)");
        }
        finally{HookPlan.PointersOverride=null;IL2CPP.Release();}
    }
    private static void NullableRectangleRoundTrip()
    {
        var calls=0;
        byte expectedHasValue=1;
        var expectedHeight=47;
        var expectedX=17;
        var expectedY=23;
        var expectedWidth=31;
        OutNative native=(p,mi)=>
        {
            Check(Marshal.ReadByte(p)==expectedHasValue,"nullable hasValue survives orig");
            Check(Marshal.ReadInt32(p,4)==expectedX && Marshal.ReadInt32(p,8)==expectedY && Marshal.ReadInt32(p,12)==expectedWidth && Marshal.ReadInt32(p,16)==expectedHeight,"nullable rectangle complete native layout survives orig");
            calls++;
        };
        SetPointers(native);
        using var hook=new Hook(typeof(Targets).GetMethod(nameof(Targets.NullableRectangle))!,new NullableRectangleHook((orig,value)=>
        {
            Check(value is not null,"nullable proxy exists");
            Check(Marshal.ReadByte(value!.Pointer)==expectedHasValue && Marshal.ReadInt32(value.Pointer,16)==expectedHeight,"entry preserves nullable full 20-byte payload");
            orig(value);
        }));
        byte* storage=stackalloc byte[20];
        new Span<byte>(storage,20).Clear();
        storage[0]=1;
        Marshal.WriteInt32((IntPtr)storage,4,17);Marshal.WriteInt32((IntPtr)storage,8,23);
        Marshal.WriteInt32((IntPtr)storage,12,31);Marshal.WriteInt32((IntPtr)storage,16,47);
        Call(hook,(IntPtr)storage);
        Check(calls==1,"nullable native callback executed after full-payload conversion");
        expectedHasValue=0;storage[0]=0;
        Call(hook,(IntPtr)storage);
        Check(calls==2,"HasValue=false retains its complete nullable payload");
        expectedX=expectedY=expectedWidth=expectedHeight=0;
        Call(hook,IntPtr.Zero);
        Check(calls==3,"null indirect pointer boxes a complete zero nullable local");
        NullableRefRoundTrip();
        NullableLocalRoundTrip();
    }
    private static void NullableRefRoundTrip()
    {
        var calls=0;
        OutNative native=(p,mi)=>{Check(Marshal.ReadInt32(p,16)==47,"ref nullable orig payload");Marshal.WriteInt32(p,16,53);calls++;};
        SetPointers(native);
        using var hook=new Hook(typeof(Targets).GetMethod(nameof(Targets.NullableRef))!,new NullableRefHook((NullableRefOrig orig,ref NullableRectangleBox value)=>
        {
            orig(ref value);
            Check(Marshal.ReadInt32(value.Pointer,16)==53,"ref nullable refresh after orig");
            Marshal.WriteInt32(value.Pointer,16,59);
        }));
        byte* storage=stackalloc byte[20];new Span<byte>(storage,20).Clear();storage[0]=1;Marshal.WriteInt32((IntPtr)storage,16,47);
        Call(hook,(IntPtr)storage);
        Check(calls==1 && Marshal.ReadInt32((IntPtr)storage,16)==59,"ref nullable entry and orig full writeback");
    }
    private static void NullableLocalRoundTrip()
    {
        var calls=0;
        SmallNullableNative native=(value,mi)=>{Check(value.HasValue==1 && value.Value==123,"small nullable local native layout");calls++;};
        SetPointers(native);
        using var hook=new Hook(typeof(Targets).GetMethod(nameof(Targets.NullableInt))!,new NullableIntHook((orig,value)=>orig(value)));
        ((delegate* unmanaged[Cdecl]<SmallNullable,IntPtr,void>)hook.EntryForTest())(new SmallNullable{HasValue=1,Value=123},IntPtr.Zero);
        Check(calls==1,"small nullable mirror local conversion");
    }
    [StructLayout(LayoutKind.Explicit,Size=8)]
    public struct SmallNullable { [FieldOffset(0)] public byte HasValue;[FieldOffset(4)] public int Value; }
    public struct Rectangle { public int X,Y,Width,Height;public Rectangle(int x,int y,int width,int height){X=x;Y=y;Width=width;Height=height;} }
    private delegate void NullableRefOrig(ref NullableRectangleBox value);
    private delegate void NullableRefHook(NullableRefOrig orig,ref NullableRectangleBox value);
    private delegate void NullableIntOrig(Il2CppSystem.Nullable<int> value);
    private delegate void NullableIntHook(NullableIntOrig orig,Il2CppSystem.Nullable<int> value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate void SmallNullableNative(SmallNullable value,IntPtr mi);
    private delegate void NullableRectangleOrig(NullableRectangleBox value);
    private delegate void NullableRectangleHook(NullableRectangleOrig orig,NullableRectangleBox value);
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
    private static class Targets{public static void Make(out SmallBox value)=>throw new NotImplementedException();public static void Bump(ref SmallBox value){}public static long Large(LargeBox value)=>0;public static long Alias(ref SmallBox left,ref SmallBox right)=>0;public static void NullableRectangle(NullableRectangleBox value){}public static void NullableRef(ref NullableRectangleBox value){}public static void NullableInt(Il2CppSystem.Nullable<int> value){}}
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
