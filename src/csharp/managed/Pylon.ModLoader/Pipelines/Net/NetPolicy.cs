using System.Text.Json;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 联机策略开关，落在 .pylon/net.json，多人托管界面（NetHostMenu）的三行开关直读直写。
/// 模组同步（旧配置键 requireMatch）：客户端的模组内容指纹必须与本机一致，否则拒绝进世界——
/// 放行的代价是配方 / 掉落 / 物品编号整体错位，轻则表现异常重则存档损坏，关闭等于主机自担风险。
/// 允许材质包：客户端可以自带纹理，纹理指纹不参与裁决；关闭则纹理也必须与本机一致。
/// 允许作弊：客户端的作弊类 Mod 功能据此让步（NetHandshake.CheatsBlocked）。
/// 发送指纹：客户端侧的独立开关（加入服务器界面 NetJoinMenu），关闭后不向服务器上报指纹包，
/// 服务器无从核对，本端也放弃内容 / 纹理自检，任何 Pylon 服都能直接进入，错位风险自担。
/// 规则位随欢迎包广播：bit0 = 允许作弊，bit1 = 客户端可自带纹理，bit2 = 客户端模组可不同。
/// 旧构建的对端只发 bit0，此时客户端把缺省位当"需一致"自裁；对方服务器无论如何都会按它自己的策略把关。
/// </summary>
internal static class NetPolicy
{
    private static bool _loaded;
    private static bool _syncMods = true;
    private static bool _allowTextures = true;
    private static bool _hostCheats = true;
    private static bool _sendFingerprint = true;

    internal static bool SyncMods
    {
        get { EnsureLoaded(); return _syncMods; }
        set { EnsureLoaded(); _syncMods = value; Save(); }
    }

    internal static bool AllowTextures
    {
        get { EnsureLoaded(); return _allowTextures; }
        set { EnsureLoaded(); _allowTextures = value; Save(); }
    }

    internal static bool HostCheats
    {
        get { EnsureLoaded(); return _hostCheats; }
        set { EnsureLoaded(); _hostCheats = value; Save(); }
    }

    internal static bool SendFingerprint
    {
        get { EnsureLoaded(); return _sendFingerprint; }
        set { EnsureLoaded(); _sendFingerprint = value; Save(); }
    }

    internal const byte RuleCheats = 1;
    internal const byte RuleOwnTextures = 2;
    internal const byte RuleOwnMods = 4;

    internal static byte HostRules => (byte)((_hostCheats ? RuleCheats : 0) |
                                             (_allowTextures ? RuleOwnTextures : 0) |
                                             (_syncMods ? 0 : RuleOwnMods));

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
            var root = doc.RootElement;
            if (TryBool(root, "syncMods", out var sync) || TryBool(root, "requireMatch", out sync)) _syncMods = sync;
            if (TryBool(root, "allowTextures", out var textures)) _allowTextures = textures;
            if (TryBool(root, "hostCheats", out var cheats)) _hostCheats = cheats;
            if (TryBool(root, "sendFingerprint", out var send)) _sendFingerprint = send;
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
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                syncMods = _syncMods,
                allowTextures = _allowTextures,
                hostCheats = _hostCheats,
                sendFingerprint = _sendFingerprint,
            }));
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
