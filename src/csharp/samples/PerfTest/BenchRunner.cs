using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PerfTest;

internal sealed unsafe class BenchRunner(Action<string> logger, string dataDirectory) : IDisposable
{
    private const int WarmupRounds = 3, SampleRounds = 9, OperationsPerRound = 256;
    private const double SliceMilliseconds = 2;
    private readonly object _viewGate = new();
    private readonly List<string> _view = [];
    private readonly List<string> _report = [];
    private List<BenchCase> _cases = [];
    private int _request, _cancel, _busy, _tickGate;
    private int _index, _round, _side, _done;
    private long _ticks, _bytes;
    private ulong _sink;
    private readonly List<double>[] _times = [[], []], _allocations = [[], []];
    private CoverageScan? _coverage;
    private bool _prepared;
    private BunnySuite? _bunny;
    private FrameDiagSuite? _frameDiag;
    private int _requestPoll;
    private string _runId = "";
    private const string ExternalDirectory = "/storage/emulated/0/Android/data/dev.pylon.loader/files/PerfTest";
    private string _status = "Idle: click Run (never auto-benchmarks)";
    private readonly MethodInfo? _resolve = typeof(Il2CppInterop.Runtime.GuardedInvoke).GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static);
    public string Status => Volatile.Read(ref _status);
    public string OutputDirectory { get; } = dataDirectory;
    public string[] Results { get { lock (_viewGate) return _view.ToArray(); } }

    public void RequestRun() => Request(1);
    public void RequestBunny() => Request(3);
    public void RequestSafety() => Request(4);
    private void PollRequest()
    {
        if (++_requestPoll < 30) return;
        _requestPoll = 0;
        foreach (string directory in new[] { OutputDirectory, ExternalDirectory }.Distinct())
        {
            string path = Path.Combine(directory, "request.txt");
            try
            {
                if (!File.Exists(path)) continue;
                string text = File.ReadAllText(path).Trim().ToLowerInvariant();
                File.Delete(path); // Consume exactly once; never replay a request after restart.
                if (text == "cancel") RequestCancel();
                else if (text == "bunny") RequestBunny();
                else if (text == "raw") RequestRun();
                else if (text == "coverage") RequestCoverage();
                else if (text == "safety") RequestSafety();
                else if (text == "frameab") Request(5);
                else if (text == "framediag") Request(6);
                else logger("[perf] ignored unknown request: " + text);
            }
            catch (Exception ex) { logger("[perf] request file: " + ex.Message); }
        }
    }
    public void RequestCoverage() => Request(2);
    public void RequestCancel() => Interlocked.Exchange(ref _cancel, 1);
    private void Request(int kind)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return;
        Interlocked.Exchange(ref _cancel, 0);
        Interlocked.Exchange(ref _request, kind);
        Volatile.Write(ref _status, "Queued for next game frame");
    }

    public void Tick()
    {
        if (Interlocked.Exchange(ref _tickGate, 1) != 0) return;
        try
        {
            PollRequest();
            if (Interlocked.Exchange(ref _cancel, 0) != 0 && Volatile.Read(ref _busy) != 0)
            {
                _frameDiag?.Cancel(); _frameDiag = null;
                Finish("CANCELLED: partial measurements are not a completed run");
                return;
            }
            int request = Interlocked.Exchange(ref _request, 0);
            if (request is 4 or 5)
            {
                _report.Clear();
                lock (_viewGate) _view.Clear();
                string prefix = request == 4 ? "safety" : "frameab";
                try { if (request == 4) SafetySuite.Run(Add); else FrameAbSuite.Run(Add); }
                catch (Exception ex) { Add("FAIL: " + ex.GetBaseException().Message); }
                finally
                {
                    string report = string.Join('\n', _report);
                    Save($"{prefix}-latest.txt", report);
                    Save($"{prefix}-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.txt", report);
                    Interlocked.Exchange(ref _busy, 0);
                    Volatile.Write(ref _status, "Safety complete; inspect PASS/FAIL (not a benchmark)");
                }
                return;
            }
            if (request == 2)
            {
                _coverage = new CoverageScan();
                Volatile.Write(ref _status, "Metadata coverage: planning only, no game methods executed");
            }
            else if (request == 1) Start();
            else if (request == 3)
            {
                _report.Clear();
                lock (_viewGate) _view.Clear();
                _bunny = new BunnySuite(Add, Save);
                Volatile.Write(ref _status, "Bunny-compatible: one continuous 20000-call round per frame");
            }
            if (request == 6) _frameDiag = new FrameDiagSuite(Add, Save);
            if (_frameDiag != null)
            {
                _frameDiag.Tick();
                if (_frameDiag.Finished) { _frameDiag = null; Interlocked.Exchange(ref _busy, 0); }
                return;
            }
            if (_bunny != null)
            {
                _bunny.Tick();
                if (_bunny.Finished)
                {
                    _bunny = null;
                    Interlocked.Exchange(ref _busy, 0);
                    Volatile.Write(ref _status, "Bunny-compatible complete; inspect PASS/SKIP/FAIL");
                }
                return;
            }
            if (_coverage != null)
            {
                if (_coverage.Step(SliceMilliseconds))
                {
                    string report = _coverage.Report();
                    foreach (string line in report.Split('\n')) logger("[perf] " + line);
                    Save("coverage.txt", report);
                    Save($"coverage-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.txt", report);
                    lock (_viewGate)
                    {
                        _view.Clear();
                        _view.Add("Metadata ABI plan coverage (NOT proxy-entry coverage)");
                        _view.AddRange(report.Split('\n').Where(x => x.StartsWith("TOTAL", StringComparison.Ordinal)));
                        _view.Add("Per-assembly detail is in coverage.txt and Mod log.");
                    }
                    _coverage = null;
                    Interlocked.Exchange(ref _busy, 0);
                    Volatile.Write(ref _status, "Coverage complete; click Run for benchmarks");
                }
                return;
            }
            if (_cases.Count != 0) Step();
        }
        catch (Exception ex)
        {
            if (_cases.Count > 0 && _index < _cases.Count)
            {
                string verdict = ex.GetBaseException() is NotSupportedException ? "SKIP" : "FAIL";
                Add($"{verdict} {_cases[_index].Label}: {ex.GetBaseException().Message}");
                Next();
            }
            else Finish("FAILED: " + ex.GetBaseException().Message);
        }
        finally { Volatile.Write(ref _tickGate, 0); }
    }

    private void Start()
    {
        DisposeCases();
        _report.Clear();
        lock (_viewGate) _view.Clear();
        _index = 0;
        _cases = BenchCase.Create();
        _runId = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        Add($"PerfTest RAW {DateTimeOffset.Now:O}; .NET {Environment.Version}; {RuntimeInformation.ProcessArchitecture}; game-thread {Environment.CurrentManagedThreadId}");
        Add($"warmup={WarmupRounds}, samples={SampleRounds}, ops/round={OperationsPerRound}, frame target={SliceMilliseconds}ms, timer={Stopwatch.Frequency}Hz");
        Add("Same MethodInfo+args+self=0 for both paths. runtime path includes object_unbox; protected path includes GuardedInvoke checks and result buffer.");
        Add("CoreCLR B/op excludes IL2CPP allocations/boxing. No forced GC. No baseline subtraction. Tick checks every 8 calls; one slow native call cannot be preempted.");
        ResetCase();
    }

    private void ResetCase()
    {
        _prepared = false; _round = _side = _done = 0; _ticks = _bytes = 0;
        foreach (var list in _times) list.Clear();
        foreach (var list in _allocations) list.Clear();
    }

    private void Step()
    {
        BenchCase test = _cases[_index];
        if (!_prepared)
        {
            test.Prepare(_resolve);
            _prepared = true;
            Add($"CASE {test.Label} {test.Assembly}:{test.Namespace}.{test.Class}::{test.Method}; MethodInfo=0x{test.Info:X}; {test.Route}; CHECK PASS");
            return; // setup/class initialization/JIT is never part of a timed sample.
        }
        bool guarded = ((_round & 1) == 0 ? _side : 1 - _side) == 0;
        long deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * SliceMilliseconds / 1000);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        do
        {
            int chunk = Math.Min(8, OperationsPerRound - _done);
            for (int i = 0; i < chunk; i++)
            {
                IntPtr result = test.Invoke(guarded);
                _sink ^= *(uint*)result; // observe identical returned bytes, no managed proxy allocation.
            }
            _done += chunk;
        } while (_done < OperationsPerRound && Stopwatch.GetTimestamp() < deadline);
        long elapsed = Stopwatch.GetTimestamp() - started;
        _ticks += elapsed;
        _bytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
        Volatile.Write(ref _status, $"{test.Label} {test.Route}; round {_round + 1}/{WarmupRounds + SampleRounds}; {(guarded ? "protected" : "runtime")} {_done}/{OperationsPerRound}");
        if (_done < OperationsPerRound) return;
        if (_round >= WarmupRounds)
        {
            int path = guarded ? 0 : 1;
            _times[path].Add(_ticks * (1_000_000_000d / Stopwatch.Frequency) / OperationsPerRound);
            _allocations[path].Add((double)_bytes / OperationsPerRound);
        }
        test.Check(guarded); // Untimed correctness after each round, not just a one-off setup check.
        _done = 0; _ticks = _bytes = 0;
        if (++_side < 2) return;
        _side = 0;
        if (++_round < WarmupRounds + SampleRounds) return;
        for (int path = 0; path < 2; path++)
            Add(string.Create(CultureInfo.InvariantCulture, $"RESULT {test.Label} {(path == 0 ? test.Route : "runtime_invoke")} ns/op median={Median(_times[path]):F2} min={_times[path].Min():F2}; CoreCLR B/op median={Median(_allocations[path]):F3}"));
        if (test.Route.StartsWith("DIRECT", StringComparison.Ordinal))
            Add(string.Create(CultureInfo.InvariantCulture, $"RATIO {test.Label} runtime/protected={Median(_times[1]) / Median(_times[0]):F3}x; CHECK PASS"));
        else Add($"NO DIRECT SPEEDUP CLAIM: {test.Route}");
        Next();
    }

    private static double Median(List<double> samples)
    {
        double[] sorted = samples.ToArray();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }
    private void Next()
    {
        _cases[_index].Dispose();
        if (++_index >= _cases.Count) Finish("DONE (inspect CASE/FAIL lines; unavailable cases are not measured)");
        else ResetCase();
    }
    private void Add(string line)
    {
        _report.Add(line);
        lock (_viewGate) { _view.Add(line); if (_view.Count > 6) _view.RemoveAt(0); }
        logger("[perf] " + line);
    }
    private void Save(string file, string text)
    {
        try
        {
            System.IO.Directory.CreateDirectory(OutputDirectory);
            File.WriteAllText(Path.Combine(OutputDirectory, file), text);
            // Export copy is accessible without run-as on devices permitting app external files.
            string external = ExternalDirectory;
            System.IO.Directory.CreateDirectory(external);
            File.WriteAllText(Path.Combine(external, file), text);
        }
        catch (Exception ex) { logger("[perf] result file write failed: " + ex.Message); }
    }
    private void Finish(string reason)
    {
        if (_bunny != null)
        {
            _bunny.Cancel();
            _bunny = null;
        }
        else if (_report.Count > 0)
        {
            Add(reason);
            string report = string.Join('\n', _report);
            Save("results-latest.txt", report);
            Save($"raw-{_runId}.txt", report);
        }
        _coverage = null;
        DisposeCases();
        Interlocked.Exchange(ref _request, 0);
        Interlocked.Exchange(ref _busy, 0);
        Volatile.Write(ref _status, reason);
    }
    private void DisposeCases() { foreach (BenchCase test in _cases) test.Dispose(); _cases.Clear(); }
    public void Dispose() => Finish("Unloaded / cancelled");
}
