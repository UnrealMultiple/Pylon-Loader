using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 配置的多人同步：<see cref="ConfigScope.ServerSide"/> 的配置由服务器说了算。
/// 服务器在把世界数据（原版消息 7）发给客户端的那一刻把自己的值推过去；客户端连上就照用，
/// 离开世界时把本机原值写回——否则「在服务器上被改过」会留在单机存档里。
/// 客户端改值只发请求（整份值），服务器用 <see cref="ModConfig.AcceptClientChanges"/> 裁决。
/// 载荷全是标量（键名 + 值成对），直接走 <see cref="NetCodec"/>，不需要额外的打包格式。
/// </summary>
internal sealed class ConfigNetPipeline : ILoaderModule
{
    private const byte ActionSync = 0, ActionAsk = 1, ActionReply = 2;

    /// <summary>原版「世界数据」消息号：新客户端连上时服务器会发它，我们搭这趟车。</summary>
    private const int WorldData = 7;

    public static ConfigNetPipeline Instance { get; } = new();

    public string Name => "confignet";

    private static IModLog Log => LoaderHost.Current.Log;

    private NetPipeline _net = null!;
    private byte _kind;

    /// <summary>被服务器改过的配置 → 它们在本机的原值（离开世界时写回）。</summary>
    private readonly Dictionary<ModConfigEntry, Dictionary<string, object>> _applied = [];

    public bool Install()
    {
        _net = NetPipeline.Instance;
        _kind = _net.OpenChannel(Receive);

        On.Terraria.NetMessage.SendData += SendData;
        On.Terraria.Main.Update += CheckLeave;

        Log.Info("[confignet] 已挂（ServerSide 配置：连上收服务器值 / 离开写回本机值 / 改值走请求）");
        return true;
    }

    // ===================== 服务器 → 客户端 =====================

    private void SendData(On.Terraria.NetMessage.orig_SendData orig, int msgType, int remoteClient, int ignoreClient,
                          Terraria.Localization.NetworkText text, int number, float number2, float number3,
                          float number4, int number5, int number6, int number7)
    {
        orig(msgType, remoteClient, ignoreClient, text, number, number2, number3, number4, number5, number6, number7);

        if (msgType != WorldData) return;
        if (Main.netMode != 2 || remoteClient < 0 || remoteClient >= 255) return;

        var entries = ConfigPipeline.Instance.List;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Instance.Scope != ConfigScope.ServerSide) continue;

            SendSync(entry, remoteClient);
        }
    }

    private void SendSync(ModConfigEntry entry, int toClient)
    {
        var values = new List<object> { ActionSync, entry.Mod.Name, entry.Name };
        Pack(entry, values);
        _net.SendValues(_kind, values, toClient, -1);
    }

    private static void Pack(ModConfigEntry entry, List<object> values)
    {
        var options = entry.Options;
        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            if (!ConfigPipeline.HasValue(option)) continue;

            values.Add(option.Key);
            values.Add(entry.Instance.GetValue(option.Key));
        }
    }

    // ===================== 收包 =====================

    private void Receive(Il2CppSystem.IO.BinaryReader reader, int from)
    {
        var values = NetCodec.ReadValues(reader);
        if (values.Length < 3) return;

        var action = values[0] as byte? ?? 255;
        if (Find(values[1] as string ?? "", values[2] as string ?? "") is not { } entry) return;

        switch (action)
        {
            case ActionSync when Main.netMode == 1:
                Apply(entry, values, 3);
                break;

            case ActionAsk when Main.netMode == 2:
                Ask(entry, from, values);
                break;

            case ActionReply when Main.netMode == 1:
                Reply(entry, values);
                break;
        }
    }

    /// <summary>客户端收服务器的值：先记下本机原值（只记第一次），再写进去。</summary>
    private void Apply(ModConfigEntry entry, object[] values, int start)
    {
        if (!_applied.ContainsKey(entry)) _applied[entry] = Snapshot(entry);

        WritePairs(entry, values, start);

        try
        {
            entry.Instance.OnApply();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[confignet] {entry.Name}.OnApply 异常：{Safe.Describe(e)}");
        }
    }

    /// <summary>服务器收到客户端的改值请求：交给作者裁决，过了就存盘、更新、回信。</summary>
    private void Ask(ModConfigEntry entry, int from, object[] values)
    {
        if (entry.Instance.Scope != ConfigScope.ServerSide) return;

        var requested = ReadPairs(values, 3);
        if (requested.Count == 0) return;

        var message = "";
        var accepted = false;

        try
        {
            accepted = entry.Instance.AcceptClientChanges(from, requested, ref message);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[confignet] {entry.Name}.AcceptClientChanges 异常：{Safe.Describe(e)}");
        }

        if (accepted) ApplyRequested(entry, requested);

        Reply(entry, from, accepted, message);
    }

    private static void ApplyRequested(ModConfigEntry entry, Dictionary<string, object> requested)
    {
        foreach (var pair in requested)
        {
            var option = FindOption(entry, pair.Key);
            if (option is null || !ConfigPipeline.Coerce(option, pair.Value, out var value)) continue;

            entry.Instance.__SetValueRaw(pair.Key, value);
        }

        ConfigPipeline.Instance.Save(entry);
        entry.Instance.OnApply();
    }

    private void Reply(ModConfigEntry entry, int toClient, bool success, string message)
    {
        var values = new List<object> { ActionReply, success, message ?? "", entry.Mod.Name, entry.Name };

        if (success) Pack(entry, values);
        _net.SendValues(_kind, values, toClient, -1);
    }

    /// <summary>客户端收裁决：成功就用服务器的值（服务器才是权威），失败把原因说出来。</summary>
    private void Reply(ModConfigEntry entry, object[] values)
    {
        if (values.Length < 5) return;

        var success = values[1] is bool ok && ok;
        var message = values[2] as string ?? "";

        if (success) Apply(entry, values, 5);

        if (message.Length > 0) Main.NewText(message, 255, 240, 20);
        else if (!success) Main.NewText($"[config] {entry.Mod.Name}/{entry.Name} 的改动被服务器拒绝", 255, 120, 120);
    }

    // ===================== 客户端请求 =====================

    /// <summary>客户端改了一份 ServerSide 配置：把整份值当请求发出去（服务器按需接受/拒绝）。</summary>
    internal void Request(ModConfigEntry entry)
    {
        if (Main.netMode != 1) return;

        var values = new List<object> { ActionAsk, entry.Mod.Name, entry.Name };
        Pack(entry, values);
        _net.SendValues(_kind, values, -1, -1);
    }

    /// <summary>离开世界：把服务器改过的值写回本机原值。</summary>
    internal void Revert()
    {
        if (_applied.Count == 0) return;

        foreach (var pair in _applied)
        {
            foreach (var value in pair.Value)
            {
                pair.Key.Instance.__SetValueRaw(value.Key, value.Value);
            }

            try
            {
                pair.Key.Instance.OnApply();
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[confignet] {pair.Key.Name}.OnApply 异常：{Safe.Describe(e)}");
            }
        }

        _applied.Clear();
    }

    /// <summary>
    /// 离开世界的判定：客户端的 netMode 从 1（连着）掉回 0（单机）就是断线或退世界那一刻。
    /// 挂在 <c>Main.Update</c> 上比挂 Netplay 的循环钩子稳——后者在专用线程，改配置不安全。
    /// </summary>
    private void CheckLeave(On.Terraria.Main.orig_Update orig, Main self, Il2CppMicrosoft.Xna.Framework.GameTime gameTime)
    {
        var wasOnline = Main.netMode == 1;

        orig(self, gameTime);

        if (wasOnline && Main.netMode == 0) Revert();
    }

    // ===================== 读写 =====================

    private static Dictionary<string, object> Snapshot(ModConfigEntry entry)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        var options = entry.Options;

        for (var index = 0; index < options.Count; index++)
        {
            var option = options[index];
            if (!ConfigPipeline.HasValue(option)) continue;

            values[option.Key] = entry.Instance.GetValue(option.Key);
        }

        return values;
    }

    private static void WritePairs(ModConfigEntry entry, object[] values, int start)
    {
        // 键名与值成对；尾部不齐的半对直接丢（发送侧不会造出这种包，收侧不猜）。
        for (var index = start; index + 1 < values.Length; index += 2)
        {
            var key = values[index] as string;
            if (string.IsNullOrEmpty(key)) continue;

            var option = FindOption(entry, key);
            if (option is null || !ConfigPipeline.Coerce(option, values[index + 1], out var coerced)) continue;

            entry.Instance.__SetValueRaw(key, coerced);
        }
    }

    private static Dictionary<string, object> ReadPairs(object[] values, int start)
    {
        var pairs = new Dictionary<string, object>(StringComparer.Ordinal);

        for (var index = start; index + 1 < values.Length; index += 2)
        {
            if (values[index] is string key) pairs[key] = values[index + 1];
        }

        return pairs;
    }

    private static ModConfigOption? FindOption(ModConfigEntry entry, string key)
    {
        var options = entry.Options;
        for (var index = 0; index < options.Count; index++)
        {
            if (options[index].Key == key) return options[index];
        }

        return null;
    }

    private static ModConfigEntry? Find(string modName, string name)
    {
        var entries = ConfigPipeline.Instance.List;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Name == name && entry.Mod.Name == modName) return entry;
        }

        return null;
    }
}
