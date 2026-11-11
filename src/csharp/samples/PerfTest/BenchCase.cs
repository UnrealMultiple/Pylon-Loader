using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;

namespace PerfTest;

internal sealed unsafe class BenchCase : IDisposable
{
    internal readonly string Label, Assembly, Namespace, Class, Method;
    internal readonly string[] Parameters;
    internal readonly byte[][] Values;
    internal readonly byte[] Expected;
    internal readonly bool FloatResult;
    internal IntPtr Info, Args;
    private readonly List<IntPtr> _cells = [];
    internal string Route = "unknown";

    internal BenchCase(string label, string assembly, string ns, string klass, string method,
        string[] parameters, byte[][] values, byte[] expected, bool floats)
    {
        Label = label; Assembly = assembly; Namespace = ns; Class = klass; Method = method;
        Parameters = parameters; Values = values; Expected = expected; FloatResult = floats;
    }

    internal static byte[] I(int value) => BitConverter.GetBytes(value);
    internal static byte[] F(params float[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();
    internal static byte[] D(double value) => BitConverter.GetBytes(value);
    internal static List<BenchCase> Create() =>
    [
        new("int -> int", "UnityEngine.CoreModule.dll", "UnityEngine", "Mathf", "Clamp",
            ["System.Int32", "System.Int32", "System.Int32"], [I(7), I(1), I(5)], I(5), false),
        new("float -> float", "UnityEngine.CoreModule.dll", "UnityEngine", "Mathf", "Lerp",
            ["System.Single", "System.Single", "System.Single"], [F(2), F(6), F(.25f)], F(3), true),
        new("HFA2 -> HFA2", "Assembly-CSharp.dll", "Microsoft.Xna.Framework", "Vector2", "Lerp",
            ["Microsoft.Xna.Framework.Vector2", "Microsoft.Xna.Framework.Vector2", "System.Single"],
            [F(2, 4), F(6, 8), F(.25f)], F(3, 5), true),
        new("HFA3 -> HFA3", "Assembly-CSharp.dll", "Microsoft.Xna.Framework", "Vector3", "Lerp",
            ["Microsoft.Xna.Framework.Vector3", "Microsoft.Xna.Framework.Vector3", "System.Single"],
            [F(2, 4, 6), F(6, 8, 10), F(.25f)], F(3, 5, 7), true),
        new("HFA4 -> HFA4", "Assembly-CSharp.dll", "Microsoft.Xna.Framework", "Vector4", "Add",
            ["Microsoft.Xna.Framework.Vector4", "Microsoft.Xna.Framework.Vector4"],
            [F(2, 4, 6, 8), F(6, 8, 10, 12)], F(8, 12, 16, 20), true),
        new("double -> double", "Assembly-CSharp.dll", "Terraria", "Utils", "Lerp",
            ["System.Double", "System.Double", "System.Double"], [D(2), D(6), D(.25)], D(3), false)
    ];

    internal void Prepare(MethodInfo? resolve)
    {
        IntPtr klass = IL2CPP.GetIl2CppClass(Assembly, Namespace, Class);
        if (klass == IntPtr.Zero) throw new NotSupportedException("class unavailable");
        IntPtr iterator = IntPtr.Zero;
        while (true)
        {
            IntPtr method = IL2CPP.il2cpp_class_get_methods(klass, ref iterator);
            if (method == IntPtr.Zero) break;
            if (IL2CPP.il2cpp_method_get_name_(method) != Method ||
                IL2CPP.il2cpp_method_get_param_count(method) != Parameters.Length ||
                IL2CPP.il2cpp_method_is_instance(method) || IL2CPP.il2cpp_method_is_generic(method)) continue;
            bool match = true;
            for (uint i = 0; i < Parameters.Length; i++)
                if (IL2CPP.il2cpp_type_get_name_(IL2CPP.il2cpp_method_get_param(method, i)) != Parameters[i]) match = false;
            if (!match) continue;
            if (Info != IntPtr.Zero) throw new InvalidOperationException("ambiguous method metadata");
            Info = method;
        }
        if (Info == IntPtr.Zero) throw new NotSupportedException("signature unavailable");
        IntPtr returnType = IL2CPP.il2cpp_method_get_return_type(Info);
        IntPtr returnClass = IL2CPP.il2cpp_class_from_type(returnType);
        uint alignment = 0;
        if (returnClass == IntPtr.Zero || !IL2CPP.il2cpp_class_is_valuetype(returnClass) ||
            IL2CPP.il2cpp_class_value_size(returnClass, ref alignment) != Expected.Length)
            throw new NotSupportedException("unexpected return layout; refusing unsafe read");
        // Reflection ONLY during setup; the timed path calls the real protected API directly.
        Route = resolve == null ? "UNKNOWN (Resolve missing)" :
            resolve.Invoke(null, [Info]) == null ? "FALLBACK (not direct)" : "DIRECT (Resolve plan)";
        Args = Marshal.AllocHGlobal(Parameters.Length * IntPtr.Size);
        for (int i = 0; i < Values.Length; i++)
        {
            IntPtr argType = IL2CPP.il2cpp_method_get_param(Info, (uint)i);
            IntPtr argClass = IL2CPP.il2cpp_class_from_type(argType);
            alignment = 0;
            if (IL2CPP.il2cpp_type_is_byref(argType) || argClass == IntPtr.Zero ||
                !IL2CPP.il2cpp_class_is_valuetype(argClass) ||
                IL2CPP.il2cpp_class_value_size(argClass, ref alignment) != Values[i].Length)
                throw new NotSupportedException("unexpected argument layout; refusing unsafe call");
            IntPtr cell = Marshal.AllocHGlobal(Values[i].Length);
            _cells.Add(cell);
            Marshal.Copy(Values[i], 0, cell, Values[i].Length);
            ((IntPtr*)Args)[i] = cell;
        }
        Check(true);
        Check(false);
    }

    internal IntPtr Invoke(bool guarded)
    {
        IntPtr exception = IntPtr.Zero, result = IntPtr.Zero;
        if (guarded)
            GuardedInvoke.Invoke(Info, IntPtr.Zero, Args, Parameters.Length, ref result, ref exception);
        else
        {
            IntPtr boxed = IL2CPP.il2cpp_runtime_invoke(Info, IntPtr.Zero, (void**)Args, ref exception);
            if (exception == IntPtr.Zero && boxed != IntPtr.Zero) result = IL2CPP.il2cpp_object_unbox(boxed);
        }
        if (exception != IntPtr.Zero) throw new InvalidOperationException($"IL2CPP exception 0x{exception:X}");
        if (result == IntPtr.Zero) throw new InvalidOperationException("null result");
        return result;
    }

    internal void Check(bool guarded)
    {
        IntPtr result = Invoke(guarded);
        var actual = new ReadOnlySpan<byte>((void*)result, Expected.Length);
        bool ok = actual.SequenceEqual(Expected);
        if (FloatResult)
        {
            ok = true;
            for (int i = 0; i < Expected.Length; i += 4)
                if (MathF.Abs(BitConverter.ToSingle(actual.Slice(i, 4)) - BitConverter.ToSingle(Expected, i)) > .00001f ||
                    !float.IsFinite(BitConverter.ToSingle(actual.Slice(i, 4)))) ok = false;
        }
        if (!ok) throw new InvalidOperationException($"{(guarded ? "GuardedInvoke" : "runtime_invoke")} correctness mismatch");
    }

    public void Dispose()
    {
        foreach (IntPtr cell in _cells) Marshal.FreeHGlobal(cell);
        _cells.Clear();
        if (Args != IntPtr.Zero) Marshal.FreeHGlobal(Args);
        Args = IntPtr.Zero;
    }
}
