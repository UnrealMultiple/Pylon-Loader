using System.Reflection;
using Il2CppInterop.Runtime;

namespace PerfTest;

// Explicit opt-in only. Math.Sign(NaN) throws without mutating game state.
internal static unsafe class SafetySuite
{
    internal static void Run(Action<string> add)
    {
        add($"PerfTest SAFETY {DateTimeOffset.Now:O}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        var resolve = typeof(GuardedInvoke).GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Resolve unavailable; cannot prove DIRECT route");
        IntPtr klass = IL2CPP.GetIl2CppClass("mscorlib.dll", "System", "Math");
        if (klass == IntPtr.Zero) throw new NotSupportedException("IL2CPP System.Math unavailable");
        IntPtr iterator = IntPtr.Zero, method = IntPtr.Zero;
        for (IntPtr candidate; (candidate = IL2CPP.il2cpp_class_get_methods(klass, ref iterator)) != IntPtr.Zero;)
        {
            if (IL2CPP.il2cpp_method_get_name_(candidate) != "Sign" ||
                IL2CPP.il2cpp_method_get_param_count(candidate) != 1 ||
                IL2CPP.il2cpp_method_is_instance(candidate) || IL2CPP.il2cpp_method_is_generic(candidate) ||
                IL2CPP.il2cpp_type_get_name_(IL2CPP.il2cpp_method_get_param(candidate, 0)) != "System.Double") continue;
            if (method != IntPtr.Zero) throw new InvalidOperationException("Ambiguous Math.Sign(double)");
            method = candidate;
        }
        if (method == IntPtr.Zero || resolve.Invoke(null, [method]) == null)
            throw new NotSupportedException("Math.Sign(double) is not DIRECT; unwind test NOT performed");

        double value = double.NaN;
        IntPtr* args = stackalloc IntPtr[1];
        args[0] = (IntPtr)(&value);
        IntPtr exception = IntPtr.Zero;
        // Establish the throw precondition and initialize the class via the reference path.
        IL2CPP.il2cpp_runtime_invoke(method, IntPtr.Zero, (void**)args, ref exception);
        if (exception == IntPtr.Zero) throw new InvalidOperationException("Reference Math.Sign(NaN) did not throw");
        using var clamp = BenchCase.Create()[0];
        clamp.Prepare(resolve);
        if (!clamp.Route.StartsWith("DIRECT", StringComparison.Ordinal))
            throw new NotSupportedException("Recovery Clamp is not DIRECT");
        add($"DIRECT Math.Sign(double.NaN) MethodInfo=0x{method:X}; reference exception confirmed");
        for (int i = 0; i < 64; i++)
        {
            value = double.NaN;
            IntPtr result = IntPtr.Zero;
            exception = IntPtr.Zero;
            GuardedInvoke.Invoke(method, IntPtr.Zero, (IntPtr)args, 1, ref result, ref exception);
            if (exception == IntPtr.Zero) throw new InvalidOperationException($"Unwind iteration {i}: missing frame.Exc");
            // Exercise the same target successfully after unwinding, then a different DIRECT plan.
            value = -42;
            exception = IntPtr.Zero;
            GuardedInvoke.Invoke(method, IntPtr.Zero, (IntPtr)args, 1, ref result, ref exception);
            if (exception != IntPtr.Zero || result == IntPtr.Zero || *(int*)result != -1)
                throw new InvalidOperationException($"Unwind iteration {i}: same-target recovery failed");
            clamp.Check(true);
        }
        add("PASS: 64 DIRECT throws returned frame.Exc; each followed by DIRECT Sign(-42)=-1 and Clamp=5");
        add("PASS: repeated exception unwind + ordinary direct-call recovery (not full ABI coverage)");
    }
}
