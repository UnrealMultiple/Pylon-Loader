using System.IO;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.Net;

namespace Pylon.ModLoader;

/// <summary>
/// Mod 数据通道：一个消息号 + 一个种类字节。四种包：Mod 包 / 世界 / NPC 附加 / 弹幕附加。
/// 载荷的序列化在 NetCodec。
/// </summary>
internal sealed class NetPipeline : ILoaderModule
{
    public static NetPipeline Instance { get; } = new();

    private const ushort Id = 0xB17E;
    private const int MaxBytes = 65000;

    internal const byte ItemKind = 5;

    internal const byte MetaKind = 6;
    private const byte MetaFingerprint = 0;

    private const byte KindPacket = 1, KindWorld = 2, KindNpc = 3, KindProjectile = 4;
    private const byte FirstChannelKind = 7;

    internal delegate void NetHandler(Il2CppSystem.IO.BinaryReader reader, int from);

    private readonly Dictionary<string, Mod> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<byte, NetHandler> _channels = [];
    private readonly MemoryStream _out = new(4096);
    private BinaryWriter? _writer;
    private int _nextChannelKind = FirstChannelKind;
    private ItemSync _items = null!;
    private NetHandshake _handshake = null!;

    public string Name => "net";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        foreach (var mod in ModLoader.Mods)
        {
            if (mod is not null && !_byName.ContainsKey(mod.Name)) _byName[mod.Name] = mod;
        }

        On.Terraria.Net.NetManager.Read += Read;
        On.Terraria.NetMessage.SendData += Send;

        _items = new ItemSync(this);
        _items.Install();
        ItemPvpHooks.InstallNetwork(this);

        _handshake = new NetHandshake(this);
        _handshake.Install();

        ContentFingerprint.Refresh();
        Log.Info("[net] 已挂（消息 0x" + Id.ToString("X") + "；" + _byName.Count + " 个 mod 可寻址；物品附属数据走 " +
                 ItemKind + "；握手前缀 " + NetHandshake.HelloPrefix + "；" + ContentFingerprint.Summary() + "）");
        return true;
    }

    #region 发送

    internal byte OpenChannel(NetHandler handler)
    {
        var kind = (byte)_nextChannelKind++;
        _channels[kind] = handler;
        return kind;
    }

    internal void SendChannel(byte kind, Action<BinaryWriter> write, int toClient, int ignoreClient)
    {
        if (Main.netMode == 0) return;

        var writer = Begin();
        writer.Write(kind);
        write(writer);
        End(toClient, ignoreClient);
    }

    internal void SendValues(byte kind, List<object> values, int toClient, int ignoreClient)
    {
        if (Main.netMode == 0) return;

        var writer = Begin();
        writer.Write(kind);
        NetCodec.WriteValues(writer, values);
        End(toClient, ignoreClient);
    }

    internal void SendPacket(ModPacket packet, int toClient, int ignoreClient)
    {
        if (Main.netMode == 0 || packet?.Mod is null) return;

        var writer = Begin();
        writer.Write(KindPacket);
        writer.Write(packet.Mod.Name);
        NetCodec.WriteValues(writer, packet.Values);
        End(toClient, ignoreClient);
    }

    internal void SendFingerprint(int toClient)
    {
        ContentFingerprint.Refresh();
        SendChannel(MetaKind, writer =>
        {
            writer.Write(MetaFingerprint);
            writer.Write(ContentFingerprint.Value);
        }, toClient, -1);
    }

    private void SendWorld(Mod mod, int toClient, int ignoreClient)
    {
        var values = new NetWriter();
        try
        {
            mod.NetSend(values);
        }
        catch (Exception e)
        {
            Safe.Report(mod.Name + ".NetSend", e);
            return;
        }

        if (values.Values.Count == 0) return;

        var writer = Begin();
        writer.Write(KindWorld);
        writer.Write(mod.Name);
        NetCodec.WriteValues(writer, values.Values);
        End(toClient, ignoreClient);
    }

    private void SendNpc(int index, int remoteClient, int ignoreClient)
    {
        if (Main.netMode == 1 || index < 0 || index >= Main.npc.Length) return;

        var npc = Main.npc[index];
        if (npc is null || !npc.active) return;

        var own = new NetWriter();
        (ModContent.Find(ContentKind.Npc, npc.type) as ModNpc)?.SendExtraAI(npc, own);

        var globals = GlobalNpcRegistry.All;
        var writer = Begin();
        writer.Write(KindNpc);
        writer.Write(index);
        writer.Write(npc.type);
        NetCodec.WriteValues(writer, own.Values);

        var field = (int)writer.BaseStream.Position;
        writer.Write((byte)0);
        var count = 0;
        for (var i = 0; i < globals.Count; i++)
        {
            var temp = new NetWriter();
            try
            {
                globals[i].NetSend(npc, temp);
            }
            catch (Exception e)
            {
                Safe.Report(globals[i].Name + ".NetSend", e);
                continue;
            }

            if (temp.Values.Count == 0) continue;

            writer.Write((byte)i);
            NetCodec.WriteValues(writer, temp.Values);
            count++;
        }

        EndWithCount(writer, field, count, own.Values.Count > 0 || count > 0, remoteClient, ignoreClient);
    }

    private void SendProjectile(int index, int remoteClient, int ignoreClient)
    {
        if (index < 0 || index >= Main.projectile.Length) return;

        var projectile = Main.projectile[index];
        if (projectile is null || !projectile.active) return;

        if (Main.netMode == 1 && projectile.owner != Main.myPlayer) return;

        var own = new NetWriter();
        (ModContent.Find(ContentKind.Projectile, projectile.type) as ModProjectile)?.SendExtraAI(projectile, own);

        var globals = GlobalProjectileRegistry.All;
        var key = projectile.key;

        var writer = Begin();
        writer.Write(KindProjectile);
        writer.Write(projectile.owner);
        writer.Write(key.Spawner);
        writer.Write(key.Index);
        writer.Write(key.Generation);
        writer.Write(projectile.type);
        NetCodec.WriteValues(writer, own.Values);

        var field = (int)writer.BaseStream.Position;
        writer.Write((byte)0);
        var count = 0;
        for (var i = 0; i < globals.Count; i++)
        {
            var temp = new NetWriter();
            try
            {
                globals[i].NetSend(projectile, temp);
            }
            catch (Exception e)
            {
                Safe.Report(globals[i].Name + ".NetSend", e);
                continue;
            }

            if (temp.Values.Count == 0) continue;

            writer.Write((byte)i);
            NetCodec.WriteValues(writer, temp.Values);
            count++;
        }

        EndWithCount(writer, field, count, own.Values.Count > 0 || count > 0, remoteClient, ignoreClient);
    }

    private BinaryWriter Begin()
    {
        _out.SetLength(0);
        _writer ??= new BinaryWriter(_out, System.Text.Encoding.UTF8, true);
        return _writer;
    }

    private void EndWithCount(BinaryWriter writer, int field, int count, bool any, int toClient, int ignoreClient)
    {
        if (!any)
        {
            _out.SetLength(0);
            return;
        }

        var end = (int)_out.Position;
        _out.Position = field;
        writer.Write((byte)count);
        _out.Position = end;
        End(toClient, ignoreClient);
    }

    private void End(int toClient, int ignoreClient)
    {
        var length = (int)_out.Length;
        if (length > MaxBytes)
        {
            Safe.Once("net.size", $"Mod 网络包 {length} 字节，超过 {MaxBytes}；已丢弃", true);
            return;
        }

        if (length < 3) return;

        var packet = new NetPacket(Id, length);
        packet.Writer.Write(_out.ToArray());
        packet.ShrinkToFit();

        var net = NetManager.Instance;
        if (Main.netMode == 1) net.SendToServer(packet);
        else if (toClient >= 0) net.SendToClient(packet, toClient);
        else net.Broadcast(packet, ignoreClient);
    }

    #endregion

    #region 接收

    private void Read(On.Terraria.Net.NetManager.orig_Read orig, NetManager self,
                      Il2CppSystem.IO.BinaryReader reader, int userId, int readLength)
    {
        if (readLength < 2)
        {
            orig(self, reader, userId, readLength);
            return;
        }

        var stream = reader.BaseStream;
        var start = stream.Position;

        try
        {
            if (reader.ReadUInt16() != Id)
            {
                stream.Position = start;
                orig(self, reader, userId, readLength);
                return;
            }

            var from = Main.netMode == 1 ? 256 : userId;
            var kind = reader.ReadByte();
            switch (kind)
            {
                case KindPacket: ReceivePacket(reader, from); break;
                case KindWorld: ReceiveWorld(reader); break;
                case KindNpc: ReceiveNpc(reader); break;
                case KindProjectile: ReceiveProjectile(reader); break;
                case ItemKind: _items.Receive(reader, from); break;
                case MetaKind: ReceiveMeta(reader, from); break;
                default: ReceiveChannel(kind, reader, from); break;
            }
        }
        catch (Exception e)
        {
            Safe.Report("net.收包", e);
        }
    }

    private void ReceiveMeta(Il2CppSystem.IO.BinaryReader reader, int from)
    {
        var action = reader.ReadByte();
        switch (action)
        {
            case MetaFingerprint:
            {
                var remote = reader.ReadUInt64();
                ContentFingerprint.Refresh();
                JudgeFingerprint(remote, from);
                break;
            }
            case NetHandshake.MetaWelcome:
            {
                if (Main.netMode == 1) _handshake.ReceiveWelcome(reader.ReadByte());
                break;
            }
        }
    }

    private void JudgeFingerprint(ulong remote, int from)
    {
        if (ContentFingerprint.Value == remote)
        {
            Log.Info($"[net] {ContentFingerprint.Summary()}，与对端一致");
            return;
        }

        Safe.Once("net.fingerprint", "[net] " + ContentFingerprint.Describe(remote), true);

        if (Main.netMode == 1) _handshake.JudgeServer(remote);
        else _handshake.JudgeClient(from, remote);
    }

    private void ReceiveChannel(byte kind, Il2CppSystem.IO.BinaryReader reader, int from)
    {
        if (!_channels.TryGetValue(kind, out var handler)) return;

        try
        {
            handler(reader, from);
        }
        catch (Exception e)
        {
            Safe.Report($"net.channel.{kind}", e);
        }
    }

    private Mod? ModOf(Il2CppSystem.IO.BinaryReader reader)
    {
        var name = reader.ReadString();
        return _byName.TryGetValue(name, out var mod) ? mod : null;
    }

    private void ReceivePacket(Il2CppSystem.IO.BinaryReader reader, int from)
    {
        var mod = ModOf(reader);
        var values = NetCodec.ReadValues(reader);
        if (mod is null) return;

        try
        {
            mod.HandlePacket(new NetReader(values), from);
        }
        catch (Exception e)
        {
            Safe.Report(mod.Name + ".HandlePacket", e);
        }
    }

    private void ReceiveWorld(Il2CppSystem.IO.BinaryReader reader)
    {
        var mod = ModOf(reader);
        var values = NetCodec.ReadValues(reader);
        if (mod is null) return;

        try
        {
            mod.NetReceive(new NetReader(values));
        }
        catch (Exception e)
        {
            Safe.Report(mod.Name + ".NetReceive", e);
        }
    }

    private void ReceiveNpc(Il2CppSystem.IO.BinaryReader reader)
    {
        var index = reader.ReadInt32();
        var type = reader.ReadInt32();
        var own = NetCodec.ReadValues(reader);
        var count = reader.ReadByte();

        if (index < 0 || index >= Main.npc.Length) return;

        var npc = Main.npc[index];
        if (npc is null || !npc.active || npc.type != type) return;

        try
        {
            (ModContent.Find(ContentKind.Npc, type) as ModNpc)?.ReceiveExtraAI(npc, new NetReader(own));
        }
        catch (Exception e)
        {
            Safe.Report("net.npc.ai", e);
        }

        var globals = GlobalNpcRegistry.All;
        for (var i = 0; i < count; i++)
        {
            var slot = reader.ReadByte();
            var values = NetCodec.ReadValues(reader);
            if (slot >= globals.Count) continue;

            try
            {
                globals[slot].NetReceive(npc, new NetReader(values));
            }
            catch (Exception e)
            {
                Safe.Report(globals[slot].Name + ".NetReceive", e);
            }
        }
    }

    private void ReceiveProjectile(Il2CppSystem.IO.BinaryReader reader)
    {
        var owner = reader.ReadInt32();
        var spawner = reader.ReadInt32();
        var keyIndex = reader.ReadInt32();
        var generation = reader.ReadInt32();
        var type = reader.ReadInt32();
        var own = NetCodec.ReadValues(reader);
        var count = reader.ReadByte();

        var projectile = FindProjectile(owner, spawner, keyIndex, generation, type);
        if (projectile is null) return;

        try
        {
            (ModContent.Find(ContentKind.Projectile, type) as ModProjectile)?.ReceiveExtraAI(
                projectile, new NetReader(own));
        }
        catch (Exception e)
        {
            Safe.Report("net.projectile.ai", e);
        }

        var globals = GlobalProjectileRegistry.All;
        for (var i = 0; i < count; i++)
        {
            var slot = reader.ReadByte();
            var values = NetCodec.ReadValues(reader);
            if (slot >= globals.Count) continue;

            try
            {
                globals[slot].NetReceive(projectile, new NetReader(values));
            }
            catch (Exception e)
            {
                Safe.Report(globals[slot].Name + ".NetReceive", e);
            }
        }
    }

    private static Projectile? FindProjectile(int owner, int spawner, int keyIndex, int generation, int type)
    {
        var all = Main.projectile;
        for (var i = 0; i < all.Length; i++)
        {
            var projectile = all[i];
            if (projectile is null || !projectile.active) continue;
            if (projectile.owner != owner || projectile.type != type) continue;

            var key = projectile.key;
            if (key.Spawner == spawner && key.Index == keyIndex && key.Generation == generation)
                return projectile;
        }

        return null;
    }

    #endregion

    #region 原生消息前后

    private void Send(On.Terraria.NetMessage.orig_SendData orig, int msgType, int remoteClient, int ignoreClient,
                      NetworkText text, int number, float number2, float number3, float number4, int number5,
                      int number6, int number7)
    {
        var hijacked = false;

        if (msgType == 6 && Main.netMode == 1 && !hijacked)
            Safe.Run("net.hello", () => _handshake.OnRequestWorld(orig));

        if (msgType == 7 && Main.netMode == 2 && remoteClient >= 0)
            Safe.Run("net.fingerprint", () => SendFingerprint(remoteClient));

        if (Main.netMode != 0)
        {
            var who = Main.netMode == 2 && remoteClient >= 0 ? remoteClient : 256;

            foreach (var mod in ModLoader.Mods)
            {
                if (mod is null || !mod.Plan.Has(HookId.HijackSendData)) continue;

                try
                {
                    if (!mod.HijackSendData(who, msgType, remoteClient, ignoreClient, text, number, number2,
                            number3, number4, number5, number6, number7)) continue;

                    hijacked = true;
                    break;
                }
                catch (Exception e)
                {
                    Safe.Report(mod.Name + ".HijackSendData", e);
                }
            }
        }

        if (!hijacked && Main.netMode != 0)
            Safe.Run("net.item", () => _items.BeforeSend(msgType, remoteClient, ignoreClient, number, (int)number2));

        if (!hijacked)
        {
            orig(msgType, remoteClient, ignoreClient, text, number, number2, number3, number4, number5, number6,
                 number7);
        }

        if (Main.netMode == 0) return;

        switch (msgType)
        {
            case 7:
                if (Main.netMode == 1) return;

                foreach (var mod in ModLoader.Mods)
                {
                    if (mod is null || !mod.Plan.Has(HookId.NetSend)) continue;
                    Safe.Run(mod.Name + ".NetSend", () => SendWorld(mod, remoteClient, ignoreClient));
                }

                break;
            case 23: Safe.Run("net.npc", () => SendNpc((int)number, remoteClient, ignoreClient)); break;
            case 27: Safe.Run("net.projectile", () => SendProjectile((int)number, remoteClient, ignoreClient)); break;
        }
    }

    #endregion
}
