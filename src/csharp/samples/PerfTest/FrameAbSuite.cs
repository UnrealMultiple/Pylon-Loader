using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Il2CppInterop.Runtime;
using Il2CppMicrosoft.Xna.Framework;

namespace PerfTest;

internal static class FrameAbSuite
{
    private static float _sink;
    internal static void Run(Action<string> add, bool cacheOnly = false)
    {
        if (!GuardedInvoke.FrameDiagnosticsAvailable)
            throw new NotSupportedException("A/B unavailable: production runtime has no route switches; build runtime with PylonFrameDiagnostics=true");
        const int n = 20000;
        // Require the NEW generated entry, not just an enabled runtime switch.
        foreach (var (type, name, count) in new[] { (typeof(MathHelper), "Clamp", 3), (typeof(Vector2), "Distance", 2), (typeof(Matrix), "Add", 2) })
        {
            var method = type.GetMethods().Single(m => m.Name == name && m.GetParameters().Length == count &&
                m.GetParameters().All(p => !p.ParameterType.IsByRef) && (name != "Clamp" || m.ReturnType == typeof(float)));
            bool pilot = false;
            byte[] il = method.GetMethodBody()!.GetILAsByteArray()!;
            for (int i = 0; i + 4 < il.Length; i++)
                if (il[i] == 0x28)
                    try { pilot |= method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1))?.Name is "TryInvokeFrame" or "TryInvokeFrameCached"; } catch { }
            if (!pilot) throw new InvalidOperationException("No generated frame pilot: " + type.Name);
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(type.TypeHandle);
            var pointerField = type.GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
                .Single(f => f.Name.StartsWith("NativeMethodInfoPtr_" + name + "_", StringComparison.Ordinal) &&
                    (name != "Clamp" || f.Name.Contains("Single_Single_Single_Single", StringComparison.Ordinal)) &&
                    (name != "Add" || !f.Name.Contains("Void", StringComparison.Ordinal)) &&
                    (name != "Distance" || !f.Name.Contains("Void", StringComparison.Ordinal)));
            IntPtr pointer = (IntPtr)pointerField.GetValue(null)!;
            object cached = typeof(GuardedInvoke).GetMethod("GetMethod", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [pointer])!;
            int shape = (int)cached.GetType().GetField("FrameShape")!.GetValue(cached)!;
            int expected = name == "Clamp" ? 1 : name == "Distance" ? 2 : 3;
            if (shape != expected) throw new InvalidOperationException("Runtime rejected frame layout: " + type.Name);
            add($"CERTIFIED {type.Name}.{name} shape={shape}; generated entry + runtime ABI matched");
        }
        bool previous = GuardedInvoke.FramePilotEnabled, previousCache = GuardedInvoke.FramePlanCacheEnabled;
        if (cacheOnly) add("MODE cache-only: FRAME=CACHED, COMPAT=TLS (both generated frame, NOT legacy Invoke)");
        add($"FRAME A/B {DateTimeOffset.Now:O}; N={n}; 5 alternating pairs; same process/proxies; ns/op includes loop; no baseline subtraction");
        try
        {
            var a = new Vector2(1, 2); var b = new Vector2(4, 6);
            var ma = Matrix.CreateScale(2f); var mb = Matrix.CreateScale(3f);
            foreach (var (name, loop) in new (string, Action)[] {
                ("Clamp", () => { float v=0; for(int i=0;i<n;i++) v= MathHelper.Clamp(12f,0f,10f); _sink=v; }),
                ("Distance", () => { float v=0; for(int i=0;i<n;i++) v=Vector2.Distance(a,b); _sink=v; }),
                ("Matrix", () => { Matrix v=default; for(int i=0;i<n;i++) v=Matrix.Add(ma,mb); _sink=v.M11; }) })
            {
                var samples = new List<double>[] { [], [] };
                for (int pair=0; pair<5; pair++)
                for (int side=0; side<2; side++)
                {
                    int mode=(pair+side)&1;
                    GuardedInvoke.FramePilotEnabled=cacheOnly || mode==1;
                    if (cacheOnly) GuardedInvoke.FramePlanCacheEnabled=mode==1;
                    if (MathHelper.Clamp(12f,0f,10f)!=10 || Vector2.Distance(a,b)!=5 || Matrix.Add(ma,mb).M11!=5)
                        throw new InvalidOperationException("A/B correctness mismatch");
                    loop(); // warm the exact typed loop for each side
                    long start=Stopwatch.GetTimestamp(); loop(); long elapsed=Stopwatch.GetTimestamp()-start;
                    double ns=elapsed*(1e9/Stopwatch.Frequency)/n;
                    samples[mode].Add(ns);
                    add(string.Create(CultureInfo.InvariantCulture,$"SAMPLE {name} pair={pair} {(mode==1?"FRAME":"COMPAT")} {ns:F2} ns/op"));
                }
                double Median(List<double> x) { x.Sort(); return x[x.Count/2]; }
                double old=Median(samples[0]), fast=Median(samples[1]);
                add(string.Create(CultureInfo.InvariantCulture,$"RESULT {name}: COMPAT={old:F2} FRAME={fast:F2} ns/op; speedup={old/fast:F3}x; CHECK PASS"));
            }
            add("DONE: frame A/B numerical checks passed; sink="+_sink);
        }
        finally { GuardedInvoke.FramePilotEnabled=previous; GuardedInvoke.FramePlanCacheEnabled=previousCache; }
    }
}
