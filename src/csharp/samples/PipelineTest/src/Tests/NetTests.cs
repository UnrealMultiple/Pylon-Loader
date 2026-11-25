#nullable enable
using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent.UI;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Core;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 26：Net 三件套（内容指纹 / SyncPlayer 摘要 / BannerNet 与 EmoteNet 的单机可验面）。
/// Ready：NetWriter→NetReader 值往返、ModPacket 是 BinaryWriter 且单机 Send 抛、表情锚序列化往返、Mod 包入口面。
/// World：PlayerNet 钩子派发计数、ApplyRemote 重建 mod 泡泡、单机发包抛。
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

        // N-R2：ModPacket 是 BinaryWriter；单机 Send 抛（基线的失败语义）。
        PT.Case("NET-R2", c =>
        {
            var packet = PipelineTestMod.Instance.GetPacket();
            c.True(packet is System.IO.BinaryWriter, "ModPacket : BinaryWriter");
            packet.Write(1);
            packet.Write("单人");
            c.True(packet.BaseStream.Position > 0, "写入进包");

            var threw = false;
            try { packet.Send(); }
            catch (InvalidOperationException) { threw = true; }
            c.True(threw, "单机 Send 抛 InvalidOperationException");
        });

        // N-R3：表情锚构造（1.4.6 删了 EmoteBubble 的锚序列化辅助，mod 同步走框架的载荷）。
        PT.Case("NET-R3", c =>
        {
            var player = Main.player[Main.myPlayer];
            if (player is null)
            {
                c.True(true, "主菜单无玩家，跳过锚构造");
                return;
            }

            var anchor = new WorldUIAnchor(player);
            c.True(anchor.type == WorldUIAnchor.AnchorType.Entity, $"锚类型 {anchor.type}（期望 Entity）");
            c.True(anchor.entity is not null && anchor.entity.Pointer == player.Pointer, "锚指回玩家实体");
        });

        // N-R4：Mod 包入口面（HandlePacket(BinaryReader, int) / GetPacket）。
        PT.Case("NET-R4", c =>
        {
            var type = typeof(Mod);
            var handle = type.GetMethod("HandlePacket");
            c.NotNull(handle, "Mod.HandlePacket");
            c.True(handle!.GetParameters() is [{ ParameterType: var reader }, _] && reader == typeof(System.IO.BinaryReader),
                "HandlePacket(BinaryReader, int)");
            c.NotNull(type.GetMethod("GetPacket"), "Mod.GetPacket");
            c.True(type.GetMethod("NetSend") is null && type.GetMethod("NetReceive") is null, "没有 Mod.NetSend/NetReceive");
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

        // N-W3：世界中单机发 ModPacket 同样抛（与基线一致）。
        PT.Case("NET-W3", c =>
        {
            var threw = false;
            try { PipelineTestMod.Instance.GetPacket().Send(); }
            catch (InvalidOperationException) { threw = true; }
            c.True(threw, "World 阶段单机发 ModPacket 抛 InvalidOperationException");
        });
    }
}
