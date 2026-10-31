namespace Pylon.ModLoader;

/// <summary>
/// 非实体 Global（GlobalBuff / GlobalTile）的注册表：模板按注册序排队，
/// 方法索引在注册期算好，分派时只取「覆写了这个方法」的列表。
/// </summary>
internal sealed class GlobalRegistry<TGlobal> where TGlobal : class
{
    private readonly List<TGlobal> _templates = [];
    private readonly List<TGlobal> _empty = [];
    private readonly Dictionary<Type, OverridePlan> _plans = [];
    private readonly Type? _baseType;

    private Dictionary<string, List<TGlobal>>? _byMethod;

    /// <param name="baseType">非 null 时按这个基类精确判定覆写；null 表示沿用 ModType.Plan。</param>
    public GlobalRegistry(Type? baseType = null) => _baseType = baseType;

    public IReadOnlyList<TGlobal> All => _templates;

    public void Register(TGlobal global)
    {
        if (global is null || _templates.Contains(global)) return;

        _templates.Add(global);
        _byMethod = null;
    }

    /// <summary>覆写了这个方法的所有模板（按注册序）。</summary>
    public List<TGlobal> For(string method)
    {
        var table = _byMethod ??= BuildMethodTable();
        return table.TryGetValue(method, out var list) ? list : _empty;
    }

    public bool AnyWith(params string[] methods)
    {
        var table = _byMethod ??= BuildMethodTable();
        foreach (var method in methods)
        {
            if (table.ContainsKey(method)) return true;
        }
        return false;
    }

    private Dictionary<string, List<TGlobal>> BuildMethodTable()
    {
        var table = new Dictionary<string, List<TGlobal>>(StringComparer.Ordinal);
        foreach (var global in _templates)
        {
            if (PlanOf(global) is not { } plan) continue;

            foreach (var name in plan.Names)
            {
                if (!table.TryGetValue(name, out var list))
                {
                    list = [];
                    table[name] = list;
                }
                if (!list.Contains(global)) list.Add(global);
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
