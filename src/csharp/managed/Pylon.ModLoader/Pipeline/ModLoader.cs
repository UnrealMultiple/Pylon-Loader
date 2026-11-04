namespace Pylon.ModLoader;

/// <summary>跨 Mod 查找：按 id 取别的 Mod 实例（可选依赖用 TryGetMod / HasMod）。</summary>
public static class ModLoader
{
    /// <summary>全部已加载的 Mod，按加载顺序。</summary>
    public static IReadOnlyList<Mod> Mods => LoaderHost.Current.Mods.Mods;

    public static bool TryGetMod(string name, out Mod? result)
    {
        result = LoaderHost.Current.Mods.Find(name);
        return result is not null;
    }

    public static Mod GetMod(string name) =>
        LoaderHost.Current.Mods.Find(name) ?? throw new InvalidOperationException(
            $"ModLoader.GetMod: 没有加载 '{name}' 这个 Mod（可选依赖请用 TryGetMod）");

    public static bool HasMod(string name) => LoaderHost.Current.Mods.Find(name) is not null;
}
