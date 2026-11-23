using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.Localization;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 联机握手：客户端发"请求世界"(msg6) 前先报双指纹（内容 + 纹理，NetPipeline 的 meta 包），
/// 再借 msg68 发 hello——把 Main.clientUUID 临时换成 "pylon-cs:&lt;内容指纹&gt;"，发完还原；
/// 服务器在 ProcessData 认出 hello 即回 {welcome, rules}（NetPolicy 的规则位）并消费掉这条消息。
/// 裁决按类别拆开：内容看模组同步开关，纹理看允许材质包开关；服务器直接踢人，客户端等世界数据
/// (msg7) 到来自踢。客户端收到过指纹包即对方是 Pylon 服，此时服从服务器广播的规则位；
/// 没握手上的原版服仍在 msg7 关口拦下（原版服务器发不了指纹包，也不认 Pylon 的 hello）。
/// 客户端可关闭指纹上报（NetPolicy.SendFingerprint，加入界面的开关）：不发指纹包、hello 报 "-"、
/// 自检豁免——服务器无从核对，本端也不自裁，任何服务器（含未装 Pylon Loader 的原版服）都直接放行，
/// 内容错位风险自担。
/// </summary>
internal sealed class NetHandshake(NetPipeline net)
{
    private const int RequestWorldMsg = 6;
    private const int KickMsg = 2;
    private const int WorldDataMsg = 7;
    private const int ClientUuidMsg = 68;

    internal const string HelloPrefix = "pylon-cs:";
    internal const byte MetaWelcome = 1;
    internal const byte RuleCheats = NetPolicy.RuleCheats;
    internal const byte RuleOwnTextures = NetPolicy.RuleOwnTextures;
    internal const byte RuleOwnMods = NetPolicy.RuleOwnMods;

    private static readonly byte[] HelloPrefixBytes = Encoding.UTF8.GetBytes(HelloPrefix);

    private static bool _welcomed;
    private static int _serverRules = RuleCheats;
    private static ulong _remoteContent;
    private static ulong _remoteTexture;
    private static bool _remoteKnown;

    private static IModLog Log => LoaderHost.Current.Log;

    internal static bool CheatsBlocked =>
        Main.netMode == 1 && _welcomed && (_serverRules & RuleCheats) == 0;

    public void Install()
    {
        On.Terraria.MessageBuffer.ProcessData += Process;
    }

    public void OnRequestWorld(On.Terraria.NetMessage.orig_SendData orig)
    {
        _welcomed = false;
        _serverRules = RuleCheats;
        _remoteContent = 0;
        _remoteTexture = 0;
        _remoteKnown = false;

        ContentFingerprint.Refresh();
        if (NetPolicy.SendFingerprint) net.SendFingerprint(-1);

        var previous = Main.clientUUID;
        try
        {
            Main.clientUUID = HelloPrefix + (NetPolicy.SendFingerprint ? ContentFingerprint.Text : "-");
            orig(ClientUuidMsg, -1, -1, null, 0, 0f, 0f, 0f, 0, 0, 0);
        }
        finally
        {
            Main.clientUUID = previous;
        }
    }

    public void OnHello(int from, string hello)
    {
        if (from < 0 || from >= 255) return;

        Log.Info($"[net] 客户端 {from} 握手成功（指纹 {hello}）");
        net.SendChannel(NetPipeline.MetaKind, writer =>
        {
            writer.Write(MetaWelcome);
            writer.Write(NetPolicy.HostRules);
        }, from, -1);
    }

    public void ReceiveWelcome(byte rules)
    {
        _welcomed = true;
        _serverRules = rules;
        Log.Info("[net] 服务器已握手（作弊 " + ((rules & RuleCheats) != 0 ? "允许" : "禁止") +
                 "；材质 " + ((rules & RuleOwnTextures) != 0 ? "可自带" : "需一致") +
                 "；模组 " + ((rules & RuleOwnMods) != 0 ? "可不同" : "需一致") + "）");
    }

    public void JudgeServer(ulong remote, ulong texture)
    {
        _remoteContent = remote;
        _remoteTexture = texture;
        _remoteKnown = true;
    }

    public void JudgeClient(int from, ulong remote, ulong texture)
    {
        string? reason = null;
        if (ContentFingerprint.Value != remote && NetPolicy.SyncMods)
            reason = "已拒绝进入：客户端与本服 mod 内容不一致。" + ContentFingerprint.Describe(remote);
        else if (texture != 0 && ContentFingerprint.TextureValue != texture && !NetPolicy.AllowTextures)
            reason = "已拒绝进入：客户端与本服纹理不一致。" + ContentFingerprint.DescribeTexture(texture);

        if (reason == null) return;

        var text = reason;
        Log.Warn("[net] " + text);
        Safe.Run("net.kick", () =>
        {
            NetMessage.SendData(KickMsg, from, -1, NetworkText.FromLiteral(text));
            var clients = Netplay.Clients;
            if (clients is not null && (uint)from < (uint)clients.Length && clients[from] is not null)
                clients[from].PendingTermination = true;
        });
    }

    private static string? Refusal()
    {
        if (!NetPolicy.SendFingerprint) return null;
        if (!_welcomed) return "该服务器未运行 Pylon Loader，继续会因内容缺失损坏数据，已拒绝进入";
        if (!_remoteKnown) return null;

        if ((_serverRules & RuleOwnMods) == 0 && ContentFingerprint.Value != _remoteContent)
            return "已拒绝进入：服务器与本端 mod 内容不一致。" + ContentFingerprint.Describe(_remoteContent);
        if ((_serverRules & RuleOwnTextures) == 0 && _remoteTexture != 0 &&
            ContentFingerprint.TextureValue != _remoteTexture)
            return "已拒绝进入：服务器与本端纹理不一致，请关闭你的材质 Mod 后再加入。" +
                   ContentFingerprint.DescribeTexture(_remoteTexture);
        return null;
    }

    private void Process(On.Terraria.MessageBuffer.orig_ProcessData orig, MessageBuffer self,
                         Il2CppStructArray<byte> data, int length, out int messageType)
    {
        if (data is not null && length >= 1 && length <= data.Length && Main.netMode == 1 &&
            data[0] == WorldDataMsg)
        {
            var refusal = Refusal();
            if (refusal is not null)
            {
                Log.Warn("[net] " + refusal);
                Kick(orig, self, refusal, out messageType);
                return;
            }
        }

        if (data is not null && length > 2 && length <= data.Length && Main.netMode == 2 &&
            data[0] == ClientUuidMsg && TryReadHello(data, length, out var hello))
        {
            messageType = ClientUuidMsg;
            OnHello(self.whoAmI, hello);
            return;
        }

        orig(self, data, length, out messageType);
    }

    private static void Kick(On.Terraria.MessageBuffer.orig_ProcessData orig, MessageBuffer self,
                             string reason, out int messageType)
    {
        var body = Encoding.UTF8.GetBytes(reason);
        var message = new List<byte>(2 + 5 + body.Length) { KickMsg, 0 };
        var size = (uint)body.Length;
        while (size >= 0x80)
        {
            message.Add((byte)(size | 0x80));
            size >>= 7;
        }
        message.Add((byte)size);
        message.AddRange(body);

        Il2CppStructArray<byte> data = message.ToArray();
        orig(self, data, data.Length, out messageType);
    }

    private static bool TryReadHello(Il2CppStructArray<byte> data, int length, out string hello)
    {
        hello = "";
        var size = 0;
        var shift = 0;
        var i = 1;
        for (; i < length && shift < 35; shift += 7)
        {
            var b = data[i++];
            size |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
        }

        if (size < HelloPrefixBytes.Length || i + size > length) return false;

        for (var k = 0; k < HelloPrefixBytes.Length; k++)
        {
            if (data[i + k] != HelloPrefixBytes[k]) return false;
        }

        var payload = new byte[size - HelloPrefixBytes.Length];
        for (var k = 0; k < payload.Length; k++) payload[k] = data[i + HelloPrefixBytes.Length + k];
        hello = Encoding.UTF8.GetString(payload);
        return true;
    }
}
