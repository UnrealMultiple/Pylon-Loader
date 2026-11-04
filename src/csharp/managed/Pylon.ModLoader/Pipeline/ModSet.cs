namespace Pylon.ModLoader;

/// <summary>已加载的 Mod 集合：顺序即加载顺序，位置即 ModIndex。</summary>
internal sealed class ModSet
{
    private readonly List<Mod> _mods = [];
    private readonly Dictionary<string, Mod> _byName = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Mod> Mods => _mods;

    public int Count => _mods.Count;

    /// <summary>登记一个 Mod，返回它在这个集合里的位置（= Mod.Index）。</summary>
    public int Add(Mod mod)
    {
        var index = _mods.Count;
        _mods.Add(mod);
        if (mod.Name.Length > 0) _byName.TryAdd(mod.Name, mod);
        return index;
    }

    public Mod? Find(string? name) =>
        !string.IsNullOrEmpty(name) && _byName.TryGetValue(name, out var mod) ? mod : null;
}
