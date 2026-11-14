using System.IO;
using System.Text.Json;

namespace Pylon.ModLoader;

/// <summary>
/// 联机策略开关，落在 .pylon/net.json。默认从严：两端内容指纹不一致就拒绝进世界——
/// 放行的代价是配方 / 掉落 / 物品编号整体错位，轻则表现异常重则存档损坏；
/// 排查两端差异时可以临时关掉，看完对账日志再开回来。
/// </summary>
internal static class NetPolicy
{
    private static bool _loaded;
    private static bool _requireMatch = true;
    private static bool _hostCheats = true;

    /// <summary>内容指纹不一致时拒绝进世界（客户端自踢与服务端踢人各自尊重本端开关）。</summary>
    internal static bool RequireMatch
    {
        get { EnsureLoaded(); return _requireMatch; }
        set { EnsureLoaded(); _requireMatch = value; Save(); }
    }

    /// <summary>本机作为服务器时是否允许作弊，随 welcome 包广播给客户端。</summary>
    internal static bool HostCheats
    {
        get { EnsureLoaded(); return _hostCheats; }
        set { EnsureLoaded(); _hostCheats = value; Save(); }
    }

    /// <summary>随 welcome 包下发的规则位：bit0 = 允许作弊。</summary>
    internal static byte HostRules => (byte)(_hostCheats ? RuleCheats : 0);

    internal const byte RuleCheats = 1;

    private static string? SettingsPath
    {
        get
        {
            var root = Framework.HostDirectory;
            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, ".pylon", "net.json");
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            var path = SettingsPath;
            if (path == null || !File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (TryBool(doc.RootElement, "requireMatch", out var match)) _requireMatch = match;
            if (TryBool(doc.RootElement, "hostCheats", out var cheats)) _hostCheats = cheats;
        }
        catch (Exception e)
        {
            Safe.Warn("读取联机设置失败（用默认值）：" + e.Message);
        }
    }

    private static void Save()
    {
        try
        {
            var path = SettingsPath;
            if (path == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllText(path, JsonSerializer.Serialize(new { requireMatch = _requireMatch, hostCheats = _hostCheats }));
        }
        catch (Exception e)
        {
            Safe.Warn("保存联机设置失败：" + e.Message);
        }
    }

    private static bool TryBool(JsonElement element, string name, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(name, out var property)) return false;
        if (property.ValueKind == JsonValueKind.True) { value = true; return true; }
        if (property.ValueKind == JsonValueKind.False) { value = false; return true; }
        return false;
    }
}
