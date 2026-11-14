using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>
/// 联机握手：客户端发"请求世界"(msg6) 前先报指纹（模块包），再借 msg68 发 hello——把 Main.clientUUID
/// 临时换成 "pylon-cs:&lt;指纹&gt;"，发完还原；服务器在 ProcessData 认出 hello 即回 {welcome, rules}
/// （bit0 = 允许作弊）并消费掉这条消息。指纹双向裁决：不一致且开关开着时，客户端等世界数据（msg7）
/// 到来自踢、服务器直接踢人。
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

    private static readonly byte[] HelloPrefixBytes = Encoding.UTF8.GetBytes(HelloPrefix);

    // 客户端状态：每次发 msg6 重置。服务器不发 msg6，服务器侧不用这些。
    private static bool _welcomed;
    private static bool _rejected;
    private static string _rejectReason = "";
    private static int _serverRules = RuleCheats;

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>联机作弊门：客户端已与 Pylon 服务器握手且服务器禁止作弊时为真（模组功能据此让步）。</summary>
    internal static bool CheatsBlocked =>
        Main.netMode == 1 && _welcomed && (_serverRules & RuleCheats) == 0;

    public void Install()
    {
        On.Terraria.MessageBuffer.ProcessData += Process;
    }

    /// <summary>
    /// 客户端发出"请求世界"(msg6) 之前：重置握手状态 → 报指纹 → 借 msg68 发 hello。
    /// 指纹必须先于世界请求：服务器有机会在世界数据发出前把不匹配的客户端踢掉。
    /// </summary>
    public void OnRequestWorld(On.Terraria.NetMessage.orig_SendData orig)
    {
        _welcomed = false;
        _rejected = false;
        _rejectReason = "";
        _serverRules = RuleCheats;

        ContentFingerprint.Refresh();
        net.SendFingerprint(-1);

        var previous = Main.clientUUID;
        try
        {
            Main.clientUUID = HelloPrefix + ContentFingerprint.Text;
            orig(ClientUuidMsg, -1, -1, null, 0, 0f, 0f, 0f, 0, 0, 0);
        }
        finally
        {
            Main.clientUUID = previous;
        }
    }

    /// <summary>服务器收到客户端的 hello：回 welcome（规则位随包），hello 消费掉不进原版。</summary>
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

    /// <summary>客户端收到服务器的 welcome：记规则位。此后收到世界数据才算真正放行。</summary>
    public void ReceiveWelcome(byte rules)
    {
        _welcomed = true;
        _serverRules = rules;
        Log.Info("[net] 服务器已握手（作弊 " + ((rules & RuleCheats) != 0 ? "允许" : "禁止") + "）");
    }

    /// <summary>客户端裁决服务器的指纹：不一致且开关开着 → 记下拒绝原因，等世界数据（msg7）到来自踢。</summary>
    public void JudgeServer(ulong remote)
    {
        if (ContentFingerprint.Value == remote || !NetPolicy.RequireMatch) return;

        _rejected = true;
        _rejectReason = "已拒绝进入：服务器与本端 mod 内容不一致。" + ContentFingerprint.Describe(remote);
    }

    /// <summary>服务器裁决客户端报上来的指纹：不一致且开关开着 → 直接踢。</summary>
    public void JudgeClient(int from, ulong remote)
    {
        if (ContentFingerprint.Value == remote || !NetPolicy.RequireMatch) return;

        var reason = "已拒绝进入：客户端与本服 mod 内容不一致。" + ContentFingerprint.Describe(remote);
        Log.Warn("[net] " + reason);
        Safe.Run("net.kick", () =>
        {
            NetMessage.SendData(KickMsg, from, -1, NetworkText.FromLiteral(reason));
            var clients = Netplay.Clients;
            if (clients is not null && (uint)from < (uint)clients.Length && clients[from] is not null)
                clients[from].PendingTermination = true;
        });
    }

    /// <summary>客户端收到世界数据时的放行裁决；null = 放行。</summary>
    private static string? Refusal()
    {
        if (!_welcomed) return "该服务器未运行 Pylon Loader，继续会因内容缺失损坏数据，已拒绝进入";
        if (_rejected) return _rejectReason;
        return null;
    }

    private void Process(On.Terraria.MessageBuffer.orig_ProcessData orig, MessageBuffer self,
                         Il2CppStructArray<byte> data, int length, out int messageType)
    {
        // 客户端收到世界数据：进世界的最后一关。没握上手（连了原版服）或指纹被拒都不放行。
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

        // 服务器收到 UUID 消息：正文以 "pylon-cs:" 开头的是 Pylon 客户端的 hello；普通 UUID 照旧交给原版。
        if (data is not null && length > 2 && length <= data.Length && Main.netMode == 2 &&
            data[0] == ClientUuidMsg && TryReadHello(data, length, out var hello))
        {
            messageType = ClientUuidMsg;
            OnHello(self.whoAmI, hello);
            return;
        }

        orig(self, data, length, out messageType);
    }

    /// <summary>构造原版踢人消息（{类型, 0, 变长长度, UTF-8 正文}）喂回原版处理链，让游戏用自带的界面断开。</summary>
    private static void Kick(On.Terraria.MessageBuffer.orig_ProcessData orig, MessageBuffer self,
                             string reason, out int messageType)
    {
        var body = Encoding.UTF8.GetBytes(reason);
        // 本构建的 msg2 在字符串正文前多读一个字节，所以这里有第二个 0。
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

    /// <summary>
    /// msg68 的正文是 BinaryWriter.Write(string)：7-bit 变长长度 + UTF-8。
    /// 只有正文以 Pylon 前缀开头的才算 hello；原版 UUID 原样放行。
    /// </summary>
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
