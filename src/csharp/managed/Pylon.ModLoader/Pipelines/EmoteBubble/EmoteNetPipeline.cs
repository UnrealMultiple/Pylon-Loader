using Terraria;
using Terraria.GameContent.UI;

namespace Pylon.ModLoader;

/// <summary>
/// mod 表情的多人同步：原版表情包只带原版编号，对端看不懂；这里带上"表情号 + 锚的序列化 + 时长"，
/// 收到的人用同一个锚重建泡泡。锚的序列化用游戏自己的 <c>EmoteBubble.SerializeNetAnchor</c> /
/// <c>DeserializeNetAnchor</c>——玩家/NPC/方块/坐标四种锚它都处理了，我们不复制一遍。
/// 走向：客户端发给服务器，服务器转发给其他人并自己建泡。
/// </summary>
internal sealed class EmoteNetPipeline : ILoaderModule
{
    public static EmoteNetPipeline Instance { get; } = new();

    public string Name => "emotenet";

    private static IModLog Log => LoaderHost.Current.Log;

    private NetPipeline _net = null!;
    private byte _kind;

    public bool Install()
    {
        _net = NetPipeline.Instance;
        _kind = _net.OpenChannel(Receive);

        Log.Info("[emotenet] 已挂（mod 表情：客户端发 / 服务器转发并自建）");
        return true;
    }

    /// <summary>把一次 mod 表情广播出去：客户端发给服务器，服务器发给所有客户端。</summary>
    internal void Push(int emoticon, WorldUIAnchor anchor, int time)
    {
        if (Main.netMode == 0 || EmoteBubblePipeline.OnReceive) return;

        try
        {
            var pair = EmoteBubble.SerializeNetAnchor(anchor);
            if (pair is null) return;

            Send(emoticon, pair.Item1, pair.Item2, time, Main.netMode == 1 ? 255 : -1, -1);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[emotenet] 序列化表情锚失败：" + Safe.Describe(e));
        }
    }

    private void Send(int emoticon, int type, int meta, int time, int toClient, int ignoreClient) =>
        _net.SendValues(_kind, [emoticon, type, meta, time], toClient, ignoreClient);

    private void Receive(Il2CppSystem.IO.BinaryReader reader, int from)
    {
        var values = NetCodec.ReadValues(reader);
        if (values.Length < 4) return;

        var emoticon = Convert.ToInt32(values[0]);
        var type = Convert.ToInt32(values[1]);
        var meta = Convert.ToInt32(values[2]);
        var time = Convert.ToInt32(values[3]);

        if (emoticon < EmoteBubbleTable.Base) return;

        try
        {
            // 服务器收到客户端发的那一份，要转给除发送者以外的所有人，然后自己也建一个。
            if (Main.netMode == 2) Send(emoticon, type, meta, time, -1, from);

            var anchor = EmoteBubble.DeserializeNetAnchor(type, meta);
            if (anchor is null) return;

            EmoteBubblePipeline.ApplyRemote(emoticon, anchor, time);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[emotenet] 重建 mod 表情失败：" + Safe.Describe(e));
        }
    }
}
