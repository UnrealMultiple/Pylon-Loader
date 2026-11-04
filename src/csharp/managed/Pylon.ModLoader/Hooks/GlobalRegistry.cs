namespace Pylon.ModLoader;

/// <summary>
/// 非实体 Global（GlobalBuff / GlobalTile）的注册表：模板按注册序排队，
/// 钩子索引在注册期算好；分派时按 <see cref="HookId"/> 直接取数组。
/// </summary>
internal sealed class GlobalRegistry<TGlobal> where TGlobal : class
{
    private static readonly int HookCount = Enum.GetValues<HookId>().Length;

    private readonly List<TGlobal> _templates = [];
    private readonly List<TGlobal> _empty = [];
    private readonly Type? _baseType;

    private List<TGlobal>?[]? _byHook;

    /// <param name="baseType">非 null 时按这个基类精确判定覆写；null 表示沿用 ModType.Plan。</param>
    public GlobalRegistry(Type? baseType = null) => _baseType = baseType;

    public IReadOnlyList<TGlobal> All => _templates;

    public void Register(TGlobal global)
    {
        if (global is null || _templates.Contains(global)) return;

        _templates.Add(global);
        _byHook = null;
    }

    /// <summary>覆写了这个钩子的所有模板（按注册序）。</summary>
    public List<TGlobal> For(HookId hook) =>
        (_byHook ??= BuildTable())[(int)hook] ?? _empty;

    public bool AnyWith(params ReadOnlySpan<HookId> hooks)
    {
        var table = _byHook ??= BuildTable();
        foreach (var hook in hooks)
        {
            if (table[(int)hook] is not null) return true;
        }
        return false;
    }

    private List<TGlobal>?[] BuildTable()
    {
        var table = new List<TGlobal>?[HookCount];
        Span<HookId> hooks = stackalloc HookId[HookCount];

        foreach (var global in _templates)
        {
            var mask = PlanOf(global);
            if (mask.IsEmpty) continue;

            var count = mask.Collect(hooks);
            for (var i = 0; i < count; i++)
            {
                var slot = (int)hooks[i];
                (table[slot] ??= []).Add(global);
            }
        }

        return table;
    }

    private HookMask PlanOf(TGlobal global) =>
        _baseType is null ? (global as ModType)?.Plan ?? HookMask.Empty : HookScan.Of(global.GetType(), _baseType);
}
