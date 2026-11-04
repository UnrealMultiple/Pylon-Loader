using System.Runtime.CompilerServices;

namespace Pylon.ModLoader;

/// <summary>
/// Global*（作用于游戏实体的全局钩子）的注册表。
/// 模板按注册序排队；钩子索引在注册期算好，分派时按 <see cref="HookId"/> 取数组。
/// 只有声明了 InstancePerEntity 或 AppliesToEntity 时才为实体维护实例列表，否则直接用模板表（零分配）。
/// </summary>
internal sealed class EntityGlobalRegistry<TGlobal, TEntity>
    where TGlobal : class
    where TEntity : class
{
    private static readonly int HookCount = Enum.GetValues<HookId>().Length;

    private sealed class HookEntry
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
    private readonly Type? _baseType;

    private ConditionalWeakTable<List<TGlobal>, Dictionary<int, List<TGlobal>>> _filtered = new();

    private HookEntry?[]? _byHook;
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
        if (_isPerEntity(global) || PlanOf(global).Has(HookId.AppliesToEntity)) _needsPerEntity = true;

        _byHook = null;
        _attached.Clear();
        _filtered = new ConditionalWeakTable<List<TGlobal>, Dictionary<int, List<TGlobal>>>();
    }

    /// <summary>覆写了这个钩子的所有模板（不含实体过滤；钩子目标还没实体时用）。</summary>
    public List<TGlobal> TemplatesFor(HookId hook) => EntryOf(hook)?.Ordered ?? _empty;

    /// <summary>这个实体的一套实例（没有实体级需求时就是模板表本身）。</summary>
    public List<TGlobal> Of(TEntity? entity) =>
        !_needsPerEntity || entity is null ? _templates : Attached(entity);

    /// <summary>这个钩子在这个实体上要跑的 Global（结果按实体缓存）。</summary>
    public List<TGlobal> For(TEntity? entity, HookId hook)
    {
        var entry = EntryOf(hook);
        if (entry is null || entry.Ordered.Count == 0) return _empty;
        if (!_needsPerEntity) return entity is null ? _empty : entry.Ordered;
        if (entity is null) return _empty;

        var attached = Attached(entity);
        if (attached.Count == 0) return _empty;

        var table = _filtered.GetOrCreateValue(attached);
        if (table.TryGetValue((int)hook, out var filtered)) return filtered;

        filtered = new List<TGlobal>(attached.Count);
        foreach (var global in attached)
        {
            if (entry.Set.Contains(global)) filtered.Add(global);
        }

        table[(int)hook] = filtered;
        return filtered;
    }

    /// <summary>任一 Global 覆写了这些钩子之一（装 hook 前的快速门）。</summary>
    public bool AnyWith(params ReadOnlySpan<HookId> hooks)
    {
        var table = _byHook ??= BuildTable();
        foreach (var hook in hooks)
        {
            if (table[(int)hook] is not null) return true;
        }
        return false;
    }

    /// <summary>这个实体上有没有 Global 要跑这些钩子之一。</summary>
    public bool AnyFor(TEntity? entity, params ReadOnlySpan<HookId> hooks)
    {
        foreach (var hook in hooks)
        {
            if (EntryOf(hook) is not null && Of(entity).Count > 0) return true;
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
            if (PlanOf(template).Has(HookId.AppliesToEntity) && !_appliesTo(template, entity, lateInstantiation)) continue;
            list.Add(_isPerEntity(template) ? _createInstance(template, entity) : template);
        }
        return list;
    }

    private HookEntry? EntryOf(HookId hook) => (_byHook ??= BuildTable())[(int)hook];

    private HookEntry?[] BuildTable()
    {
        var table = new HookEntry?[HookCount];
        Span<HookId> hooks = stackalloc HookId[HookCount];

        foreach (var global in _templates)
        {
            var mask = PlanOf(global);
            if (mask.IsEmpty) continue;

            var count = mask.Collect(hooks);
            for (var i = 0; i < count; i++)
            {
                var slot = (int)hooks[i];
                var entry = table[slot] ??= new HookEntry();
                if (entry.Set.Add(global)) entry.Ordered.Add(global);
            }
        }

        return table;
    }

    private HookMask PlanOf(TGlobal global) =>
        _baseType is null ? (global as ModType)?.Plan ?? HookMask.Empty : HookScan.Of(global.GetType(), _baseType);
}
