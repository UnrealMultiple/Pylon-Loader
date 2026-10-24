using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Il2CppInterop.RuntimeDetour;

BindingTests.Run();
EngineTests.Run();
#if HOOK_TEST_SHIM
BoxedTests.Run();
#endif
Console.WriteLine("ALL PASS RuntimeDetour.Tests");

internal static unsafe class EngineTests
{
    private static readonly List<object> Roots = new();
    private static FakeBackend backend = null!;
    internal static void Run()
    {
        backend = new FakeBackend();
        DetourBackend.Current = backend;
        NAdd native = (a,b,mi) => a+b;
        Roots.Add(native);
        var ptr = Marshal.GetFunctionPointerForDelegate(native);
        HookPlan.PointersOverride = _ => (IntPtr.Zero, ptr);
        var target = typeof(Targets).GetMethod(nameof(Targets.Add))!;
        var a = new Hook(target, new AddHandler((orig,x,y)=>orig(x,y)*10));
        var b = new Hook(target, new OtherHandler((orig,x,y)=>orig(x,y)+100));
        Check(Call(a,2,3)==1050,"mixed delegate type chain");
        var removes = backend.Removes;
        a.Undo();
        Check(Call(b,2,3)==105,"head removed, remaining handler active");
        b.Undo();
        Check(backend.Removes==removes+1 && HookManager.AppliedCount==0,"head-first undo removes native patch");
        var sites = HookState.SiteCount;
        for(var i=0;i<100;i++){using var hook=new Hook(target,new AddHandler((orig,x,y)=>orig(x,y)));Check(Call(hook,3,4)==7,"reapply");}
        Check(HookState.SiteCount==sites,"reapply reuses stable site");
        backend.FailApply = true;
        Throws<InvalidOperationException>(()=>new Hook(target,new AddHandler((orig,x,y)=>0)));
        Check(HookManager.AppliedCount==0,"failed apply not published");
        backend.FailApply=false;
        using(var hook=new Hook(target,new AddHandler((orig,x,y)=>orig(x,y)))) Check(Call(hook,1,2)==3,"apply after failure");
        InvocationFrames();
        RefOut();
        Conversions();
        ExceptionBoundary();
        ConcurrentSubscriptions(target);
        DeferredRemoval(target);
        GenericSharing();
        if(Environment.GetCommandLineArgs().Contains("--bench")) Bench(target, ptr);
        HookPlan.PointersOverride=null;
        Console.WriteLine("PASS EngineTests");
    }
    private static void InvocationFrames()
    {
        HookState.EnterInvocation(0,2);
        HookState.SetIndirectArg(0,0,new IntPtr(111));
        HookState.EnterInvocation(0,2);
        HookState.SetIndirectArg(0,0,new IntPtr(222));
        Check(HookState.GetIndirectArg(0,0)==new IntPtr(222),"inner frame");
        HookState.ExitInvocation();
        Check(HookState.GetIndirectArg(0,0)==new IntPtr(111),"outer frame restored");
        Task.Run(()=>{Check(HookState.GetIndirectArg(0,0)==IntPtr.Zero,"thread isolation");HookState.EnterInvocation(0,2);HookState.SetIndirectArg(0,0,new IntPtr(333));HookState.ExitInvocation();}).GetAwaiter().GetResult();
        HookState.ExitInvocation();
        Check(HookState.GetIndirectArg(0,0)==IntPtr.Zero,"no stale pointer");
    }
    private static void RefOut()
    {
        NBump native=(ref int value,IntPtr mi)=>value+=2;
        Roots.Add(native);
        var ptr=Marshal.GetFunctionPointerForDelegate(native);
        HookPlan.PointersOverride=_=>(IntPtr.Zero,ptr);
        using(var hook=new Hook(typeof(Targets).GetMethod(nameof(Targets.Bump))!,new BumpHandler((BumpOrig orig,ref int value)=>{value+=10;orig(ref value);}))){var value=5;((delegate* unmanaged[Cdecl]<int*,IntPtr,void>)hook.EntryForTest())(&value,IntPtr.Zero);Check(value==17,"ref int writeback");}
        NMake make=(out int value,IntPtr mi)=>value=7;
        Roots.Add(make);ptr=Marshal.GetFunctionPointerForDelegate(make);HookPlan.PointersOverride=_=>(IntPtr.Zero,ptr);
        using(var hook=new Hook(typeof(Targets).GetMethod(nameof(Targets.Make))!,new MakeHandler((MakeOrig orig,out int value)=>{orig(out value);value++;}))){int value;((delegate* unmanaged[Cdecl]<int*,IntPtr,void>)hook.EntryForTest())(&value,IntPtr.Zero);Check(value==8,"out int writeback");}
    }
    private static void Conversions()
    {
        NVec native=(value,mi)=>new Vec(value.Y,value.X);Roots.Add(native);var ptr=Marshal.GetFunctionPointerForDelegate(native);HookPlan.PointersOverride=_=>(IntPtr.Zero,ptr);
        using(var hook=new Hook(typeof(Targets).GetMethod(nameof(Targets.Flip))!,new VecHandler((orig,value)=>orig(value)))){var value=((delegate* unmanaged[Cdecl]<Vec,IntPtr,Vec>)hook.EntryForTest())(new Vec(1,2),IntPtr.Zero);Check(value.X==2 && value.Y==1,"blittable struct ABI");}
        NBool toggle=(value,mi)=>(byte)(value==0?1:0);Roots.Add(toggle);ptr=Marshal.GetFunctionPointerForDelegate(toggle);HookPlan.PointersOverride=_=>(IntPtr.Zero,ptr);
        using(var hook=new Hook(typeof(Targets).GetMethod(nameof(Targets.Toggle))!,new BoolHandler((orig,value)=>!orig(value)))) Check(((delegate* unmanaged[Cdecl]<byte,IntPtr,byte>)hook.EntryForTest())(1,IntPtr.Zero)==1,"bool byte ABI");
        var toManaged=HookStub.Il2CppToManaged;var toNative=HookStub.ManagedToIl2Cpp;
        try
        {
            HookStub.Il2CppToManaged=p=>p==new IntPtr(11)?"source":null;
            HookStub.ManagedToIl2Cpp=s=>s=="source!"?new IntPtr(22):IntPtr.Zero;
            NString name=mi=>new IntPtr(11);Roots.Add(name);ptr=Marshal.GetFunctionPointerForDelegate(name);HookPlan.PointersOverride=_=>(IntPtr.Zero,ptr);
            using var hook=new Hook(typeof(Targets).GetMethod(nameof(Targets.Name))!,new StringHandler(orig=>orig()+"!"));
            Check(((delegate* unmanaged[Cdecl]<IntPtr,IntPtr>)hook.EntryForTest())(IntPtr.Zero)==new IntPtr(22),"string return roundtrip");
        }
        finally{HookStub.Il2CppToManaged=toManaged;HookStub.ManagedToIl2Cpp=toNative;}
    }
    private static void ExceptionBoundary()
    {
        NAdd native=(x,y,mi)=>x+y;Roots.Add(native);var ptr=Marshal.GetFunctionPointerForDelegate(native);HookPlan.PointersOverride=_=>(IntPtr.Zero,ptr);
        using var hook=new Hook(typeof(Targets).GetMethod(nameof(Targets.Add))!,new AddHandler((orig,x,y)=>throw new Exception("expected")));
        Check(Call(hook,1,2)==0,"handler exception contained");
    }
    private static void ConcurrentSubscriptions(System.Reflection.MethodInfo target)
    {
        NAdd native=(x,y,mi)=>x+y;Roots.Add(native);var ptr=Marshal.GetFunctionPointerForDelegate(native);HookPlan.PointersOverride=_=>(IntPtr.Zero,ptr);
        Parallel.For(0,8,_=>{for(var i=0;i<30;i++){using var hook=new Hook(target,new AddHandler((orig,x,y)=>orig(x,y)));Check(Call(hook,1,2)==3,"concurrent dispatch");}});
        Check(HookManager.AppliedCount==0,"concurrent subscriptions fully removed");
    }
    private static void DeferredRemoval(System.Reflection.MethodInfo target)
    {
        NAdd native=(x,y,mi)=>x+y;Roots.Add(native);var ptr=Marshal.GetFunctionPointerForDelegate(native);HookPlan.PointersOverride=_=>(IntPtr.Zero,ptr);
        Hook? active=null;var removes=backend.Removes;
        using(var hook=new Hook(target,new AddHandler((orig,x,y)=>{active!.Undo();Check(backend.Removes==removes,"defer remove while handler active");return orig(x,y);}))){active=hook;Check(Call(hook,2,3)==5,"orig after self-undo");}
        Check(backend.Removes==removes+1,"remove after handler exit");
        backend.Safe=false;removes=backend.Removes;
        using(var hook=new Hook(target,new AddHandler((orig,x,y)=>orig(x,y)+1))){Check(Call(hook,2,3)==6,"unsafe backend handler");hook.Undo();Check(Call(hook,2,3)==5,"idle site forwards without user handler");}
        Check(backend.Removes==removes,"unsafe backend retains native patch");
        backend.Safe=true;HookState.FlushRetired();
        Check(backend.Removes==removes+1,"explicit safe retry drains idle site");
    }
    private static void GenericSharing()
    {
        NContext native=(x,mi)=>x+(int)mi;Roots.Add(native);var ptr=Marshal.GetFunctionPointerForDelegate(native);
        HookPlan.PointersOverride=_=>(new IntPtr(10),ptr);
        var target=typeof(Targets).GetMethod(nameof(Targets.Echo))!.MakeGenericMethod(typeof(int));
        using var hook=new Hook(target,new UnaryHandler((orig,x)=>orig(x)*2));
        var entry=(delegate* unmanaged[Cdecl]<int,IntPtr,int>)hook.EntryForTest();
        Check(entry(1,new IntPtr(10))==22,"matching generic context hooked");
        Check(entry(1,new IntPtr(20))==21,"other generic context forwards unchanged");
        HookPlan.PointersOverride=_=>(new IntPtr(20),ptr);
        Throws<NotSupportedException>(()=>new Hook(target,new UnaryHandler((orig,x)=>orig(x))));
    }
    private static void Bench(System.Reflection.MethodInfo target,IntPtr ptr)
    {
        HookPlan.PointersOverride=_=>(IntPtr.Zero,ptr);
        using var hook=new Hook(target,new AddHandler((orig,x,y)=>orig(x,y)+1));
        var entry=(delegate* unmanaged[Cdecl]<int,int,IntPtr,int>)hook.EntryForTest();
        var direct=(delegate* unmanaged[Cdecl]<int,int,IntPtr,int>)ptr;
        const int n=2_000_000;var sum=0;
        for(var i=0;i<100_000;i++)sum+=entry(1,2,IntPtr.Zero);
        var sw=System.Diagnostics.Stopwatch.StartNew();for(var i=0;i<n;i++)sum+=direct(1,2,IntPtr.Zero);sw.Stop();var baseline=sw.Elapsed.TotalNanoseconds/n;
        sw.Restart();for(var i=0;i<n;i++)sum+=entry(1,2,IntPtr.Zero);sw.Stop();Console.WriteLine($"BENCH direct={baseline:F1} ns, one={sw.Elapsed.TotalNanoseconds/n:F1} ns");
        using var second=new Hook(target,new AddHandler((orig,x,y)=>orig(x,y)+1));
        sw.Restart();for(var i=0;i<n;i++)sum+=entry(1,2,IntPtr.Zero);sw.Stop();Console.WriteLine($"BENCH two={sw.Elapsed.TotalNanoseconds/n:F1} ns checksum={sum}");
    }
    private static int Call(Hook hook,int a,int b)=>((delegate* unmanaged[Cdecl]<int,int,IntPtr,int>)hook.EntryForTest())(a,b,IntPtr.Zero);
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
    private static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static class Targets{public static int Add(int a,int b)=>a+b;public static void Bump(ref int value)=>value++;public static void Make(out int value)=>value=7;public static T Echo<T>(T value)=>value;public static Vec Flip(Vec value)=>value;public static bool Toggle(bool value)=>!value;public static string Name()=>"source";}
    [StructLayout(LayoutKind.Sequential)]private struct Vec{public float X,Y;public Vec(float x,float y){X=x;Y=y;}}
    private delegate Vec VecOrig(Vec value);
    private delegate Vec VecHandler(VecOrig orig,Vec value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate Vec NVec(Vec value,IntPtr mi);
    private delegate bool BoolOrig(bool value);
    private delegate bool BoolHandler(BoolOrig orig,bool value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate byte NBool(byte value,IntPtr mi);
    private delegate string StringOrig();
    private delegate string StringHandler(StringOrig orig);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]private delegate IntPtr NString(IntPtr mi);
    private delegate int UnaryOrig(int value);
    private delegate int UnaryHandler(UnaryOrig orig,int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NContext(int value,IntPtr mi);
    private delegate int AddOrig(int a,int b);
    private delegate int AddHandler(AddOrig orig,int a,int b);
    private delegate int OtherOrig(int a,int b);
    private delegate int OtherHandler(OtherOrig orig,int a,int b);
    private delegate void BumpOrig(ref int value);
    private delegate void BumpHandler(BumpOrig orig,ref int value);
    private delegate void MakeOrig(out int value);
    private delegate void MakeHandler(MakeOrig orig,out int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NAdd(int a,int b,IntPtr mi);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void NBump(ref int value,IntPtr mi);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void NMake(out int value,IntPtr mi);
    private sealed class FakeBackend:DetourBackend
    {
        public int Removes;
        public bool FailApply;
        public bool Safe=true;
        public override bool SupportsSafeRemoval=>Safe;
        public override bool IsAvailable=>true;
        public override int Apply(IntPtr target,IntPtr detour,out IntPtr trampoline){trampoline=FailApply?IntPtr.Zero:target;return FailApply?-5:0;}
        public override int Remove(IntPtr target){Removes++;return 0;}
    }
}
