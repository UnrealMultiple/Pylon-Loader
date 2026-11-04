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

    // 联机元信息（内容指纹）：两端比对发号是否一致，不做任何游戏逻辑。
    private const byte MetaKind = 6;
    private const byte MetaFingerprint = 0;

    private const byte KindPacket = 1, KindWorld = 2, KindNpc = 3, KindProjectile = 4;
    private const byte FirstChannelKind = 7;

    /// <summary>子通道的接收端：别的管线借 Mod 通道发自己的包，种类字节由这里分配。</summary>
    internal delegate void NetHandler(Il2CppSystem.IO.BinaryReader reader, int from);

    private readonly Dictionary<string, Mod> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<byte, NetHandler> _channels = [];
    private readonly MemoryStream _out = new(4096);
    private BinaryWriter? _writer;
    private int _nextChannelKind = FirstChannelKind;
    private ItemSync _items = null!;

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

        ContentFingerprint.Refresh();
        Log.Info("[net] 已挂（消息 0x" + Id.ToString("X") + "；" + _byName.Count + " 个 mod 可寻址；物品附属数据走 " +
                 ItemKind + "；" + ContentFingerprint.Summary() + "）");
        return true;
    }

    #region 发送

    /// <summary>
    /// 登记一个子通道，返回它的种类字节。分配顺序 = 模块安装顺序，跨设备一致；
    /// 用 <see cref="SendChannel"/> 发、收到时回调 <paramref name="handler"/>。
    /// </summary>
    internal byte OpenChannel(NetHandler handler)
    {
        var kind = (byte)_nextChannelKind++;
        _channels[kind] = handler;
        return kind;
    }

    /// <summary>用 Mod 通道发一包：先写种类字节，再由调用方写载荷。</summary>
    internal void SendChannel(byte kind, Action<BinaryWriter> write, int toClient, int ignoreClient)
    {
        if (Main.netMode == 0) return;

        var writer = Begin();
        writer.Write(kind);
        write(writer);
        End(toClient, ignoreClient);
    }

    /// <summary>直接发一串已序列化的值（拥有固定种类字节的通道用，避免每包一个闭包）。</summary>
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

    /// <summary>内容指纹随世界数据发给新客户端：两端 mod 集合或顺序不同就当场能看见。</summary>
    private void SendFingerprint(int toClient)
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

        // 只写有数据的全局对象，编号就是它在注册表里的位置。
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

        // 客户端只发自己拥有的弹幕；服务器要转发客户端打来的弹幕，按 owner 过滤会把它们全漏掉。
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

    /// <summary>全局对象个数是回填的：先占一字节，写完再回到那个位置填真实个数。</summary>
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

            // 是我们的包：半读的流不能再交回原版。
        }
        catch (Exception e)
        {
            Safe.Report("net.收包", e);
        }
    }

    /// <summary>
    /// 联机元信息：服务器把指纹随世界数据发给新客户端，客户端回一条自己的。
    /// 两端不一致只记一条错，不断连接——玩家至少能从日志里看到是哪一侧的 mod 装错了。
    /// </summary>
    private void ReceiveMeta(Il2CppSystem.IO.BinaryReader reader, int from)
    {
        var action = reader.ReadByte();
        if (action != MetaFingerprint) return;

        var remote = reader.ReadUInt64();

        if (ContentFingerprint.Value == remote)
        {
            Log.Info($"[net] {ContentFingerprint.Summary()}，与对端一致");
            return;
        }

        Safe.Once("net.fingerprint", "[net] " + ContentFingerprint.Describe(remote), true);

        // 客户端把指纹回给服务器，让服务器那一侧也留下证据（只回一次，不回环）。
        if (Main.netMode != 1) return;

        SendChannel(MetaKind, writer =>
        {
            writer.Write(MetaFingerprint);
            writer.Write(ContentFingerprint.Value);
        }, -1, -1);
    }

    /// <summary>子通道的包：没登记的种类直接丢弃（同一条消息号下别家发的包不该被误读）。</summary>
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

    /// <summary>弹幕槽位会被复用：四元组是唯一能区分新旧弹幕的组合。</summary>
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

        // 物品的附属数据必须在原版包之前发：接收侧是"等原版把物品摆好"再贴，晚到就贴不上了。
        if (!hijacked && Main.netMode != 0)
            Safe.Run("net.item", () => _items.BeforeSend(msgType, remoteClient, ignoreClient, number, (int)number2));

        if (!hijacked)
        {
            orig(msgType, remoteClient, ignoreClient, text, number, number2, number3, number4, number5, number6,
                 number7);
        }

        // 原版之后补发 Mod 数据：被拦下的消息同样要补，否则两端会永久不同步。
        if (Main.netMode == 0) return;

        switch (msgType)
        {
            case 7:
                if (Main.netMode == 1) return;

                SendFingerprint(remoteClient);

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
