using System.Text.Json;

namespace Pylon.Managed.Mods;

/// <summary>manifest.json 的元数据；代码坐标由反射得到。</summary>
internal sealed record ModManifest(string Id, string Version, bool Enabled, IReadOnlyList<string> Dependencies)
{
    public static ModManifest Default(string fallbackId) => new(fallbackId, "0.0.0", true, []);

    /// <summary>读取清单；文件缺失或字段非法时退回默认值并给出 error。</summary>
    public static ModManifest Read(string path, string fallbackId, out string? error)
    {
        error = null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return new ModManifest(
                GetString(root, "id") ?? fallbackId,
                GetString(root, "version") ?? "0.0.0",
                root.TryGetProperty("enabled", out var enabled) ? enabled.ValueKind is not JsonValueKind.False : true,
                GetDependencies(root));
        }
        catch (Exception e)
        {
            error = "manifest.json 解析失败：" + e.Message;
            return Default(fallbackId);
        }
    }

    private static string? GetString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.String
            ? value.GetString()
            : null;

    private static string[] GetDependencies(JsonElement root)
    {
        if (!root.TryGetProperty("dependencies", out var dependencies) || dependencies.ValueKind is not JsonValueKind.Array)
            return [];

        return dependencies.EnumerateArray()
            .Where(dependency => dependency.ValueKind is JsonValueKind.String)
            .Select(dependency => dependency.GetString()!)
            .ToArray();
    }
}
