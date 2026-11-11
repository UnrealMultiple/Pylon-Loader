using System.Diagnostics;
using System.Globalization;
using Il2CppInterop.Runtime;
using Il2CppMicrosoft.Xna.Framework;

namespace PerfTest;

// Identical loop measured on frame entry and immediately after a full-loop warmup.
// No native bypass/test exports; all samples use the production guarded proxies.
internal sealed class FrameDiagSuite
{
    private const int N = 20000;
    private readonly Action<string> _add;
    private readonly Action<string, string> _save;
    private readonly List<string> _report = [];
    private readonly List<double>[,,] _samples = new List<double>[6,2,2];
    private readonly bool _oldCache = GuardedInvoke.FramePlanCacheEnabled;
    private readonly bool _oldPilot = GuardedInvoke.FramePilotEnabled;
    private readonly Func<int,long>[] _loops;
    private readonly string[] _names = ["Clamp/bunny", "Distance/bunny", "Matrix/bunny", "Clamp/constant", "Distance/constant", "Matrix/constant"];
    private int _case, _sample, _settle = 4;
    private readonly System.Reflection.PropertyInfo? _visible;
    private readonly object? _oldVisible;
    private long _sink, _last;
    private static Matrix _a, _b;
    internal bool Finished { get; private set; }
    internal FrameDiagSuite(Action<string> add, Action<string,string> save)
    {
        if (!GuardedInvoke.FrameDiagnosticsAvailable)
            throw new NotSupportedException("framediag requires PylonFrameDiagnostics=true; production route is cache-only");
        _add=add; _save=save;
        _visible=typeof(Pylon.ModLoader.UI.TerrariaUI).Assembly.GetType("Pylon.ModLoader.UI.ImGuiOverlay")?.GetProperty("Visible", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static);
        _oldVisible=_visible?.GetValue(null);
        _visible?.SetValue(null,false);
        _loops=[BunnySuite.Clamp, BunnySuite.Distance, BunnySuite.Matrix, ConstantClamp, ConstantDistance, ConstantMatrix];
        for(int c=0;c<6;c++) for(int m=0;m<2;m++) for(int h=0;h<2;h++) _samples[c,m,h]=[];
        Log($"FRAME DIAG {DateTimeOffset.Now:O}; N={N}; 5 alternating TLS/CACHED pairs per loop; first after frame gap vs immediate full-loop warm; raw ns/op, no empty subtraction");
        Log($"Overlay temporarily hidden={_visible != null}; restored at completion; isolates known renderer texture assertion, not a renderer fix");
        Log($"Runtime={Environment.Version}; tier env={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default"}; dynamic PGO={Environment.GetEnvironmentVariable("DOTNET_TieredPGO") ?? "default"}; neither env nor timings prove tier promotion");
        BunnySuite.CheckMatrix(); _a=Matrix.CreateScale(2f); _b=Matrix.CreateScale(3f);
        GuardedInvoke.FramePilotEnabled=true;
        _last=Stopwatch.GetTimestamp();
    }
    internal void Tick()
    {
        if(Finished) return;
        if (_settle-- > 0) return; // let previously queued render data drain before timed work
        try
        {
            int mode=(_sample/2+_sample%2)&1;
            GuardedInvoke.FramePlanCacheEnabled=mode==1;
            var loop=_loops[_case];
            long now=Stopwatch.GetTimestamp(); double gap=(now-_last)*1000d/Stopwatch.Frequency;
            string before=Cpu();
            long start=Stopwatch.GetTimestamp(); _sink^=loop(N); long first=Stopwatch.GetTimestamp()-start;
            _sink^=loop(N); // exact same full-loop warmup; no frame yield
            start=Stopwatch.GetTimestamp(); _sink^=loop(N); long hot=Stopwatch.GetTimestamp()-start;
            string after=Cpu(); _last=Stopwatch.GetTimestamp();
            double f=first*(1e9/Stopwatch.Frequency)/N, h=hot*(1e9/Stopwatch.Frequency)/N;
            _samples[_case,mode,0].Add(f); _samples[_case,mode,1].Add(h);
            Log(string.Create(CultureInfo.InvariantCulture,$"SAMPLE {_names[_case]} pair={_sample/2} {(mode==1?"CACHED":"TLS")} first={f:F2} hot={h:F2} gap-ms={gap:F2} cpu={before}->{after}"));
            if(MathHelper.Clamp(12f,0f,10f)!=10 || Vector2.Distance(new(1,2),new(4,6))!=5 || Matrix.Add(_a,_b).M11!=5) throw new InvalidOperationException("numerical check failed");
            if(++_sample<10) return;
            for(int m=0;m<2;m++)
                Log(string.Create(CultureInfo.InvariantCulture,$"RESULT {_names[_case]} {(m==1?"CACHED":"TLS")}: first-median={Median(_samples[_case,m,0]):F2} hot-median={Median(_samples[_case,m,1]):F2} ns/op; CHECK PASS"));
            _sample=0;
            if(++_case==6) Complete("DONE: all paired samples retained; CHECK PASS; sink="+_sink);
        }
        catch(Exception e) { Complete("FAIL: "+e); }
    }
    internal void Cancel() => Complete("CANCELLED");
    private void Complete(string message)
    {
        if(Finished)return;
        Log(message); Finished=true;
        if (message.StartsWith("DONE", StringComparison.Ordinal))
        {
            try { for (int round=0;round<3;round++) { Log("SAME-FRAME CACHE A/B round="+round); FrameAbSuite.Run(Log, cacheOnly:true); } }
            catch (Exception e) { Log("FAIL cache A/B: "+e); }
        }
        GuardedInvoke.FramePlanCacheEnabled=_oldCache; GuardedInvoke.FramePilotEnabled=_oldPilot;
        if (_oldVisible != null) _visible?.SetValue(null,_oldVisible);
        string report=string.Join('\n',_report);
        _save("framediag-latest.txt",report); _save($"framediag-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.txt",report);
    }
    private void Log(string line) { _report.Add(line); _add(line); }
    private static double Median(List<double> values) { var x=values.Order().ToArray(); return x[x.Length/2]; }
    private static string Cpu()
    {
        try
        {
            string stat=File.ReadAllText("/proc/thread-self/stat");
            string[] fields=stat[(stat.LastIndexOf(')')+2)..].Split(' ',StringSplitOptions.RemoveEmptyEntries);
            string cpu=fields[36]; // field 39, after pid + comm
            try { return cpu+"@"+File.ReadAllText($"/sys/devices/system/cpu/cpu{cpu}/cpufreq/scaling_cur_freq").Trim(); }
            catch { return cpu+"@unavailable"; }
        }
        catch { return "unavailable"; }
    }
    private static long ConstantClamp(int n) { float v=0; for(int i=0;i<n;i++)v=MathHelper.Clamp(12f,0f,10f);return (long)v; }
    private static long ConstantDistance(int n) { var a=new Vector2(1,2);var b=new Vector2(4,6);float v=0;for(int i=0;i<n;i++)v=Vector2.Distance(a,b);return (long)v; }
    private static long ConstantMatrix(int n) { Matrix v=default;for(int i=0;i<n;i++)v=Matrix.Add(_a,_b);return (long)v.M11; }
}
