using System;
using System.Collections.Generic;
using System.Diagnostics;
using Il2CppInterop.Runtime;

namespace Pylon.ModLoader.Tables;

// 扩表只有一个原语：ArrayGrow.Grow（见 ArrayGrow.cs）。下面这些只是"扩哪些表、什么时候扩"的描述。

/// <summary>一类跟着内容编号变长的原生表：原版长度 + 要一起变长的类。</summary>
internal sealed class TableSet
{
    private readonly Func<int> _vanilla;

    internal TableSet(string name, Func<int> vanilla, Type[] types, bool fillWithMode = false, string[]? skip = null)
    {
        Name = name;
        _vanilla = vanilla;
        Types = types;
        FillWithMode = fillWithMode;
        Skip = skip;
    }

    /// <summary>日志里用的中文名。</summary>
    internal string Name { get; }

    /// <summary>原版表长（要在扩表之前读；0 = 表还没建）。</summary>
    internal int VanillaCount => _vanilla();

    /// <summary>要扫静态数组字段的类。</summary>
    internal Type[] Types { get; }

    /// <summary>尾部填原数组的众数（方块/墙壁：填 0 会变成实心泥土）。</summary>
    internal bool FillWithMode { get; }

    /// <summary>这些类里不跟着变长的字段名。</summary>
    internal string[]? Skip { get; }
}

/// <summary>一条扩表请求：哪张表、目标多长、要不要在内容装配前扩好。</summary>
internal readonly struct TableRequest(TableSet set, Func<int> target, bool early = false)
{
    internal TableSet Set { get; } = set;

    /// <summary>目标表长，每次重新算。</summary>
    internal Func<int> Target { get; } = target;

    /// <summary>true = 内容装配（发号 / 写槽）之前必须扩好。</summary>
    internal bool Early { get; } = early;
}

/// <summary>扩表调度：按各管线声明的表扩到位，同一类表全进程只有一份扩表器。</summary>
internal static class TableMaintenance
{
    internal static readonly List<TableGrower> Active = [];
    private static int _watchTicks;

    internal static TableGrower? Find(TableSet set)
    {
        for (var i = 0; i < Active.Count; i++)
            if (Active[i].Set == set) return Active[i];
        return null;
    }

    /// <summary>全部模块声明的扩表请求。</summary>
    internal static IReadOnlyList<TableRequest> Requests => LoaderRegistry.Tables;

    /// <summary>取这一类表的扩表器，并把目标长度抬到至少 <paramref name="target"/>。</summary>
    internal static TableGrower Ensure(TableSet set, int target)
    {
        var grower = Find(set);
        if (grower is null) return new TableGrower(set, target);
        grower.Ensure(target);
        return grower;
    }

    /// <summary>内容装配前必须扩好的表一次扩完；表还没建就返回 false，下一帧再来。</summary>
    internal static bool PumpEarly(Action<string>? log)
    {
        var ready = true;

        foreach (var request in Requests)
        {
            if (!request.Early) continue;

            var grower = Ensure(request.Set, request.Target());
            if (grower.Vanilla <= 0)
            {
                ready = false;
                continue;
            }

            grower.GrowAll(log);
        }

        return ready;
    }

    /// <summary>每帧补扩，另外每 120 帧查一遍游戏重建。</summary>
    public static int Maintain(Action<string>? log = null, Action<TableSet>? regrown = null)
    {
        if (Active.Count == 0) return 0;

        var grown = 0;
        for (var i = 0; i < Active.Count; i++)
        {
            var n = Active[i].CheckPending(log);
            if (n <= 0) continue;
            grown += n;
            Notify(regrown, Active[i], log);
        }

        if (++_watchTicks >= 120)
        {
            _watchTicks = 0;
            for (var i = 0; i < Active.Count; i++)
            {
                var n = Active[i].Watch(log);
                if (n <= 0) continue;
                grown += n;
                Notify(regrown, Active[i], log);
            }
        }

        return grown;
    }

    private static void Notify(Action<TableSet>? regrown, TableGrower grower, Action<string>? log)
    {
        if (regrown is null) return;
        try { regrown(grower.Set); }
        catch (Exception e) { log?.Invoke($"[tables] {grower.Name} 换表后重写失败：{e.Message}"); }
    }

    /// <summary>自检：某类表里扩到位 / 还是原版 / 还没建 各几张。</summary>
    public static bool Audit(TableSet set, out int total, out int grown, out int stale, out int missing,
                             List<string>? missingNames = null)
    {
        total = grown = stale = missing = 0;
        var grower = Find(set);
        if (grower is null) return false;
        grower.Audit(out total, out grown, out stale, out missing, missingNames);
        return true;
    }
}

/// <summary>
/// 一类表的扩容器：按类枚举静态数组字段（il2cpp 字段指针，不建 interop 包装、不走反射），
/// 长度等于原版表的当场扩到目标长度。不分帧，真机整批几十毫秒。
/// </summary>
internal sealed class TableGrower
{
    private const uint FieldStatic = 0x0010;   // FieldAttributes.Static
    private const uint FieldLiteral = 0x0040;  // const
    private const int TypeSzArray = 0x1D;      // T[]
    private const int ThreadStatic = -1;

    private readonly TableSet _set;
    private readonly List<IntPtr> _watched = [];   // 扫到的全部静态数组字段
    private readonly List<IntPtr> _targets = [];   // 已确认是本类表的字段
    private readonly List<IntPtr> _pending = [];   // 扫到时还是 null 的字段
    private int _capacity;
    private int _vanilla;
    private int _grown;

    internal TableGrower(TableSet set, int capacity)
    {
        _set = set;
        _capacity = capacity;

        // 同类的旧扩表器（重装 / 重跑）换成这一份，别让维护期出现两份
        for (var i = 0; i < TableMaintenance.Active.Count; i++)
        {
            if (TableMaintenance.Active[i].Set != set) continue;
            TableMaintenance.Active[i] = this;
            return;
        }
        TableMaintenance.Active.Add(this);
    }

    internal TableSet Set => _set;
    internal string Name => _set.Name;

    /// <summary>原版表长；表还没建时读到 0，不缓存，下次再问。</summary>
    internal int Vanilla
    {
        get
        {
            if (_vanilla > 0) return _vanilla;
            var current = _set.VanillaCount;
            if (current > 0) _vanilla = current;
            return _vanilla;
        }
    }

    internal void Ensure(int capacity)
    {
        if (capacity > _capacity) _capacity = capacity;
    }

    /// <summary>扫一遍类清单并扩到位，返回本次扩了几张。</summary>
    internal int GrowAll(Action<string>? log)
    {
        if (!Ready) return 0;

        var watch = Stopwatch.StartNew();
        var before = _grown;

        foreach (var type in _set.Types) Scan(type, log);
        SweepTargets(log);

        if (watch.ElapsedMilliseconds >= 20)
            log?.Invoke($"[tables] {_set.Name} {watch.ElapsedMilliseconds}ms，扩了 {_grown - before} 张");

        return _grown - before;
    }

    /// <summary>扫到时还没建出来的表：建出来就补扩（每帧）。</summary>
    internal int CheckPending(Action<string>? log)
    {
        if (_pending.Count == 0 || !Ready) return 0;

        var grown = 0;
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var field = _pending[i];
            var array = ReadStatic(field);
            if (array == IntPtr.Zero) continue;

            _pending.RemoveAt(i);
            if (ArrayGrow.Length(array) != Vanilla) continue;
            _targets.Add(field);
            if (!GrowField(field, array)) continue;
            _grown++;
            grown++;
        }

        if (grown > 0) log?.Invoke($"[tables] {_set.Name} 补扩：{grown} 张");
        return grown;
    }

    /// <summary>游戏把表重建回原版长度：再扩一遍。</summary>
    internal int Watch(Action<string>? log) => Ready ? SweepTargets(log) : 0;

    /// <summary>自检：该扩的表里扩到位 / 还是原版 / 还没建 各几张。</summary>
    internal void Audit(out int total, out int grown, out int stale, out int missing, List<string>? missingNames = null)
    {
        total = grown = stale = missing = 0;
        if (!Ready) return;

        foreach (var field in _targets)
        {
            total++;
            var array = ReadStatic(field);
            if (array == IntPtr.Zero)
            {
                missing++;
                missingNames?.Add(NameOf(field));
                continue;
            }
            var length = ArrayGrow.Length(array);
            if (length >= _capacity) grown++;
            else if (length == Vanilla) stale++;
        }

        foreach (var field in _pending)
        {
            total++;
            if (ReadStatic(field) != IntPtr.Zero) continue;
            missing++;
            missingNames?.Add(NameOf(field));
        }
    }

    private bool Ready => _capacity > Vanilla && Vanilla > 0;

    /// <summary>扫一个类：枚举静态数组字段，长度等于原版表的记进目标。</summary>
    private void Scan(Type type, Action<string>? log)
    {
        var klass = Il2CppClassPointerStore.GetNativeClassPointer(type);
        if (klass == IntPtr.Zero)
        {
            log?.Invoke($"[tables] {_set.Name} 找不到 {type.Name}");
            return;
        }

        try { IL2CPP.il2cpp_runtime_class_init(klass); } catch { }

        var iterator = IntPtr.Zero;
        while (true)
        {
            var field = IL2CPP.il2cpp_class_get_fields(klass, ref iterator);
            if (field == IntPtr.Zero) return;
            if (!IsStaticArray(field) || IsSkipped(field)) continue;
            if (_watched.Contains(field)) continue;
            _watched.Add(field);

            var array = ReadStatic(field);
            if (array == IntPtr.Zero)
            {
                _pending.Add(field);
                continue;
            }
            if (ArrayGrow.Length(array) != Vanilla) continue;

            _targets.Add(field);
            log?.Invoke($"[grow] {_set.Name} {type.Name}.{NameOf(field)} len={Vanilla}→{_capacity}");
            if (GrowField(field, array)) _grown++;
        }
    }

    /// <summary>已确认的表（含游戏重建后的）统一补到目标长度。</summary>
    private int SweepTargets(Action<string>? log)
    {
        var grown = 0;
        foreach (var field in _targets)
        {
            var array = ReadStatic(field);
            if (array == IntPtr.Zero || ArrayGrow.Length(array) >= _capacity) continue;
            if (!GrowField(field, array)) continue;
            _grown++;
            grown++;
        }

        if (grown > 0) log?.Invoke($"[tables] {_set.Name} 补扩：{grown} 张");
        return grown;
    }

    private bool GrowField(IntPtr field, IntPtr array)
    {
        var grown = ArrayGrow.Grow(array, _capacity, _set.FillWithMode);
        if (grown == IntPtr.Zero) return false;
        WriteStatic(field, grown);
        return true;
    }

    private static bool IsStaticArray(IntPtr field)
    {
        var attributes = (uint)IL2CPP.il2cpp_field_get_flags(field);
        if ((attributes & FieldStatic) == 0 || (attributes & FieldLiteral) != 0) return false;
        if (unchecked((int)IL2CPP.il2cpp_field_get_offset(field)) == ThreadStatic) return false;
        return IL2CPP.il2cpp_type_get_type(IL2CPP.il2cpp_field_get_type(field)) == TypeSzArray;
    }

    private bool IsSkipped(IntPtr field)
    {
        var skip = _set.Skip;
        if (skip is not { Length: > 0 }) return false;
        var name = NameOf(field);
        foreach (var candidate in skip)
            if (string.Equals(candidate, name, StringComparison.Ordinal)) return true;
        return false;
    }

    private static string NameOf(IntPtr field)
    {
        try { return IL2CPP.il2cpp_field_get_name_(field) ?? "?"; }
        catch { return "?"; }
    }

    private static unsafe IntPtr ReadStatic(IntPtr field)
    {
        var value = IntPtr.Zero;
        IL2CPP.il2cpp_field_static_get_value(field, &value);
        return value;
    }

    private static unsafe void WriteStatic(IntPtr field, IntPtr value) =>
        IL2CPP.il2cpp_field_static_set_value(field, (void*)value);
}
