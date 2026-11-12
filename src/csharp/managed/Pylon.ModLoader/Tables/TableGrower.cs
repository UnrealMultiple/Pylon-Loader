using System;
using System.Collections.Generic;
using System.Diagnostics;
using Il2CppInterop.Runtime;

namespace Pylon.ModLoader.Tables;

/// <summary>一类表的扩容器：枚举类里的静态数组字段（il2cpp 指针，不建包装、不反射），长度等于原版表的扩到目标长度。</summary>
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

    /// <summary>游戏把表重建回原版长度：再扩一遍。装配期一次都没扫到过（表当时没建）就补扫。</summary>
    internal int Watch(Action<string>? log)
    {
        if (!Ready) return 0;
        return _watched.Count == 0 ? GrowAll(log) : SweepTargets(log);
    }

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
            if (!IsStaticArray(field) || !IsAllowed(field)) continue;
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

    private bool IsAllowed(IntPtr field)
    {
        var name = NameOf(field);

        // 白名单优先：非空时只认名单里的字段（表长太普通、黑名单拦不全）。
        var include = _set.Include;
        if (include is { Length: > 0 })
        {
            foreach (var candidate in include)
                if (string.Equals(candidate, name, StringComparison.Ordinal)) return true;
            return false;
        }

        var skip = _set.Skip;
        if (skip is not { Length: > 0 }) return true;
        foreach (var candidate in skip)
            if (string.Equals(candidate, name, StringComparison.Ordinal)) return false;
        return true;
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

    // This API takes the object pointer itself for reference fields; a pointer to a local stores the stack address.
    private static unsafe void WriteStatic(IntPtr field, IntPtr value) =>
        IL2CPP.il2cpp_field_static_set_value(field, value.ToPointer());
}
