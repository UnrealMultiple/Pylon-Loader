// 桌面回归探针：验证 Il2CppInterop.RuntimeDetour 的桩生成与调用约定。
//
// 它模拟 il2cpp 的两条边：
//   原生 → 托管：把入口桩当原生函数指针调（含隐藏尾参 MethodInfo*）
//   托管 → 原生：orig 桩用 calli 调"原函数"，原函数也是带尾参的原生函数指针
// 覆盖：int / 值类型（按值传参与返回）/ ref / out / bool（1 字节）/ 链式叠加 / 异常兜底。
// --bench 额外跑一次每次调用的开销（不含 il2cpp，只看引擎）。
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Il2CppInterop.RuntimeDetour;

[StructLayout(LayoutKind.Sequential)]
struct Vec2
{
    public float X;
    public float Y;
    public Vec2(float x, float y) { X = x; Y = y; }
    public override string ToString() => "(" + X + ", " + Y + ")";
}

static class Targets
{
    public static int Add(int a, int b) => a + b;
    public static Vec2 Flip(Vec2 v) => new Vec2(v.Y, v.X);
    public static void Bump(ref int x) => x += 2;
    public static void Make(out int x) => x = 7;
    public static bool Toggle(bool b) => !b;
    public static string Name() => "probe";
}

delegate int AddOrig(int a, int b);
delegate int AddHook(AddOrig orig, int a, int b);

delegate Vec2 FlipOrig(Vec2 v);
delegate Vec2 FlipHook(FlipOrig orig, Vec2 v);

delegate void BumpOrig(ref int x);
delegate void BumpHook(BumpOrig orig, ref int x);

delegate void MakeOrig(out int x);
delegate void MakeHook(MakeOrig orig, out int x);

delegate bool ToggleOrig(bool b);
delegate bool ToggleHook(ToggleOrig orig, bool b);

delegate string NameOrig();
delegate string NameHook(NameOrig orig);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NAdd(int a, int b, IntPtr methodInfo);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate Vec2 NFlip(Vec2 v, IntPtr methodInfo);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void NBump(ref int x, IntPtr methodInfo);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void NMake(out int x, IntPtr methodInfo);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate byte NToggle(byte b, IntPtr methodInfo);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr NStr(IntPtr methodInfo);

sealed class ProbeBackend : DetourBackend
{
    public override bool IsAvailable => true;

    // 假跳板 = 原函数地址（桌面探针没有真正的 detour）
    public override int Apply(IntPtr target, IntPtr detour, out IntPtr trampoline)
    {
        trampoline = target;
        return 0;
    }

    public override int Remove(IntPtr target) => 0;
}

static class Probe
{
    private static readonly List<object> Kept = new();
    private static int _failures;

    private static void Check(string name, bool ok, object actual, object expected)
    {
        if (!ok) _failures++;
        Console.WriteLine((ok ? "OK   " : "FAIL ") + name + " = " + actual + "（期望 " + expected + "）");
    }

    private static Hook HookIt(System.Reflection.MethodInfo target, Delegate handler, Delegate nativeOriginal)
    {
        var originalPointer = Marshal.GetFunctionPointerForDelegate(nativeOriginal);
        Kept.Add(nativeOriginal);
        HookPlan.PointersOverride = _ => (IntPtr.Zero, originalPointer);
        return new Hook(target, handler);
    }

    // 用 unmanaged 函数指针直接调入口桩：走的是真正的原生 ABI（x64 SysV）。
    private static unsafe int CallAdd(Hook hook, int a, int b)
        => ((delegate* unmanaged[Cdecl]<int, int, IntPtr, int>)hook.EntryForTest())(a, b, IntPtr.Zero);

    private static unsafe Vec2 CallFlip(Hook hook, Vec2 v)
        => ((delegate* unmanaged[Cdecl]<Vec2, IntPtr, Vec2>)hook.EntryForTest())(v, IntPtr.Zero);

    private static unsafe void CallBump(Hook hook, ref int value)
        => ((delegate* unmanaged[Cdecl]<int*, IntPtr, void>)hook.EntryForTest())((int*)Unsafe.AsPointer(ref value), IntPtr.Zero);

    private static unsafe int CallMake(Hook hook)
    {
        int value;
        ((delegate* unmanaged[Cdecl]<int*, IntPtr, void>)hook.EntryForTest())(&value, IntPtr.Zero);
        return value;
    }

    private static unsafe byte CallToggle(Hook hook, byte b)
        => ((delegate* unmanaged[Cdecl]<byte, IntPtr, byte>)hook.EntryForTest())(b, IntPtr.Zero);

    private static void Main()
    {
        HookLog.Info = message => Console.WriteLine("[hook] " + message);
        HookLog.Error = (label, e) => Console.WriteLine("[hook:err] " + label + ": " + e.GetType().Name + ": " + e.Message);
        DetourBackend.Current = new ProbeBackend();

        // ① int，orig 正常调用
        var add = HookIt(typeof(Targets).GetMethod(nameof(Targets.Add)),
            new AddHook((orig, a, b) => orig(a, b) + 1),
            new NAdd((a, b, mi) => a + b));
        Check("entry.Add(10, 20)", CallAdd(add, 10, 20) == 31, CallAdd(add, 10, 20), 31);
        add.Dispose();

        // ② 结构体按值传参 + 结构体返回
        var flip = HookIt(typeof(Targets).GetMethod(nameof(Targets.Flip)),
            new FlipHook((orig, v) => { var r = orig(v); return new Vec2(r.X + 1, r.Y); }),
            new NFlip((v, mi) => new Vec2(v.Y, v.X)));
        var flipped = CallFlip(flip, new Vec2(1, 2));
        Check("entry.Flip(1,2)", flipped.X == 3 && flipped.Y == 1, flipped, "(3, 1)");
        flip.Dispose();

        // ③ ref int：handler 改 + orig 改，都写回原生变量
        var bump = HookIt(typeof(Targets).GetMethod(nameof(Targets.Bump)),
            new BumpHook((BumpOrig orig, ref int x) => { x += 10; orig(ref x); }),
            new NBump((ref int x, IntPtr mi) => x += 2));
        var value = 5;
        CallBump(bump, ref value);
        Check("entry.Bump(ref 5)", value == 17, value, 17);
        bump.Dispose();

        // ④ out int
        var make = HookIt(typeof(Targets).GetMethod(nameof(Targets.Make)),
            new MakeHook((MakeOrig orig, out int x) => { orig(out x); x += 1; }),
            new NMake((out int x, IntPtr mi) => x = 7));
        var made = CallMake(make);
        Check("entry.Make(out)", made == 8, made, 8);
        make.Dispose();

        // ⑤ bool：il2cpp 是 1 字节，托管是 4 字节
        var toggle = HookIt(typeof(Targets).GetMethod(nameof(Targets.Toggle)),
            new ToggleHook((orig, b) => !orig(b)),
            new NToggle((b, mi) => (byte)(b == 0 ? 1 : 0)));
        Check("entry.Toggle(1)", CallToggle(toggle, 1) == 1, CallToggle(toggle, 1), (byte)1);
        toggle.Dispose();

        // ⑥ 链式：两条 hook 叠加，先挂的先跑（同一个原函数地址 = 同一条链）
        var sharedOriginal = new NAdd((a, b, mi) => a + b);
        Kept.Add(sharedOriginal);
        var sharedPointer = Marshal.GetFunctionPointerForDelegate(sharedOriginal);
        HookPlan.PointersOverride = _ => (IntPtr.Zero, sharedPointer);
        var add1 = new Hook(typeof(Targets).GetMethod(nameof(Targets.Add)),
            new AddHook((orig, a, b) => orig(a, b) * 10));
        var add2 = new Hook(typeof(Targets).GetMethod(nameof(Targets.Add)),
            new AddHook((orig, a, b) => orig(a, b) + 100));
        // handler1 在外层（先跑），它的 orig 调用到 handler2，所以 (2+3+100)*10 = 1050
        Check("entry.Add chain(2,3)", CallAdd(add1, 2, 3) == 1050, CallAdd(add1, 2, 3), 1050);
        add2.Dispose();
        Check("entry.Add chain after undo", CallAdd(add1, 2, 3) == 50, CallAdd(add1, 2, 3), 50);
        add1.Dispose();



        // ⑦ string 返回/参数：注入假转换后完整走入口桩 + orig 桩，dump IL
        ReRunString();

        // ⑧ handler 抛异常：记一条 + 返回默认值，不把游戏线程带走
        var boom = HookIt(typeof(Targets).GetMethod(nameof(Targets.Add)),
            new AddHook((orig, a, b) => throw new InvalidOperationException("boom")),
            new NAdd((a, b, mi) => a + b));
        Check("entry.Add(boom)", CallAdd(boom, 1, 2) == 0, CallAdd(boom, 1, 2), 0);
        boom.Dispose();

        if (Array.IndexOf(Environment.GetCommandLineArgs(), "--bench") >= 0) Bench();

        Console.WriteLine(_failures == 0 ? "\n全部通过。" : "\n失败 " + _failures + " 项。");
        Environment.Exit(_failures == 0 ? 0 : 1);
    }

    private static unsafe void ReRunString()
    {
        // 注入假转换：真机走 IL2CPP，桌面用可辨识的指针
        HookStub.Il2CppToManaged = ptr => ptr == IntPtr.Zero ? null : "s:" + ptr.ToInt64();
        HookStub.ManagedToIl2Cpp = str => new IntPtr(str != null ? str.GetHashCode() : 0);

        // string 返回的 hook：原生函数返回 IntPtr（模拟 il2cpp string），
        // 走完整 BuildEntry/BuildOrig + calli；桌面注入转换后应能 JIT 并执行。
        var nativeOriginal = new NStr((mi) => new IntPtr(0x5155));
        Kept.Add(nativeOriginal);
        var originalPointer = Marshal.GetFunctionPointerForDelegate(nativeOriginal);
        HookPlan.PointersOverride = _ => (IntPtr.Zero, originalPointer);
        var hook = new Hook(typeof(Targets).GetMethod(nameof(Targets.Name)), new NameHook((orig) => orig() + "!"));
        var e = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr>)hook.EntryForTest();
        var result = e(IntPtr.Zero);
        Check("entry.Name string 返回走通", result != IntPtr.Zero, result, "非零");
        // dump orig 桩 IL，定位 string 转换的调用序
        try
        {
            var orig = (System.Delegate)hook.Original;
            Console.WriteLine("orig={0}", orig.Method.Module.Name);
        }
        catch (Exception ex) { Console.WriteLine("orig dump fail: " + ex.Message); }
        hook.Dispose();
        Console.WriteLine("[probe] string 转换注入后 入口桩可 JIT 并执行");
    }

    private static unsafe void Bench()
    {
        // 直接调原函数指针 vs 走 hook 入口桩，差值就是引擎的开销（不含 il2cpp）。
        var nativeOriginal = new NAdd((a, b, mi) => a + b);
        Kept.Add(nativeOriginal);
        var originalPointer = Marshal.GetFunctionPointerForDelegate(nativeOriginal);
        HookPlan.PointersOverride = _ => (IntPtr.Zero, originalPointer);
        var hook = new Hook(typeof(Targets).GetMethod(nameof(Targets.Add)),
            new AddHook((orig, a, b) => orig(a, b) + 1));

        var direct = (delegate* unmanaged[Cdecl]<int, int, IntPtr, int>)originalPointer;
        var entry = (delegate* unmanaged[Cdecl]<int, int, IntPtr, int>)hook.EntryForTest();

        const int warm = 200_000;
        const int iterations = 20_000_000;
        for (var i = 0; i < warm; i++) { direct(1, 2, IntPtr.Zero); entry(1, 2, IntPtr.Zero); }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var sum = 0;
        for (var i = 0; i < iterations; i++) sum += direct(1, 2, IntPtr.Zero);
        sw.Stop();
        var baseline = sw.Elapsed.TotalMilliseconds;

        sw.Restart();
        for (var i = 0; i < iterations; i++) sum += entry(1, 2, IntPtr.Zero);
        sw.Stop();
        var hooked = sw.Elapsed.TotalMilliseconds;

        Console.WriteLine($"\n[bench] 直接调原函数  {baseline / iterations * 1e6:F1} ns/call");
        Console.WriteLine($"[bench] 走 hook 入口   {hooked / iterations * 1e6:F1} ns/call");
        Console.WriteLine($"[bench] 引擎增量       {(hooked - baseline) / iterations * 1e6:F1} ns/call（sum={sum}）");
        hook.Dispose();
    }
}