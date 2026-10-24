using System.Collections.Frozen;

namespace Pylon.Managed.Hosting;

/// <summary>哪些程序集由宿主提供——判据是 TPA 本身，不是硬编码的前缀表。</summary>
internal static class HostAssemblies
{
    private static readonly FrozenSet<string> Names = Build();

    public static int Count => Names.Count;

    /// <summary>宿主已提供的程序集名（大小写不敏感）。</summary>
    public static bool Contains(string simpleName) => Names.Contains(simpleName);

    private static FrozenSet<string> Build()
    {
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string tpa) return FrozenSet<string>.Empty;

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Length > 0) names.Add(name);
        }
        return names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
}
