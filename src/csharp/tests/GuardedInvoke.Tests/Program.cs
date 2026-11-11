using System.Reflection;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;

internal static unsafe class Program
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static int checks;
    private static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        checks++;
        Console.WriteLine("PASS " + name);
    }

    private static void Main()
    {
        Check(Marshal.SizeOf<PylonCallFrame>() == 256, "native frame size");
        Check(Marshal.OffsetOf<PylonCallFrame>(nameof(PylonCallFrame.Out)).ToInt32() == 208, "return frame offset");
        Check(Marshal.OffsetOf<PylonCallFrame>(nameof(PylonCallFrame.Exc)).ToInt32() == 248, "exception frame offset");
        PackTest();
        FallbackTests();
        Console.WriteLine($"ALL {checks} CHECKS PASSED");
    }

    private static void PackTest()
    {
        Type owner = typeof(GuardedInvoke);
        Type planType = owner.GetNestedType("MethodPlan", BindingFlags.NonPublic)!;
        Type opType = owner.GetNestedType("ArgOp", BindingFlags.NonPublic)!;
        object plan = Activator.CreateInstance(planType, true)!;
        planType.GetField("SelfDst")!.SetValue(plan, 0);
        planType.GetField("MethodDst")!.SetValue(plan, 2);
        Array ops = Array.CreateInstance(opType, 5);
        void Op(int i, byte kind, byte dst, byte count, int size)
        {
            object op = Activator.CreateInstance(opType)!;
            opType.GetField("Kind")!.SetValue(op, kind);
            opType.GetField("Dst")!.SetValue(op, dst);
            opType.GetField("Count")!.SetValue(op, count);
            opType.GetField("Size")!.SetValue(op, size);
            ops.SetValue(op, i);
        }
        Op(0, 2, 0, 1, 0); // D0 is NOT X0 (this).
        Op(1, 4, 1, 3, 12); // s1,s2,s3; D2 is NOT X2 (MethodInfo).
        Op(2, 1, 1, 0, 4);
        Op(3, 5, 8, 0, 9); // partial final word must remain zero-padded.
        Op(4, 4, 4, 2, 16);
        planType.GetField("Args")!.SetValue(plan, ops);
        float scalar = 1.25f;
        float* hfa = stackalloc float[] { 2.5f, -3.5f, 4.5f };
        int integer = 12345;
        byte* small = stackalloc byte[16];
        for (int i = 0; i < 16; i++) small[i] = i < 9 ? (byte)(i + 1) : (byte)0xEE;
        double* doubles = stackalloc double[] { -1.5, 9.25 };
        IntPtr* args = stackalloc IntPtr[] { (IntPtr)(&scalar), (IntPtr)hfa, (IntPtr)(&integer), (IntPtr)small, (IntPtr)doubles };
        object[] call = { new PylonCallFrame(), plan, (IntPtr)0x5678, (IntPtr)0x1234, (IntPtr)args };
        owner.GetMethod("PackArguments", PrivateStatic)!.Invoke(null, call);
        PylonCallFrame frame = (PylonCallFrame)call[0];
        Check(frame.X[0] == 0x1234 && frame.X[1] == 12345, "self and integer argument order");
        Check(frame.D[0] == BitConverter.SingleToUInt32Bits(scalar), "D0 survives X0 collision");
        Check(frame.D[1] == BitConverter.SingleToUInt32Bits(hfa[0]) &&
              frame.D[2] == BitConverter.SingleToUInt32Bits(hfa[1]) &&
              frame.D[3] == BitConverter.SingleToUInt32Bits(hfa[2]), "float HFA occupies individual SIMD registers");
        Check(frame.D[4] == BitConverter.DoubleToUInt64Bits(doubles[0]) &&
              frame.D[5] == BitConverter.DoubleToUInt64Bits(doubles[1]), "double HFA registers");
        Check(frame.X[9] == 9, "partial struct word does not over-read");
        var packer = (Delegate?)owner.GetMethod("CreatePacker", PrivateStatic)!.Invoke(null, [plan]);
        Check(packer != null, "specialized packer created");
        PylonCallFrame specialized = default;
        packer!.DynamicInvoke(Pointer.Box(&specialized, typeof(PylonCallFrame*)), (IntPtr)0x5678, (IntPtr)0x1234, (IntPtr)args);
        Check(new ReadOnlySpan<byte>(&specialized, sizeof(PylonCallFrame)).SequenceEqual(
            new ReadOnlySpan<byte>(&frame, sizeof(PylonCallFrame))), "specialized packer matches all frame bytes");
    }

    private static void FallbackTests()
    {
        IntPtr box = Marshal.AllocHGlobal(2048);
        try
        {
            new Span<byte>((void*)box, 2048).Fill(0xA5);
            IL2CPP.Returned = box;
            IntPtr ret = IntPtr.Zero, exc = (IntPtr)99;
            // InternalCall must use runtime_invoke even on ARM64, never the native executor.
            int methodId = 10;
            foreach (var (type, size) in new[] { (0x02, 1), (0x03, 2), (0x08, 4), (0x0A, 8), (0x0C, 4), (0x0D, 8), (0x18, 8), (0x11, 1024) })
            {
                IL2CPP.ReturnType = type;
                IL2CPP.ValueSize = size;
                int calls = IL2CPP.UnboxCalls;
                GuardedInvoke.Invoke((IntPtr)methodId++, IntPtr.Zero, IntPtr.Zero, 0, ref ret, ref exc);
                Check(exc == IntPtr.Zero && IL2CPP.UnboxCalls == calls + 1 &&
                      new ReadOnlySpan<byte>((void*)ret, size).SequenceEqual(new ReadOnlySpan<byte>((void*)(box + 16), size)),
                      $"fallback unboxes type {type:X} size {size}");
            }
            IL2CPP.ReturnType = 0x15;
            IL2CPP.IsValue = false;
            int previous = IL2CPP.UnboxCalls;
            GuardedInvoke.Invoke((IntPtr)1, IntPtr.Zero, IntPtr.Zero, 0, ref ret, ref exc);
            Check(*(IntPtr*)ret == box && IL2CPP.UnboxCalls == previous, "generic reference stays a reference");
            IntPtr outer = ret, inner = IntPtr.Zero;
            IL2CPP.Callback = () =>
            {
                IntPtr nestedExc = IntPtr.Zero;
                GuardedInvoke.Invoke((IntPtr)1, IntPtr.Zero, IntPtr.Zero, 0, ref inner, ref nestedExc);
            };
            GuardedInvoke.Invoke((IntPtr)1, IntPtr.Zero, IntPtr.Zero, 0, ref ret, ref exc);
            Check(inner != ret && outer == ret, "nested fallback gets a distinct buffer");
            int metadataCalls = IL2CPP.ParamCountCalls;
            GuardedInvoke.Invoke((IntPtr)1, IntPtr.Zero, IntPtr.Zero, 0, ref ret, ref exc);
            Check(IL2CPP.ParamCountCalls == metadataCalls, "cached calls do not query argument metadata");
            bool countRejected = false;
            IntPtr* wrongArgs = stackalloc IntPtr[1];
            try { GuardedInvoke.Invoke((IntPtr)1, IntPtr.Zero, (IntPtr)wrongArgs, 1, ref ret, ref exc); }
            catch (ArgumentException) { countRejected = true; }
            Check(countRejected, "cached argument count still rejects mismatches");
            IL2CPP.Exception = (IntPtr)0x789;
            GuardedInvoke.Invoke((IntPtr)1, IntPtr.Zero, IntPtr.Zero, 0, ref ret, ref exc);
            Check(ret == IntPtr.Zero && exc == IL2CPP.Exception, "exception clears stale return");
            bool rejected = false;
            try { GuardedInvoke.Invoke((IntPtr)1, IntPtr.Zero, IntPtr.Zero, 1, ref ret, ref exc); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid argument array rejected");
        }
        finally { Marshal.FreeHGlobal(box); }
    }
}
