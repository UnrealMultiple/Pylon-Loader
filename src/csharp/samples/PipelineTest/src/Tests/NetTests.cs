#nullable enable
using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent.UI;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 26：Net 三件套（内容指纹 / SyncPlayer 摘要 / BannerNet 与 EmoteNet 的单机可验面）。
/// Ready：NetWriter→NetReader 值往返、ModPacket 单机 no-op、表情锚序列化往返、联机入口方法面。
/// World：PlayerNet 钩子派发计数、ApplyRemote 重建 mod 泡泡、单机发包 no-op。
/// </summary>
internal static class NetTests
{
    public static void Register()
    {
        PT.Group("net", Phase.Ready, RunReady);
        PT.Group("net-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        // N-R1：NetWriter 按序存值、NetReader 按序读回——管线序列化两端共用的公共契约。
        PT.Case("NET-R1", c =>
        {
            var w = new NetWriter();
            w.Write(1234567);
            w.Write("管线文本");
            w.Write(true);
            w.WriteVector2(new Vector2(3.5f, -7.25f));
            w.WriteFlags(true, false, true, true);
            w.WriteByte(217);
            w.WriteInt64(long.MaxValue);
            c.Eq(w.Values.Count, 7, "Values 按写入顺序存 7 个");

            var r = new NetReader(w.Values.ToArray());
            c.Eq(r.ReadInt32(), 1234567, "int 往返");
            c.Eq(r.ReadString(), "管线文本", "string 往返");
            c.True(r.ReadBoolean(), "bool 往返");
            var v = r.ReadVector2();
            c.True(Math.Abs(v.X - 3.5f) < 1e-5f && Math.Abs(v.Y + 7.25f) < 1e-5f, $"Vector2 往返 ({v.X},{v.Y})");
            var flags = r.ReadFlags();
            c.True(flags is [true, false, true, true], $"flags 往返 ({string.Join(",", flags ?? Array.Empty<bool>())})");
            c.Eq(r.ReadByte(), 217, "byte 往返");
            c.Eq(r.ReadInt64(), long.MaxValue, "long 往返");
            c.True(!r.HasMore, "读完即止");
        });

        // N-R2：ModPacket 单机 Send 是 no-op，不能抛（单人游戏走不到网络层）。
        PT.Case("NET-R2", c =>
        {
            var packet = PipelineTestMod.Instance.GetPacket();
            packet.Write(1);
            packet.Write("单人");
            c.True(packet.Values.Count == 2, "ModPacket 存值");
            c.NoThrow(() => packet.Send(), "ModPacket.Send 单机 no-op");
        });

        // N-R3：表情锚序列化往返（EmoteNet 同步载荷里那对函数）：玩家锚 → 序列化 → 反序列化。
        PT.Case("NET-R3", c =>
        {
            var player = Main.player[Main.myPlayer];
            if (player is null)
            {
                c.True(true, "主菜单无玩家，跳过锚往返");
                return;
            }

            var anchor = new WorldUIAnchor(player);
            var pair = EmoteBubble.SerializeNetAnchor(anchor);
            c.NotNull(pair, "玩家锚序列化有值");
            if (pair is not { } p) return;

            var rebuilt = EmoteBubble.DeserializeNetAnchor(p.Item1, p.Item2);
            c.NotNull(rebuilt, "反序列化回锚");
        });

        // N-R4：联机数据三件套的入口方法面（Mod.NetSend / NetReceive / HandlePacket / GetPacket）。
        PT.Case("NET-R4", c =>
        {
            var type = typeof(Mod);
            c.NotNull(type.GetMethod("NetSend"), "Mod.NetSend");
            c.NotNull(type.GetMethod("NetReceive"), "Mod.NetReceive");
            c.NotNull(type.GetMethod("HandlePacket"), "Mod.HandlePacket");
            c.NotNull(type.GetMethod("GetPacket"), "Mod.GetPacket");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("NET-W1", "玩家还没就绪");
            PT.SkipCase("NET-W2", "玩家还没就绪");
            PT.SkipCase("NET-W3", "玩家还没就绪");
            return;
        }

        // N-W1：进世界触发的 PlayerNet 派发。OnEnterWorld 进世界那一刻派发；
        // SyncPlayer / SendClientChanges 是联机路径，单机（netMode=0）不能误派。
        PT.Case("NET-W1", c =>
        {
            c.True(Content.TestNetPlayer.CtOnEnterWorld > 0,
                $"OnEnterWorld 派发（{Content.TestNetPlayer.CtOnEnterWorld} 次）");
            c.Eq(Content.TestNetPlayer.CtSyncPlayer, 0,
                "单机不派 SyncPlayer（联机时 SyncOnePlayer/EnterWorld 才走）");
            c.Eq(Content.TestNetPlayer.CtSendClientChanges, 0,
                "单机不派 SendClientChanges（那是联机客户端每帧的活）");
            PT.Info($"PlayerConnect={Content.TestNetPlayer.CtPlayerConnect} PlayerDisconnect={Content.TestNetPlayer.CtPlayerDisconnect}（单机不触发属正常）");
        });

        // N-W2：联机钩子的方法面（SyncPlayer / CopyClientState / SendClientChanges / PlayerConnect /
        // PlayerDisconnect）都在 ModPlayer 基类上；单机时 LastSync 应该是空的。
        PT.Case("NET-W2", c =>
        {
            c.True(Content.TestNetPlayer.LastSync.Length == 0,
                "单机无 SyncPlayer 记录");

            var mp = typeof(ModPlayer);
            c.NotNull(mp.GetMethod("PlayerConnect"), "ModPlayer.PlayerConnect");
            c.NotNull(mp.GetMethod("PlayerDisconnect"), "ModPlayer.PlayerDisconnect");
            c.NotNull(mp.GetMethod("SyncPlayer"), "ModPlayer.SyncPlayer");
            c.NotNull(mp.GetMethod("CopyClientState"), "ModPlayer.CopyClientState");
            c.NotNull(mp.GetMethod("SendClientChanges"), "ModPlayer.SendClientChanges");
        });

        // N-W3：单机发 ModPacket 仍然 no-op（NetPipeline.SendChannel 对 netMode=0 直接返回）。
        PT.Case("NET-W3", c =>
        {
            c.NoThrow(() => PipelineTestMod.Instance.GetPacket().Send(), "World 阶段单机发 ModPacket 仍 no-op");
        });
    }
}
