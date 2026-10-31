using System.Runtime.CompilerServices;

namespace Pylon.ModLoader;

/// <summary>
/// Global*（作用于游戏实体的全局钩子）的通用注册表。
/// 模板按注册序排队；分派前先用注册期算好的覆写集合过滤，避免对空钩子付出调用成本。
/// 只有声明了 <c>InstancePerEntity</c> 或 <c>AppliesToEntity</c> 时才为每个实体维护一份列表，
/// 否则实体列表就是模板列表本身（零分配）。
/// </summary>
internal sealed class EntityGlobalRegistry<TGlobal, TEntity>
    where TGlobal : class
    where TEntity : class
{
    private sealed class MethodEntry
    {
        public readonly List<TGlobal> Ordered = [];
        public readonly HashSet<TGlobal> Set = [];
    }

    private readonly Func<TGlobal, bool> _isPerEntity;
    private readonly Func<TGlobal, TEntity, bool, bool> _appliesTo;
    private readonly Func<TGlobal, TEntity, TGlobal> _createInstance;
    private readonly Func<TEntity?, long> _address;
    private readonly bool _defaultLateInstantiation;

    private readonly List<TGlobal> _templates = [];
    private readonly List<TGlobal> _empty = [];
    private readonly Dictionary<long, List<TGlobal>> _attached = [];
    private readonly Dictionary<Type, OverridePlan> _plans = [];
    private readonly Type? _baseType;

    private ConditionalWeakTable<List<TGlobal>, Dictionary<string, List<TGlobal>>> _filtered = new();

    private Dictionary<string, MethodEntry>? _byMethod;
    private bool _needsPerEntity;

    /// <param name="baseType">
    /// 非 null 时按这个基类精确判定覆写（如 typeof(GlobalNpc)）；
    /// null 表示沿用 ModType.Plan 的判定（GlobalItem / GlobalProjectile 当前行为）。
    /// </param>
    public EntityGlobalRegistry(
        Func<TGlobal, bool> isPerEntity,
        Func<TGlobal, TEntity, bool, bool> appliesTo,
        Func<TGlobal, TEntity, TGlobal> createInstance,
        Func<TEntity?, long> address,
        bool defaultLateInstantiation,
        Type? baseType = null)
    {
        _isPerEntity = isPerEntity;
        _appliesTo = appliesTo;
        _createInstance = createInstance;
        _address = address;
        _defaultLateInstantiation = defaultLateInstantiation;
        _baseType = baseType;
    }

    public IReadOnlyList<TGlobal> All => _templates;

    public void Register(TGlobal global)
    {
        if (global is null || _templates.Contains(global)) return;

        _templates.Add(global);
        if (_isPerEntity(global) || (PlanOf(global)?.Has("AppliesToEntity") ?? false)) _needsPerEntity = true;

        _byMethod = null;
        _attached.Clear();
        _filtered = new ConditionalWeakTable<List<TGlobal>, Dictionary<string, List<TGlobal>>>();
    }

    /// <summary>覆写了这个方法的所有模板（不含实体过滤；钩子目标还没实体时用）。</summary>
    public List<TGlobal> TemplatesFor(string method) => EntryOf(method)?.Ordered ?? _empty;

    /// <summary>这个实体的一套实例（没有实体级需求时就是模板表本身）。</summary>
    public List<TGlobal> Of(TEntity? entity) =>
        !_needsPerEntity || entity is null ? _templates : Attached(entity);

    /// <summary>这个方法在这个实体上要跑的 Global（按覆写与实体过滤，结果按实体缓存）。</summary>
    public List<TGlobal> For(TEntity? entity, string method)
    {
        var entry = EntryOf(method);
        if (entry is null || entry.Ordered.Count == 0) return _empty;
        if (!_needsPerEntity) return entity is null ? _empty : entry.Ordered;
        if (entity is null) return _empty;

        var attached = Attached(entity);
        if (attached.Count == 0) return _empty;

        var table = _filtered.GetOrCreateValue(attached);
        if (table.TryGetValue(method, out var filtered)) return filtered;

        filtered = new List<TGlobal>(attached.Count);
        foreach (var global in attached)
        {
            if (entry.Set.Contains(global)) filtered.Add(global);
        }

        table[method] = filtered;
        return filtered;
    }

    /// <summary>任一 Global 覆写了这些方法之一（装 hook 前的快速门）。</summary>
    public bool AnyWith(params string[] methods)
    {
        var table = _byMethod ??= BuildMethodTable();
        foreach (var method in methods)
        {
            if (table.ContainsKey(method)) return true;
        }
        return false;
    }

    /// <summary>这个实体上有没有 Global 要跑这些方法之一。</summary>
    public bool AnyFor(TEntity? entity, params string[] methods)
    {
        foreach (var method in methods)
        {
            if (EntryOf(method) is not null && Of(entity).Count > 0) return true;
        }
        return false;
    }

    /// <summary>SetDefaults 时重建实体级列表（lateInstantiation 语义由调用方给）。</summary>
    public void Attach(TEntity? entity, bool lateInstantiation)
    {
        if (!_needsPerEntity || entity is null) return;

        var address = _address(entity);
        if (address == 0) return;
        _attached[address] = Build(entity, lateInstantiation);
    }

    private List<TGlobal> Attached(TEntity entity)
    {
        var address = _address(entity);
        if (address == 0) return _templates;
        if (_attached.TryGetValue(address, out var cached)) return cached;

        var list = Build(entity, _defaultLateInstantiation);
        _attached[address] = list;
        return list;
    }

    private List<TGlobal> Build(TEntity entity, bool lateInstantiation)
    {
        var list = new List<TGlobal>(_templates.Count);
        foreach (var template in _templates)
        {
            if ((PlanOf(template)?.Has("AppliesToEntity") ?? false) && !_appliesTo(template, entity, lateInstantiation)) continue;
            list.Add(_isPerEntity(template) ? _createInstance(template, entity) : template);
        }
        return list;
    }

    private MethodEntry? EntryOf(string method)
    {
        var table = _byMethod ??= BuildMethodTable();
        return table.TryGetValue(method, out var entry) ? entry : null;
    }

    private Dictionary<string, MethodEntry> BuildMethodTable()
    {
        var table = new Dictionary<string, MethodEntry>(StringComparer.Ordinal);
        foreach (var global in _templates)
        {
            if (PlanOf(global) is not { } plan) continue;
            foreach (var name in plan.Names)
            {
                if (!table.TryGetValue(name, out var entry))
                {
                    entry = new MethodEntry();
                    table[name] = entry;
                }
                if (entry.Set.Add(global)) entry.Ordered.Add(global);
            }
        }
        return table;
    }

    private OverridePlan? PlanOf(TGlobal global)
    {
        if (_baseType is null) return (global as ModType)?.Plan;

        var type = global.GetType();
        if (_plans.TryGetValue(type, out var plan)) return plan;

        plan = OverridePlan.Build(type, _baseType);
        _plans[type] = plan;
        return plan;
    }
}
