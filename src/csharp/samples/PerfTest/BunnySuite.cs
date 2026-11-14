using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using Il2CppInterop.Runtime;
using Xna = Il2CppMicrosoft.Xna.Framework;

namespace PerfTest;

// Independently implemented protocol adapter: typed proxy loops.
internal sealed class BunnySuite
{
    private const int Count = 20_000;
    private readonly Action<string> _log;
    private readonly Action<string, string> _save;
    private readonly List<string> _report = [];
    private readonly List<Case> _cases = [];
    private readonly double[] _samples = new double[3], _allocations = new double[3];
    private int _index, _round;
    private long _sink;
    private double _empty;
    private bool _prepared;
    private static Xna.Matrix _matrixA, _matrixB;
    private readonly string _id = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
    internal bool Finished { get; private set; }
    private sealed record Case(string Name, int N, Func<int, long> Loop, Action Check, MethodInfo? Proxy, bool World = false);

    internal BunnySuite(Action<string> log, Action<string, string> save)
    {
        _log = log; _save = save;
        Add($"BUNNY-COMPAT {_id}; .NET {Environment.Version}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        Add("PROTOCOL typed generated proxies; warmup=16; N=20000 (string=5000); median of 3 minus median empty loop. One entire round/frame; no inner-loop slicing.");
        Add("CAUTION each 20000-call round can stall the game thread. CoreCLR B/op excludes IL2CPP boxing. Cross-device results are not a controlled speedup ratio.");
        _cases.Add(new("empty loop", Count, Empty, () => { }, null));
        _cases.Add(new("MathHelper.Clamp(float x3)", Count, Clamp, () => Require(Xna.MathHelper.Clamp(12, 0, 10) == 10, "Clamp expected 10"),
            Find(typeof(Xna.MathHelper), "Clamp", typeof(float), typeof(float), typeof(float))));
        _cases.Add(new("Vector2.Distance", Count, Distance, () => Require(MathF.Abs(Xna.Vector2.Distance(new(1, 2), new(4, 6)) - 5) < .00001f, "Distance expected 5"),
            Find(typeof(Xna.Vector2), "Distance", typeof(Xna.Vector2), typeof(Xna.Vector2))));
        _cases.Add(new("Matrix.Add (64-byte)", Count, Matrix, CheckMatrix,
            Find(typeof(Xna.Matrix), "Add", typeof(Xna.Matrix), typeof(Xna.Matrix))));
        _cases.Add(new("WorldGen.InWorld", Count, InWorld, CheckWorld,
            Find(typeof(Terraria.WorldGen), "InWorld", typeof(int), typeof(int), typeof(int)), true));
        _cases.Add(new("Player.CountBuffs", Count, Buffs, CheckBuffs,
            Find(typeof(Terraria.Player), "CountBuffs"), true));
        _cases.Add(new("Player.Center", Count, Center, CheckCenter,
            typeof(Terraria.Player).GetProperty("Center")?.GetMethod, true));
        _cases.Add(new("Lang.GetItemNameValue(98)", Count / 4, Name, CheckName,
            Find(typeof(Terraria.Lang), "GetItemNameValue", typeof(int)), true));
    }

    internal void Tick()
    {
        if (Finished) return;
        Case c = _cases[_index];
        try
        {
            if (c.World && !WorldReady()) throw new NotSupportedException("requires an active local player/world");
            if (!_prepared)
            {
                c.Check();
                string route;
                try { route = c.Proxy == null ? "BASELINE" : Route(c.Proxy); }
                catch (Exception e) { route = "UNKNOWN (route inspection: " + e.GetBaseException().Message + ")"; }
                Add($"CASE {c.Name}; N={c.N}; {route}; CHECK PASS");
                _sink ^= c.Loop(16);
                _prepared = true;
                return;
            }
            // A single delegate dispatch encloses the complete typed loop, never one per operation.
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            long before = Stopwatch.GetTimestamp();
            _sink ^= c.Loop(c.N);
            long elapsed = Stopwatch.GetTimestamp() - before;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            _samples[_round] = elapsed * (1e9 / Stopwatch.Frequency) / c.N;
            _allocations[_round] = (double)bytes / c.N;
            c.Check();
            if (++_round < 3) return;
            double median = Median(_samples);
            if (_index == 0) _empty = median;
            double net = _index == 0 ? median : Math.Max(0, median - _empty);
            Add(string.Create(CultureInfo.InvariantCulture, $"RESULT {c.Name}: net={net:F2} ns/op; raw-median={median:F2}; empty={_empty:F2}; raw-min={_samples.Min():F2}; CoreCLR B/op={Median(_allocations):F3}; CHECK PASS"));
            Next();
        }
        catch (Exception ex)
        {
            Exception e = ex.GetBaseException();
            Add($"{(e is NotSupportedException ? "SKIP" : "FAIL")} {c.Name}: {e.Message}");
            Next();
        }
    }

    internal void Cancel() { if (!Finished) Complete("CANCELLED: partial run"); }
    private void Next()
    {
        _prepared = false; _round = 0;
        if (++_index >= _cases.Count) Complete("DONE: inspect CHECK/SKIP/FAIL; unavailable cases are not measurements");
    }
    private void Complete(string text)
    {
        Add(text + $"; sink={_sink}"); Finished = true;
        string report = string.Join('\n', _report);
        _save($"bunny-{_id}.txt", report);
        _save("bunny-latest.txt", report);
    }
    private void Add(string line) { _report.Add(line); _log(line); }
    private static double Median(double[] values) { double[] copy = (double[])values.Clone(); Array.Sort(copy); return copy[1]; }
    private static MethodInfo? Find(Type owner, string name, params Type[] args) => owner.GetMethod(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance, null, args, null);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static bool WorldReady() => !Terraria.Main.gameMenu && Terraria.Main.player != null && Terraria.Main.myPlayer >= 0 && Terraria.Main.myPlayer < Terraria.Main.player.Length;
    private static Terraria.Player Player()
    {
        if (!WorldReady()) throw new NotSupportedException("world/player no longer available");
        return Terraria.Main.player[Terraria.Main.myPlayer] ?? throw new NotSupportedException("local player unavailable");
    }
    private static long Empty(int n) { long sum = 0; for (int i = 0; i < n; ++i) sum += i; return sum; }
    internal static long Clamp(int n) { float sum = 0; for (int i = 0; i < n; ++i) sum += Xna.MathHelper.Clamp(i * .5f, 0, 10); return (long)sum; }
    internal static long Distance(int n) { Xna.Vector2 a = new(1, 2), b = new(4, 6); float sum = 0; for (int i = 0; i < n; ++i) sum += Xna.Vector2.Distance(a, b); return (long)sum; }
    internal static long Matrix(int n)
    {
        float sum = 0;
        for (int i = 0; i < n; ++i) sum += Xna.Matrix.Add(_matrixA, _matrixB).M11;
        return (long)sum;
    }
    internal static void CheckMatrix()
    {
        _matrixA = Xna.Matrix.CreateScale(2f);
        _matrixB = Xna.Matrix.CreateScale(3f);
        Xna.Matrix result = Xna.Matrix.Add(_matrixA, _matrixB);
        Require(result.M11 == 5 && result.M22 == 5 && result.M33 == 5 && result.M44 == 2 && result.M12 == 0, "Matrix.Add expected scale sum diagonal 5,5,5,2");
    }
    private static long InWorld(int n) { long sum = 0; for (int i = 0; i < n; ++i) if (Terraria.WorldGen.InWorld(100 + (i & 7), 100, 0)) ++sum; return sum; }
    private static void CheckWorld() => Require(!Terraria.WorldGen.InWorld(-1, -1, 0) && Terraria.WorldGen.InWorld(100, 100, 0), "InWorld boundary/interior");
    private static long Buffs(int n) { var p = Player(); long sum = 0; for (int i = 0; i < n; ++i) sum += p.CountBuffs(); return sum; }
    private static void CheckBuffs() { var p = Player(); int expected = 0; for (int i = 0; i < p.buffType.Length; ++i) if (p.buffType[i] > 0) ++expected; Require(p.CountBuffs() == expected, "CountBuffs vs buffType slots"); }
    private static long Center(int n) { var p = Player(); float sum = 0; for (int i = 0; i < n; ++i) sum += p.Center.X; return (long)sum; }
    private static void CheckCenter() { var p = Player(); var center = p.Center; Require(MathF.Abs(center.X - (p.position.X + p.width * .5f)) < .001f && MathF.Abs(center.Y - (p.position.Y + p.height * .5f)) < .001f, "Center vs position + size/2"); }
    private static long Name(int n) { long sum = 0; for (int i = 0; i < n; ++i) sum += Terraria.Lang.GetItemNameValue(98)?.Length ?? 0; return sum; }
    private static void CheckName() => Require(!string.IsNullOrEmpty(Terraria.Lang.GetItemNameValue(98)), "localized item 98 name nonempty");

    // Read only the selected generated method's IL. Specific cctor initialization is setup, not timed.
    private static string Route(MethodInfo proxy)
    {
        byte[]? il = proxy.GetMethodBody()?.GetILAsByteArray();
        if (il == null) return "UNKNOWN (proxy IL unavailable)";
        bool guarded = false, invoke = false;
        IntPtr native = IntPtr.Zero;
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => unchecked((ushort)o.Value));
        for (int offset = 0; offset < il.Length;)
        {
            ushort code = il[offset++];
            if (code == 0xfe) code = (ushort)(0xfe00 | il[offset++]);
            OpCode op = opcodes[code];
            int bytes = op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, offset),
                _ => 4
            };
            if (op == OpCodes.Call)
            {
                var target = proxy.Module.ResolveMethod(BitConverter.ToInt32(il, offset));
                guarded |= target?.DeclaringType == typeof(GuardedInvoke) && target.Name == "Invoke";
                invoke |= target?.DeclaringType == typeof(IL2CPP) && target.Name == "il2cpp_runtime_invoke";
            }
            if (op == OpCodes.Ldsfld)
            {
                var field = proxy.Module.ResolveField(BitConverter.ToInt32(il, offset));
                if (field?.FieldType == typeof(IntPtr) && field.Name.StartsWith("NativeMethodInfoPtr_", StringComparison.Ordinal)) native = (IntPtr)field.GetValue(null)!;
            }
            offset += bytes;
        }
        if (!guarded) return invoke ? "FALLBACK (proxy runtime_invoke)" : "UNKNOWN (no generated game call found)";
        if (native == IntPtr.Zero) return "UNKNOWN (method pointer unavailable)";
        MethodInfo? supported = typeof(GuardedInvoke).GetMethod("IsDirectSupported", BindingFlags.Static | BindingFlags.Public, null, [typeof(IntPtr)], null);
        if (supported?.ReturnType == typeof(bool)) return (bool)supported.Invoke(null, [native])! ? "DIRECT (typed generated proxy)" : "FALLBACK (runtime ABI gate)";
        MethodInfo? resolve = typeof(GuardedInvoke).GetMethod("Resolve", BindingFlags.Static | BindingFlags.NonPublic);
        if (resolve == null) return "UNKNOWN (runtime route API unavailable)";
        return resolve.Invoke(null, [native]) != null ? "DIRECT (typed proxy, Resolve plan)" : "FALLBACK (runtime ABI gate)";
    }
}
