namespace Pylon.ModLoader;

/// <summary>跨 Mod 查找：按 id 取别的 Mod 实例（可选依赖用 TryGetMod / HasMod）。</summary>
public static class ModLoader
{
    private static readonly Dictionary<string, Mod> Registry = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<Mod> Ordered = [];

    /// <summary>Mod 加载时由管线登记；顺序即加载顺序。</summary>
    internal static void Register(Mod mod)
    {
        if (mod is null || string.IsNullOrEmpty(mod.Name) || Registry.ContainsKey(mod.Name)) return;

        Registry[mod.Name] = mod;
        Ordered.Add(mod);
    }

    /// <summary>按 id 找 Mod。</summary>
    public static bool TryGetMod(string name, out Mod? result) => (result = Find(name)) is not null;

    /// <summary>按 id 找 Mod；没有就抛（可选依赖请用 <see cref="TryGetMod"/> / <see cref="HasMod"/>）。</summary>
    public static Mod GetMod(string name) =>
        Find(name) ?? throw new InvalidOperationException(
            $"ModLoader.GetMod: 没有加载 '{name}' 这个 Mod（可选依赖请用 TryGetMod）");

    public static bool HasMod(string name) => Find(name) is not null;

    /// <summary>全部已加载的 Mod，按加载顺序。</summary>
    public static IReadOnlyList<Mod> Mods => Ordered;

    private static Mod? Find(string name) =>
        !string.IsNullOrEmpty(name) && Registry.TryGetValue(name, out var mod) ? mod : null;
}
