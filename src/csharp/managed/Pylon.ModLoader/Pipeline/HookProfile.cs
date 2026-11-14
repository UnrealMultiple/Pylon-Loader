using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// Hook 耗时统计：per 钩子×Mod 的调用次数、累计与均值。开关落 .pylon/profile.json（照 NetPolicy 的
/// .pylon/net.json 先例），周期汇总进日志 + 全量快照落 .pylon/profile.snapshot.json。
/// 统计面 = HookBus 通道扇出（Mod 级钩子）；
/// 内容级 per 实体钩子走各自分派点，不在本面。
/// </summary>
internal static class HookProfile
{
    // ---- 配置 ----

    private static bool _loaded;
    private static bool _enabled;
    private static int _intervalSeconds = 30;

    /// <summary>统计开关；关闭时分派路径一次 bool 判空即走，零计时。</summary>
    internal static bool Enabled
    {
        get
        {
            EnsureLoaded();
            return _enabled;
        }
    }

    private static string? SettingsPath
    {
        get
        {
            var root = Framework.HostDirectory;
            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, ".pylon", "profile.json");
        }
    }

    private static string? SnapshotPath
    {
        get
        {
            var root = Framework.HostDirectory;
            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, ".pylon", "profile.snapshot.json");
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        var path = SettingsPath;
        if (path is null) return; // 宿主上下文还没就位：先不缓存，下次再读
        _loaded = true;
        try
        {
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (TryBool(doc.RootElement, "enabled", out var enabled)) _enabled = enabled;
            if (TryInt(doc.RootElement, "intervalSeconds", out var interval) && interval is >= 5 and <= 3600)
                _intervalSeconds = interval;
        }
        catch (Exception e)
        {
            Safe.Warn("读取统计设置失败（用默认值）：" + e.Message);
        }
    }

    private static bool TryBool(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(name, out var property)) return false;
        if (property.ValueKind == JsonValueKind.True) { value = true; return true; }
        if (property.ValueKind == JsonValueKind.False) { value = false; return true; }
        return false;
    }

    private static bool TryInt(JsonElement element, string name, out int value)
    {
        value = 0;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number)
            return false;
        return property.TryGetInt32(out value);
    }

    // ---- 密集统计：索引 = hook * modSlots + mod.Index，long 累加不分配 ----

    private static readonly int HookCount = Enum.GetValues<HookId>().Length;
    private static readonly double NsPerTick = 1_000_000_000.0 / Stopwatch.Frequency;

    private static long[] _calls = [];
    private static long[] _totalNs = [];
    private static long[] _previous = [];
    private static int _modSlots;
    private static string[] _hookNames = [];
    private static IModLog _log = NullLog.Instance;
    private static long _lastReport;

    /// <summary>HookBus.Rebuild 时同步 Mod 槽位数。Mod 集合只在注册期增长；旧行按旧步长迁移，不丢注册期数据。</summary>
    internal static void EnsureCapacity(int modSlots)
    {
        if (modSlots <= _modSlots) return;
        var oldSlots = _modSlots;
        var oldCalls = _calls;
        var oldTotals = _totalNs;
        _modSlots = modSlots;
        _calls = new long[HookCount * modSlots];
        _totalNs = new long[HookCount * modSlots];
        if (oldSlots == 0) return;
        for (var hook = 0; hook < HookCount; hook++)
        {
            for (var slot = 0; slot < oldSlots; slot++)
            {
                _calls[hook * modSlots + slot] = oldCalls[hook * oldSlots + slot];
                _totalNs[hook * modSlots + slot] = oldTotals[hook * oldSlots + slot];
            }
        }
        _previous = []; // 增量基线按新步长重建，下一条汇总把累计当增量报一次（顺便展示总账）
    }

    /// <summary>只由 <see cref="ModChannel"/> 的枚举器在关闭计时段时调用；不锁（竞态最坏丢一次计数）。</summary>
    internal static void Record(int hook, int modSlot, long ns)
    {
        var index = hook * _modSlots + modSlot;
        if ((uint)index >= (uint)_calls.Length) return;
        _calls[index]++;
        _totalNs[index] += ns;
    }

    internal static long TicksToNs(long ticks) => (long)(ticks * NsPerTick);

    // ---- 安装与周期汇总 ----

    internal static void Install(IModLog log)
    {
        _log = log;
        _hookNames = Enum.GetNames<HookId>();
        _lastReport = Environment.TickCount64;
        if (!Enabled)
        {
            log.Info("[profile] 关闭（.pylon/profile.json 写 enabled=true 可开启）");
            return;
        }
        On.Terraria.Main.DoUpdate += Tick;
        log.Info($"[profile] 已开启（每 {_intervalSeconds}s 输出增量汇总；快照落 .pylon/profile.snapshot.json）");
    }

    private static void Tick(On.Terraria.Main.orig_DoUpdate orig, Main self, GameTime gameTime)
    {
        orig(self, gameTime);
        try
        {
            if (Environment.TickCount64 - _lastReport < _intervalSeconds * 1000L) return;
            _lastReport = Environment.TickCount64;
            Report();
        }
        catch (Exception e)
        {
            Safe.Warn("[profile] 汇总失败：" + Safe.Describe(e));
        }
    }

    private readonly record struct Row(int Hook, int Slot, long Calls, long TotalNs, long DeltaCalls, long DeltaTotalNs);

    private static void Report()
    {
        var calls = _calls;
        var totals = _totalNs;
        var modSlots = _modSlots;
        if (modSlots == 0 || calls.Length == 0) return;

        var previous = _previous.Length == calls.Length ? _previous : new long[calls.Length];
        var mods = LoaderHost.Current.Mods.Mods;

        List<Row> rows = [];
        List<Row> deltas = [];
        for (var hook = 0; hook < HookCount; hook++)
        {
            for (var slot = 0; slot < modSlots; slot++)
            {
                var index = hook * modSlots + slot;
                var total = totals[index];
                if (total == 0) continue;
                var row = new Row(hook, slot, calls[index], total, calls[index] - previous[index], total - previous[index]);
                rows.Add(row);
                if (row.DeltaCalls > 0) deltas.Add(row);
            }
        }

        if (deltas.Count > 0)
        {
            deltas.Sort((a, b) => b.DeltaTotalNs.CompareTo(a.DeltaTotalNs));
            var top = Math.Min(deltas.Count, 8);
            _log.Info($"[profile] 近 {_intervalSeconds}s 增量 top {top}（有活动的钩子·Mod 组合共 {deltas.Count} 个）：");
            for (var i = 0; i < top; i++)
            {
                var row = deltas[i];
                var mean = row.DeltaTotalNs / row.DeltaCalls;
                _log.Info($"[profile]   {_hookNames[row.Hook]} · {ModName(mods, row.Slot)}：{row.DeltaCalls} 次，" +
                          $"均 {FormatNs(mean)}，期间 {FormatNs(row.DeltaTotalNs)}，累计 {FormatNs(row.TotalNs)}");
            }
            _log.Info($"[profile] 全量累计（{rows.Count} 行）已写 .pylon/profile.snapshot.json");
        }

        Array.Copy(totals, previous, totals.Length);
        _previous = previous;
        WriteSnapshot(rows);
    }

    private static string ModName(IReadOnlyList<Mod> mods, int slot) =>
        (uint)slot < (uint)mods.Count ? mods[slot].Name : "?";

    private static string FormatNs(long ns)
    {
        return ns switch
        {
            < 1_000 => $"{ns}ns",
            < 1_000_000 => $"{(ns / 1000.0).ToString("F1", Inv)}µs",
            < 1_000_000_000 => $"{(ns / 1_000_000.0).ToString("F1", Inv)}ms",
            _ => $"{(ns / 1_000_000_000.0).ToString("F2", Inv)}s",
        };
    }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly JsonSerializerOptions SnapshotOptions = new() { WriteIndented = true };

    private static void WriteSnapshot(List<Row> rows)
    {
        if (rows.Count == 0) return; // 保留上一份快照，别拿空文件盖掉
        var path = SnapshotPath;
        if (path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            List<SnapshotRow> payload = new(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                payload.Add(new SnapshotRow(_hookNames[row.Hook], ModName(LoaderHost.Current.Mods.Mods, row.Slot), row.Calls, row.TotalNs));
            }
            File.WriteAllText(path, JsonSerializer.Serialize(payload, SnapshotOptions));
        }
        catch (Exception e)
        {
            Safe.Warn("[profile] 快照写入失败：" + e.Message);
        }
    }

    private sealed class SnapshotRow(string hook, string mod, long calls, long totalNs)
    {
        public string Hook { get; } = hook;
        public string Mod { get; } = mod;
        public long Calls { get; } = calls;
        public long TotalNs { get; } = totalNs;
        public long MeanNs { get; } = calls > 0 ? totalNs / calls : 0;
    }
}

/// <summary>
/// HookBus 通道的返回面：用法与 <c>ReadOnlySpan&lt;Mod&gt;</c> 一致（Length/IsEmpty/索引器/foreach），
/// 分派点零改动。统计开着时在枚举器里计时——窗口 = 同一通道两次 MoveNext 之间，正好包住一个 Mod
/// 回调（含分派点 try/catch 的开销）；foreach 的 break/return/异常出口由编译器调 Dispose 补关最后一段。
/// 嵌套扇出各自独立计时（互不踩），只记 total 不拆 self。
/// </summary>
internal ref struct ModChannel
{
    private readonly ReadOnlySpan<Mod> _mods;
    private readonly int _hook;

    internal ModChannel(ReadOnlySpan<Mod> mods, int hook)
    {
        _mods = mods;
        _hook = hook;
    }

    public int Length => _mods.Length;

    public bool IsEmpty => _mods.IsEmpty;

    public Mod this[int index] => _mods[index];

    public Enumerator GetEnumerator() => new(_mods, _hook, HookProfile.Enabled);

    internal ref struct Enumerator
    {
        private readonly ReadOnlySpan<Mod> _mods;
        private readonly int _hook;
        private readonly bool _profile;
        private int _index;
        private long _start; // 当前计时段的起点（开着才维护）
        private bool _open;  // 有未关闭的计时段

        internal Enumerator(ReadOnlySpan<Mod> mods, int hook, bool profile)
        {
            _mods = mods;
            _hook = hook;
            _profile = profile;
            _index = -1;
        }

        public Mod Current => _mods[_index];

        public bool MoveNext()
        {
            if (!_profile) return ++_index < _mods.Length;

            Close();
            var next = _index + 1;
            if (next >= _mods.Length) return false;
            _index = next;
            _start = Stopwatch.GetTimestamp();
            _open = true;
            return true;
        }

        /// <summary>循环中途 break/return/异常也会走到这里——把最后一个未关的计时段补上。</summary>
        public void Dispose()
        {
            if (_open) Close();
        }

        private void Close()
        {
            _open = false;
            if (_index < 0) return;
            HookProfile.Record(_hook, _mods[_index].Index, HookProfile.TicksToNs(Stopwatch.GetTimestamp() - _start));
        }
    }
}
