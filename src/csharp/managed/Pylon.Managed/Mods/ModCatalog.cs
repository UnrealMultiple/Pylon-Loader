using System.Text.Json;
using Pylon.Managed.Hosting;

namespace Pylon.Managed.Mods;

/// <summary>Mod 目录扫描、选择文件与依赖排序。</summary>
internal sealed class ModCatalog
{
    private ModCatalog(IReadOnlyList<ModEntry> ordered) => Ordered = ordered;

    /// <summary>依赖优先的加载顺序（含已停用项，由调用方过滤）。</summary>
    public IReadOnlyList<ModEntry> Ordered { get; }

    public static ModCatalog Discover(IReadOnlyList<string> roots)
    {
        var entries = new List<ModEntry>();
        if (roots.Count > 0)
        {
            foreach (var root in roots) Collect(root, entries);

            // 选择文件永远放用户 Mods 目录（roots[0]）；包解包缓存读同一份。
            var selectionRoot = roots.FirstOrDefault(Directory.Exists) ?? roots[0];
            ApplySelection(selectionRoot, entries);
        }

        LogSummary(roots, entries);
        return new ModCatalog(SortByDependencies(entries));
    }

    private static void Collect(string root, List<ModEntry> entries)
    {
        if (!Directory.Exists(root))
        {
            HostLog.Write($"Mod 目录不存在：{root}");
            return;
        }

        foreach (var directory in Directory.GetDirectories(root))
        {
            if (Path.GetFileName(directory) is ".pylon") continue;
            if (FindModAssembly(directory) is { } assemblyPath)
                entries.Add(ModEntry.Discover(assemblyPath, Path.GetFileName(directory)));
        }

        // 松散放在根目录下的 dll 也算一个 Mod。
        foreach (var assemblyPath in Directory.GetFiles(root, "*.dll"))
            entries.Add(ModEntry.Discover(assemblyPath, Path.GetFileNameWithoutExtension(assemblyPath)));
    }

    /// <summary>启动器写下的勾选结果（Mods/.pylon/selection.json），优先于各 Mod 清单的 enabled。</summary>
    private static void ApplySelection(string root, List<ModEntry> entries)
    {
        var path = Path.Combine(root, ".pylon", "selection.json");
        if (!File.Exists(path)) return;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("enabled", out var enabled) ||
                enabled.ValueKind is not JsonValueKind.Object)
                return;

            var byId = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in enabled.EnumerateObject())
            {
                if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    byId[property.Name] = property.Value.GetBoolean();
            }

            var applied = 0;
            foreach (var entry in entries)
            {
                if (!byId.TryGetValue(entry.Id, out var value)) continue;
                entry.Enabled = value;
                applied++;
            }
            HostLog.Write($"Mod 选择：{path}（覆盖 {applied} 个）");
        }
        catch (Exception e)
        {
            // 选择文件坏掉不能拖垮启动：按各 Mod 清单的 enabled 继续。
            HostLog.Write($"Mod 选择文件解析失败（忽略）：{e.Message}");
        }
    }

    /// <summary>在 Mod 目录里找主程序集。</summary>
    private static string? FindModAssembly(string directory)
    {
        var preferred = Path.Combine(directory, "mod.dll");
        if (File.Exists(preferred)) return preferred;

        var named = Path.Combine(directory, Path.GetFileName(directory) + ".dll");
        if (File.Exists(named)) return named;

        var libraries = Directory.GetFiles(directory, "*.dll");
        Array.Sort(libraries, StringComparer.OrdinalIgnoreCase);
        switch (libraries.Length)
        {
            case 0: return null;
            case 1: return libraries[0];
        }

        HostLog.Write(
            $"{Path.GetFileName(directory)} 里有 {libraries.Length} 个 dll，无法判断哪个是 Mod 本体：" +
            $"{string.Join(", ", libraries.Select(Path.GetFileName))}；" +
            "请把主程序集命名为 mod.dll 或与目录同名，私有依赖放进 lib/");
        return null;
    }

    /// <summary>依赖优先排序；缺失依赖只记日志，成环则跳过环上节点。</summary>
    private static List<ModEntry> SortByDependencies(List<ModEntry> entries)
    {
        var byId = new Dictionary<string, ModEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) byId[entry.Id] = entry;

        var ordered = new List<ModEntry>(entries.Count);
        var state = new Dictionary<string, VisitState>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) Visit(entry);
        return ordered;

        void Visit(ModEntry entry)
        {
            switch (state.GetValueOrDefault(entry.Id))
            {
                case VisitState.Visiting:
                    HostLog.Write($"依赖成环：跳过 {entry.Id}");
                    return;
                case VisitState.Done:
                    return;
            }

            state[entry.Id] = VisitState.Visiting;
            foreach (var dependencyId in entry.Dependencies)
            {
                if (byId.TryGetValue(dependencyId, out var dependency)) Visit(dependency);
                else HostLog.Write($"{entry.Id} 依赖的 {dependencyId} 不存在");
            }
            state[entry.Id] = VisitState.Done;
            ordered.Add(entry);
        }
    }

    private static void LogSummary(IReadOnlyList<string> roots, List<ModEntry> entries)
    {
        HostLog.Write($"Mod 扫描：{string.Join(" + ", roots)} → {entries.Count} 个" +
                      $"（宿主提供 {HostAssemblies.Count} 个程序集，Mod 不可遮蔽）");
        foreach (var entry in entries) HostLog.Write(Describe(entry));
    }

    private static string Describe(ModEntry entry)
    {
        var context = entry.LoadContext is { } loadContext ? $" [{loadContext.Name}]" : "";
        var note = entry.Error is { } error
            ? $"（停用：{error}）"
            : entry.Enabled ? "" : "（enabled=false）";
        return $"  · {entry.Id} v{entry.Version}{context}{note}";
    }

    private enum VisitState
    {
        None,
        Visiting,
        Done,
    }
}
