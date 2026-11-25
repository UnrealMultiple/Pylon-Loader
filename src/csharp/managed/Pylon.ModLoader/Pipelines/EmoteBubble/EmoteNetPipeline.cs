using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.GameContent.UI;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// mod 表情的多人同步：原版表情包只带原版编号，对端看不懂；这里带上"表情号 + 锚的序列化 + 时长"，
/// 收到的人用同一个锚重建泡泡。1.4.6 删掉了游戏自带的锚序列化辅助，这里用固定载荷自己编解码：
/// 玩家/NPC 只带 whoAmI（同一局里双方编号一致），方块/坐标带位置与尺寸，无锚带一个空标记。
/// 走向：客户端发给服务器，服务器转发给其他人并自己建泡。
/// </summary>
internal sealed class EmoteNetPipeline : ILoaderModule
{
    public static EmoteNetPipeline Instance { get; } = new();

    public string Name => "emotenet";

    private static IModLog Log => LoaderHost.Current.Log;

    private NetPipeline _net = null!;
    private byte _kind;

    /// <summary>emotenet 管线的安装门：仅在有 ModEmoteBubble 注册时开同步通道。</summary>
    internal static class Gates
    {
        internal struct Sync : IGate
        {
            public static bool Wanted() => ModContent.GetContent<ModEmoteBubble>().Count > 0;

            public static void Install() => Instance.InstallSync();
        }
    }

    public bool Install() => GateHub<EmoteNetPipeline>.Run();

    private void InstallSync()
    {
        _net = NetPipeline.Instance;
        _kind = _net.OpenChannel(Receive);

        Log.Info("[emotenet] 已挂（mod 表情：客户端发 / 服务器转发并自建）");
    }

    /// <summary>把一次 mod 表情广播出去：客户端发给服务器，服务器发给所有客户端。</summary>
    internal void Push(int emoticon, WorldUIAnchor anchor, int time)
    {
        if (Main.netMode == 0 || EmoteBubblePipeline.OnReceive) return;

        try
        {
            var values = EncodeAnchor(emoticon, anchor, time);
            Send(values, Main.netMode == 1 ? 255 : -1, -1);
        }
        catch (Exception e)
        {
            ContentErrors.Line("[emotenet] 序列化表情锚失败：" + Safe.Dump(e));
        }
    }

    internal static int[] EncodeAnchor(int emoticon, WorldUIAnchor anchor, int time)
    {
        var values = new int[8];
        values[0] = emoticon;
        values[1] = time;
        if (anchor is null)
        {
            values[2] = 4;
            return values;
        }
        if (anchor.entity is { } entity)
        {
            var player = entity.TryCast<Player>();
            if (player is not null)
            {
                values[2] = 0;
                values[3] = player.whoAmI;
                return values;
            }
            var npc = entity.TryCast<NPC>();
            if (npc is not null)
            {
                values[2] = 1;
                values[3] = npc.whoAmI;
                return values;
            }
        }
        values[3] = (int)anchor.pos.X;
        values[4] = (int)anchor.pos.Y;
        switch (anchor.type)
        {
            case WorldUIAnchor.AnchorType.Tile:
                values[2] = 2;
                values[5] = (int)anchor.size.X;
                values[6] = (int)anchor.size.Y;
                break;
            case WorldUIAnchor.AnchorType.None:
                values[2] = 4;
                break;
            default:
                values[2] = 3;
                break;
        }
        return values;
    }

    internal static WorldUIAnchor? DecodeAnchor(int[] values)
    {
        var kind = values[2];
        var a = values[3];
        var b = values[4];
        switch (kind)
        {
            case 0:
                var players = Main.player;
                if (players is null || (uint)a >= (uint)players.Length) return null;
                return new WorldUIAnchor(players[a]);
            case 1:
                var npcs = Main.npc;
                if (npcs is null || (uint)a >= (uint)npcs.Length) return null;
                return new WorldUIAnchor(npcs[a]);
            case 2:
                return new WorldUIAnchor(a, b, values[5], values[6]);
            case 3:
                return new WorldUIAnchor(new Il2CppMicrosoft.Xna.Framework.Vector2(a, b));
            case 4:
                return new WorldUIAnchor();
            default:
                return null;
        }
    }

    private void Send(int[] values, int toClient, int ignoreClient)
    {
        var list = new List<object>(values.Length);
        foreach (var value in values) list.Add(value);
        _net.SendValues(_kind, list, toClient, ignoreClient);
    }

    private void Receive(Il2CppSystem.IO.BinaryReader reader, int from)
    {
        var raw = NetCodec.ReadValues(reader);
        if (raw.Length < 7) return;

        var values = new int[8];
        for (var i = 0; i < raw.Length && i < values.Length; i++) values[i] = Convert.ToInt32(raw[i]);

        var emoticon = values[0];
        var time = values[1];

        if (emoticon < EmoteBubbleTable.Base) return;

        try
        {
            if (Main.netMode == 2) Send(values, -1, from);

            var anchor = DecodeAnchor(values);
            if (anchor is null) return;

            EmoteBubblePipeline.ApplyRemote(emoticon, anchor, time);
        }
        catch (Exception e)
        {
            ContentErrors.Line("[emotenet] 重建 mod 表情失败：" + Safe.Dump(e));
        }
    }
}
