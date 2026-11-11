using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using Il2CppInterop.RuntimeDetour;

internal static class BindingTests
{
    public static void Run()
    {
        var previousPointers = HookPlan.PointersOverride;
        try
        {
            HookPlan.PointersOverride = _ => (new IntPtr(1), new IntPtr(2));
            ValidateDelegates();
            ExactAndCompactBindings();
            GenericBindings();
            FailedUndoRemainsRetryable();
            DelegateCacheUsesTypeIdentity();
        }
        finally { HookPlan.PointersOverride = previousPointers; }
        Console.WriteLine("PASS BindingTests");
    }

    private static void ValidateDelegates()
    {
        var target = typeof(Targets).GetMethod(nameof(Targets.Add))!;
        var plan = HookPlan.Create(target, new IntHandler((orig, a, b) => orig(a, b)));
        Check(plan.OrigType == typeof(IntOrig), "valid orig type");
        Throws<ArgumentException>(() => HookPlan.Create(target, new WrongReturn((orig, a, b) => "wrong")));
        Throws<ArgumentException>(() => HookPlan.Create(target, new WrongParameters((orig, a, b) => 0)));
        Throws<ArgumentException>(() => HookPlan.Create(target, new WrongOrig((orig, a, b) => 0)));

        // A closed static delegate has one extra argument in Method, but not in Invoke.
        var method = typeof(BindingTests).GetMethod(nameof(BoundHandler), BindingFlags.NonPublic | BindingFlags.Static)!;
        var bound = (IntHandler)Delegate.CreateDelegate(typeof(IntHandler), new object(), method);
        Check(bound.Method.GetParameters().Length == 4, "bound static backing method has extra context");
        HookPlan.Create(target, bound);

        var outTarget = typeof(Targets).GetMethod(nameof(Targets.Make))!;
        HookPlan.Create(outTarget, new OutHandler((OutOrig orig, out int value) => orig(out value)));
        Throws<ArgumentException>(() => HookPlan.Create(outTarget, new RefHandler((RefOrig orig, ref int value) => orig(ref value))));
    }

    private static void ExactAndCompactBindings()
    {
        var endpoint = HookEndpoint.Bind(typeof(Targets), nameof(Targets.Add), new[] { typeof(int), typeof(int) }, typeof(IntOrig), typeof(IntHandler));
        Check(endpoint.Target.Name == nameof(Targets.Add), "exact binding");
        Throws<ArgumentException>(() => HookEndpoint.Bind(typeof(Targets), nameof(Targets.Add), new[] { typeof(int), typeof(int) }, typeof(IntOrig), typeof(WrongReturn)));
        Throws<MissingMethodException>(() => HookEndpoint.Bind(typeof(Targets), nameof(Targets.Add), new[] { typeof(string), typeof(string) }, typeof(IntOrig), typeof(IntHandler)));

        HookEndpoint compact = null!;
        HookEndpoint.BindOnce(ref compact, typeof(Targets), nameof(Targets.Add), 2, typeof(IntOrig), typeof(IntHandler));
        Check(compact.Target == endpoint.Target, "compact unique target");
        HookEndpoint ambiguous = null!;
        Throws<AmbiguousMatchException>(() => HookEndpoint.BindOnce(ref ambiguous, typeof(Targets), nameof(Targets.Overload), 1, typeof(UnaryOrig), typeof(UnaryHandler)));
        Check(ambiguous is null, "failed lazy bind does not publish endpoint");
        HookEndpoint badSignature = null!;
        Throws<ArgumentException>(() => HookEndpoint.BindOnce(ref badSignature, typeof(Targets), nameof(Targets.OnlyString), 1, typeof(UnaryOrig), typeof(UnaryHandler)));
        HookEndpoint genericOnly = null!;
        Throws<MissingMethodException>(() => HookEndpoint.BindOnce(ref genericOnly, typeof(Targets), nameof(Targets.Echo), 1, typeof(UnaryOrig), typeof(UnaryHandler)));

        var ctor = HookEndpoint.Bind(typeof(Constructed), ".ctor", new[] { typeof(int) }, typeof(CtorOrig), typeof(CtorHandler));
        Check(ConstructorTarget.Unwrap(ctor.Target).GetParameters()[0].ParameterType == typeof(int), "ctor uses pure actual parameter list");
        HookEndpoint compactCtor = null!;
        HookEndpoint.BindOnce(ref compactCtor, typeof(Constructed), ".ctor", 1, typeof(CtorOrig), typeof(CtorHandler));
        Throws<MissingMethodException>(() => HookEndpoint.Bind(typeof(Constructed), ".ctor", new[] { typeof(Constructed), typeof(int) }, typeof(CtorOrig), typeof(CtorHandler)));
    }

    private static void GenericBindings()
    {
        var closed = HookEndpoint.BindGeneric(typeof(Targets), nameof(Targets.Echo), new[] { typeof(int) }, Type.EmptyTypes, new[] { typeof(int) }, typeof(UnaryOrig), typeof(UnaryHandler));
        Check(closed.Target.IsGenericMethod && !closed.Target.ContainsGenericParameters, "method generic arguments closed");
        var legacyMethod = HookEndpoint.Bind(typeof(Targets), nameof(Targets.Echo), new[] { typeof(int) }, new[] { typeof(int) }, typeof(UnaryOrig), typeof(UnaryHandler));
        Check(!legacyMethod.Target.ContainsGenericParameters, "legacy method-only arguments");
        var legacyType = HookEndpoint.Bind(typeof(GenericTargets<>), nameof(GenericTargets<int>.TypeEcho), new[] { typeof(int) }, new[] { typeof(int) }, typeof(UnaryOrig), typeof(UnaryHandler));
        Check(legacyType.Target.DeclaringType == typeof(GenericTargets<int>), "legacy declaring-type arguments");
        var both = HookEndpoint.BindGeneric(typeof(GenericTargets<>), nameof(GenericTargets<int>.Combine), new[] { typeof(int), typeof(string) }, new[] { typeof(int) }, new[] { typeof(string) }, typeof(CombineOrig), typeof(CombineHandler));
        Check(both.Target.DeclaringType == typeof(GenericTargets<int>) && both.Target.GetGenericArguments()[0] == typeof(string), "independent type and method arguments");
        HookEndpoint lazy = null!;
        HookEndpoint.BindGenericOnce(ref lazy, typeof(GenericTargets<>), nameof(GenericTargets<int>.Combine), new[] { typeof(int), typeof(string) }, new[] { typeof(int) }, new[] { typeof(string) }, typeof(CombineOrig), typeof(CombineHandler));
        Check(lazy.Target == both.Target, "generic lazy bind");
        Throws<ArgumentException>(() => HookEndpoint.Bind(typeof(GenericTargets<int>), nameof(GenericTargets<int>.Combine), new[] { typeof(int), typeof(string) }, new[] { typeof(string) }, typeof(CombineOrig), typeof(CombineHandler)));
        Throws<MissingMethodException>(() => HookEndpoint.BindGeneric(typeof(Targets), nameof(Targets.Echo), new[] { typeof(string) }, Type.EmptyTypes, new[] { typeof(int) }, typeof(UnaryOrig), typeof(UnaryHandler)));
        Throws<AmbiguousMatchException>(() => HookEndpoint.BindGeneric(typeof(Targets), nameof(Targets.Ambiguous), new[] { typeof(int) }, Type.EmptyTypes, new[] { typeof(int) }, typeof(UnaryOrig), typeof(UnaryHandler)));
        Throws<MissingMethodException>(() => HookEndpoint.BindGeneric(typeof(Targets), nameof(Targets.StructOnly), new[] { typeof(string) }, Type.EmptyTypes, new[] { typeof(string) }, typeof(StringOrig), typeof(StringHandler)));
        HookEndpoint.BindGeneric(typeof(Targets), nameof(Targets.RefEcho), new[] { typeof(int).MakeByRefType() }, Type.EmptyTypes, new[] { typeof(int) }, typeof(RefOrig), typeof(RefHandler));
        HookEndpoint.BindGeneric(typeof(Targets), nameof(Targets.ArrayEcho), new[] { typeof(int[]) }, Type.EmptyTypes, new[] { typeof(int) }, typeof(ArrayOrig), typeof(ArrayHandler));
    }

    private static void FailedUndoRemainsRetryable()
    {
        var previousBackend = DetourBackend.Current;
        var backend = new RetryBackend();
        DetourBackend.Current = backend;
        var endpoint = HookEndpoint.Bind(typeof(Targets), nameof(Targets.Add), new[] { typeof(int), typeof(int) }, typeof(IntOrig), typeof(IntHandler));
        IntHandler handler = (orig, a, b) => orig(a, b);
        try
        {
            endpoint.Add(handler);
            backend.FailRemove = true;
            Throws<InvalidOperationException>(() => endpoint.Remove(handler));
            Check(endpoint.Count == 1, "Remove failure retains subscription");
            backend.FailRemove = false;
            endpoint.Remove(handler);
            Check(endpoint.Count == 0, "Remove retries successfully");
            endpoint.Add(handler);
            backend.FailRemove = true;
            Throws<InvalidOperationException>(() => endpoint.UndoAll());
            Check(endpoint.Count == 1, "UndoAll failure retains subscription");
            backend.FailRemove = false;
            endpoint.UndoAll();
            Check(endpoint.Count == 0, "UndoAll retries successfully");
        }
        finally
        {
            backend.FailRemove = false;
            try { endpoint.UndoAll(); }
            finally { DetourBackend.Current = previousBackend; }
        }
    }

    private sealed class RetryBackend : DetourBackend
    {
        public bool FailRemove;
        public override bool SupportsSafeRemoval => true;
        public override bool IsAvailable => true;
        public override int Apply(IntPtr target, IntPtr detour, out IntPtr trampoline) { trampoline = target; return 0; }
        public override int Remove(IntPtr target) => FailRemove ? -5 : 0;
    }

    private static void DelegateCacheUsesTypeIdentity()
    {
        var first = DefineValue("HookBindingIdentityOne");
        var second = DefineValue("HookBindingIdentityTwo");
        Check(first.FullName == second.FullName && first != second, "fixture has matching names but different identities");
        var parameters = new[] { first };
        var one = NativeDelegateFactory.Create(typeof(void), parameters, CallingConvention.Cdecl);
        parameters[0] = second;
        var two = NativeDelegateFactory.Create(typeof(void), parameters, CallingConvention.Cdecl);
        Check(one != two, "parameter type identity distinguishes cached delegates");
        Check(one == NativeDelegateFactory.Create(typeof(void), new[] { first }, CallingConvention.Cdecl), "cache key owns parameter array snapshot");
        Check(NativeDelegateFactory.Create(first, Type.EmptyTypes, CallingConvention.Cdecl) != NativeDelegateFactory.Create(second, Type.EmptyTypes, CallingConvention.Cdecl), "return type identity distinguishes cached delegates");
    }

    private static Type DefineValue(string assemblyName)
    {
        var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(assemblyName), AssemblyBuilderAccess.Run).DefineDynamicModule(assemblyName);
        var builder = module.DefineType("Fixture.SameName", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, typeof(ValueType));
        builder.DefineField("Value", typeof(int), FieldAttributes.Public);
        return builder.CreateTypeInfo()!.AsType();
    }

    private static int BoundHandler(object context, IntOrig orig, int a, int b) => orig(a, b);
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("BindingTests: " + message); }
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("BindingTests: expected " + typeof(T).Name);
    }

    private delegate int IntOrig(int a, int b);
    private delegate int IntHandler(IntOrig orig, int a, int b);
    private delegate string WrongReturn(IntOrig orig, int a, int b);
    private delegate int WrongParameters(IntOrig orig, string a, string b);
    private delegate int WrongOrig(StringOrig orig, int a, int b);
    private delegate int UnaryOrig(int value);
    private delegate int UnaryHandler(UnaryOrig orig, int value);
    private delegate string StringOrig(string value);
    private delegate string StringHandler(StringOrig orig, string value);
    private delegate void OutOrig(out int value);
    private delegate void OutHandler(OutOrig orig, out int value);
    private delegate void RefOrig(ref int value);
    private delegate void RefHandler(RefOrig orig, ref int value);
    private delegate void CtorOrig(Constructed self, int value);
    private delegate void CtorHandler(CtorOrig orig, Constructed self, int value);
    private delegate string CombineOrig(int first, string second);
    private delegate string CombineHandler(CombineOrig orig, int first, string second);
    private delegate int[] ArrayOrig(int[] values);
    private delegate int[] ArrayHandler(ArrayOrig orig, int[] values);

    private sealed class Constructed { public Constructed() { } public Constructed(int value) { } }
    private static class Targets
    {
        public static int Add(int a, int b) => a + b;
        public static int Overload(int value) => value;
        public static string Overload(string value) => value;
        public static string OnlyString(string value) => value;
        public static T Echo<T>(T value) => value;
        public static T Ambiguous<T>(T value) => value;
        public static int Ambiguous<T>(int value) => value;
        public static T StructOnly<T>(T value) where T : struct => value;
        public static void Make(out int value) => value = 1;
        public static void RefEcho<T>(ref T value) { }
        public static T[] ArrayEcho<T>(T[] values) => values;
    }
    private static class GenericTargets<T>
    {
        public static T TypeEcho(T value) => value;
        public static U Combine<U>(T first, U second) => second;
    }
}
