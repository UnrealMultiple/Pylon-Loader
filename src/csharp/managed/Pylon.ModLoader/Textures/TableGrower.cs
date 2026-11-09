using System;
using System.Collections.Generic;
using System.Diagnostics;
using Il2CppInterop.Runtime;
using static Pylon.ModLoader.Textures.NativeTables;

namespace Pylon.ModLoader.Textures;

/// <summary>已经跑过（还在盯着的）扩表器：每帧补 pending / 每 120 帧查重建。</summary>
internal static class TableMaintenance
{
    internal static readonly List<TableGrower> Active = [];
    private static int _watchTicks;

    /// <summary>找到某一类表的扩表器（没扩过就是 null）。</summary>
    internal static TableGrower? Find(TableSet set)
    {
        for (var i = 0; i < Active.Count; i++)
            if (Active[i].Set == set) return Active[i];
        return null;
    }

    /// <summary>
    /// 被游戏重建回原版长度的表。返回本次真正扩了几张；<paramref name="regrown"/> 拿到表集合回调，
    /// 贴图/名字要往新表重写一遍。
    /// </summary>
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

    /// <summary>表换了内容（补扩/重建）：贴图与名字要往新表再写一遍。</summary>
    private static void Notify(Action<TableSet>? regrown, TableGrower grower, Action<string>? log)
    {
        if (regrown is null) return;
        try { regrown(grower.Set); }
        catch (Exception e) { log?.Invoke($"[typetables] {grower.Name} 换了新表后重写失败：{e.Message}"); }
    }

    /// <summary>全部模块声明的扩表请求（谁用到谁声明，框架按模块顺序执行）。</summary>
    internal static IReadOnlyList<TableRequest> Requests => LoaderRegistry.Tables;

    /// <summary>拿这一类表的扩表器，并把目标长度至少抬到 <paramref name="target"/>。</summary>
    internal static TableGrower Ensure(TableSet set, int target)
    {
        var grower = Find(set);
        if (grower is null) return new TableGrower(set, target);
        grower.Ensure(target);
        return grower;
    }

    /// <summary>
    /// 把"内容装配前必须扩好"的表一次扩完；全部就绪返回 true。
    /// 表还没建（原版长度读不到）时返回 false，下一帧再来 —— 不阻塞、不猜。
    /// </summary>
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
/// 一类表的扩容器：扫完类清单，该扩的当场一次扩完（真机整批几毫秒），不分帧、没有批量预算。
/// 扩完留在 <see cref="TableMaintenance"/> 的名单里，负责补扩（扫到时还是 null 的表）与游戏重建后的重扩。
/// </summary>
internal sealed class TableGrower
{
    private readonly TableSet _set;
    private readonly string _name;
    private readonly TableClass[] _classes;
    private readonly bool _fillWithMode;
    private int _newCapacity;
    private int _vanilla;

    private int _classIndex;
    private readonly List<IntPtr> _fields = [];      // 扫到的全部静态数组字段（Watch 用）
    private readonly List<IntPtr> _candidates = [];  // 其中"长度 == 原版数、该扩"的表（自检用）
    private readonly List<IntPtr> _pending = [];     // 扫到时还是 null、之后每帧补的表
    private int _fieldIndex;
    private int _grown;

    internal TableGrower(TableSet set, int newCapacity)
    {
        _set = set;
        _name = set.Name;
        _newCapacity = newCapacity;
        _classes = set.Classes;
        _fillWithMode = set.FillWithMode;

        // 同种类的旧扩表器（重装/诊断重跑）换成这一份，别让 Maintenance 双份跟踪
        for (var i = 0; i < TableMaintenance.Active.Count; i++)
        {
            if (TableMaintenance.Active[i].Set != set) continue;
            TableMaintenance.Active[i] = this;
            return;
        }
        TableMaintenance.Active.Add(this);
    }

    internal TableSet Set => _set;
    internal string Name => _name;

    /// <summary>
    /// 原版表长。惰性读：表还没建时读到的 0 当"还不知道"，不缓存、也不据此判 Done ——
    /// 框架会每帧再问一次，等游戏把表建出来再扩。
    /// </summary>
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

    /// <summary>目标长度上调（同一类表在安装后又被要求更长时用）。</summary>
    internal void Ensure(int capacity)
    {
        if (capacity > _newCapacity) _newCapacity = capacity;
    }

    /// <summary>清单走完、且扫到的字段都处理过（扩了或判过不是这类表）。</summary>
    internal bool Done
    {
        get
        {
                var vanilla = Vanilla;
                if (vanilla <= 0) return false;              // 表还没建：继续等
                if (_newCapacity <= vanilla) return true;     // 不需要扩
                return _classIndex >= _classes.Length && _fieldIndex >= _fields.Count;
            }
        }

    /// <summary>
    /// 一次扩完：扫完清单里的每个类、每个静态数组字段，该扩的全扩。
    /// 返回本次扩了几张；表还没建（原版长度读不到）返回 0，等下一帧再试。
    /// </summary>
    internal int GrowAll(Action<string>? log)
    {
        var vanilla = Vanilla;
        if (vanilla <= 0 || _newCapacity <= vanilla) return 0;

        var watch = Stopwatch.StartNew();
        var grownBefore = _grown;

        while (true)
        {
            if (_fieldIndex >= _fields.Count)
            {
                if (_classIndex >= _classes.Length) break;
                var spec = _classes[_classIndex++];
                if (!BeginClass(spec, log)) continue;
                continue;   // BeginClass 往 _fields 里加了字段，回到上面处理
            }

            var field = _fields[_fieldIndex++];
            var array = ReadStatic(field);
            if (array == IntPtr.Zero)
            {
                // 静态构造没建它：记下来，维护期再补
                _pending.Add(field);
                continue;
            }
            if (ArrayLength(array) != vanilla) continue;   // 不是这一类（同长度靠 Skip 排除）
            _candidates.Add(field);                        // 这一张就该扩（自检按这份名单数）

            log?.Invoke($"[grow] {_name} {FieldLabel(field)} len={vanilla}→{_newCapacity} ptr=0x{array.ToInt64():x}");
            if (!GrowField(field, array, log)) continue;
            _grown++;
        }

        // 真机自检过：整批一般是几毫秒；冒到几十毫秒就说明某个类的静态构造/扫描很贵，值得单独记一条。
        if (watch.ElapsedMilliseconds >= 20)
            log?.Invoke($"[typetables] {_name} 这批 {watch.ElapsedMilliseconds}ms：扩了 {_grown - grownBefore} 张，" +
                        $"已扫到 {_fields.Count} 张表");
        return _grown - grownBefore;
    }

    /// <summary>诊断用：<c>类名.字段名</c>，扩表流水账里标明是哪一张。</summary>
    private static string FieldLabel(IntPtr field)
    {
        try
        {
            var cls = Meta.ParentOf(field);
            var clsName = cls == IntPtr.Zero ? "?" : IL2CPP.il2cpp_class_get_name_(cls) ?? "?";
            return clsName + "." + (IL2CPP.il2cpp_field_get_name_(field) ?? "?");
        }
        catch { return "?"; }
    }

    /// <summary>换一个类：按名解析 → 跑静态构造 → 收集静态数组字段。</summary>
    private bool BeginClass(TableClass spec, Action<string>? log)
    {
        var cls = FindClass(spec);
        if (cls == IntPtr.Zero)
        {
            log?.Invoke($"[typetables] {_name} 找不到 {spec}，跳过");
            return false;
        }

        // 先跑静态构造：没跑过时 Sets 整类还是 null（此时 mod 已读过/写过越界位置），只能当 pending 等下帧补。
        var watch = Stopwatch.StartNew();
        try { IL2CPP.il2cpp_runtime_class_init(cls); }
        catch (Exception e) { log?.Invoke($"[typetables] {_name} {spec} 静态构造异常（继续扫描）：{e.Message}"); }
        var initMs = watch.ElapsedMilliseconds;

        var before = _fields.Count;
        try { CollectFields(cls, spec, _fields); }
        catch (Exception e)
        {
            log?.Invoke($"[typetables] {_name} 枚举 {spec} 字段异常：{e.Message}");
            return false;
        }
        var scanMs = watch.ElapsedMilliseconds - initMs;
        // 冒到几十毫秒就记一条：静态构造贵（游戏内容没建全）或字段特别多时会看到。
        if (initMs + scanMs >= 20)
            log?.Invoke($"[typetables] {_name} {spec}：静态构造 {initMs}ms，枚举 {scanMs}ms，" +
                        $"{_fields.Count - before} 张表");
        return true;
    }

    private bool GrowField(IntPtr field, IntPtr array, Action<string>? log)
    {
        try
        {
            var grown = GrowArray(array, _newCapacity, _fillWithMode);
            if (grown == IntPtr.Zero) return false;
            WriteStatic(field, grown);
            return true;
        }
        catch (Exception e)
        {
            log?.Invoke($"[typetables] {_name} 成员 {IL2CPP.il2cpp_field_get_name_(field)} 异常：{e.Message}");
            return false;
        }
    }

    /// <summary>扫到时还是 null 的表：建出来就补扩（每帧）。</summary>
    internal int CheckPending(Action<string>? log)
    {
        var vanilla = Vanilla;
            if (_pending.Count == 0 || vanilla <= 0 || _newCapacity <= vanilla) return 0;

        var grown = 0;
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var field = _pending[i];
            var array = ReadStatic(field);
            if (array == IntPtr.Zero) continue;   // 还没建，下一帧再试
            _pending.RemoveAt(i);

            if (ArrayLength(array) != vanilla) continue;
            _candidates.Add(field);
            if (!GrowField(field, array, log)) continue;
            _grown++;
            grown++;
        }
        if (grown > 0) log?.Invoke($"[typetables] {_name} 补扩之后才建出来的表：{grown} 张");
        return grown;
    }

    /// <summary>游戏把表重建回原版长度（切语言、重载资源）：再扩一遍。</summary>
    internal int Watch(Action<string>? log)
    {
        var vanilla = Vanilla;
        if (vanilla <= 0 || _newCapacity <= vanilla) return 0;

        var grown = 0;
        for (var i = 0; i < _fields.Count; i++)
        {
            var field = _fields[i];
            var array = ReadStatic(field);
            if (array == IntPtr.Zero) continue;
            var length = ArrayLength(array);
            if (length >= _newCapacity || length != vanilla) continue;
            if (!GrowField(field, array, log)) continue;
            _grown++;
            grown++;
        }
        if (grown > 0) log?.Invoke($"[typetables] {_name} 被游戏重建后又扩：{grown} 张");
        return grown;
    }

    /// <summary>
    /// 自检/诊断：本种类"该扩的表"里，扩到位 / 还是原版 / 还没建 各几张。
    /// 名单只算扫到时长度 == 原版数、或被补扩过的那些，不含 Main 里长度本来就够大的数组。
    /// </summary>
    internal void Audit(out int total, out int grown, out int stale, out int missing,
                       List<string>? missingNames = null)
    {
        total = grown = stale = missing = 0;
        var vanilla = Vanilla;
        if (vanilla <= 0 || _newCapacity <= vanilla) return;

        for (var i = 0; i < _candidates.Count; i++)
        {
            total++;
            var array = ReadStatic(_candidates[i]);
            if (array == IntPtr.Zero)
            {
                missing++;
                missingNames?.Add(FieldName(_candidates[i]));
                continue;
            }
            var length = ArrayLength(array);
            if (length >= _newCapacity) grown++;
            else if (length == vanilla) stale++;
        }
        // 扫到时还是 null、到现在也没建出来的也算"还没建"
        for (var i = 0; i < _pending.Count; i++)
        {
            total++;
            if (ReadStatic(_pending[i]) != IntPtr.Zero) continue;
            missing++;
            missingNames?.Add(FieldName(_pending[i]));
        }
    }

    private static string FieldName(IntPtr field)
    {
        try { return IL2CPP.il2cpp_field_get_name_(field) ?? "?"; }
        catch { return "?"; }
    }
}
