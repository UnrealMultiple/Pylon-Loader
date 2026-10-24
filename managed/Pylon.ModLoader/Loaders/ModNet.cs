using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;
using Terraria.Localization;
using Terraria.Net;

namespace Pylon.ModLoader
{
    /// <summary>Mod 数据过 NetModule 通道（消息 82，id 0xB17E）：ModPacket、NetSend/NetReceive、SendExtraAI/ReceiveExtraAI、Global NetSend/NetReceive；载荷是类型标记的二进制流。</summary>
    internal static class ModNet
    {
        private const ushort Id = 0xB17E;
        private const int MaxBytes = 65000;

        private const byte KindPacket = 1, KindWorld = 2, KindNpc = 3, KindProjectile = 4;

        private static readonly Dictionary<Type, OverridePlan> Plans = new();
        private static bool _installed;

        internal static void Install(Harmony harmony, ModLogger log)
        {
            if (_installed || harmony == null) return;
            _installed = true;

            GameHooks.Patch(harmony, log, typeof(ModNet), typeof(NetManager), "Read",
                new[] { typeof(Il2CppSystem.IO.BinaryReader), typeof(int), typeof(int) }, nameof(ReadPrefix));

            GameHooks.Patch(harmony, log, typeof(ModNet), typeof(NetMessage), "SendData",
                new[]
                {
                    typeof(int), typeof(int), typeof(int), typeof(NetworkText), typeof(int),
                    typeof(float), typeof(float), typeof(float), typeof(int), typeof(int), typeof(int),
                }, nameof(SendPrefix), nameof(SendPostfix));
        }

        private static bool Overrides(Mod mod, string method)
        {
            var type = mod.GetType();
            if (!Plans.TryGetValue(type, out var plan))
            {
                plan = OverridePlan.Build(type, typeof(Mod));
                Plans[type] = plan;
            }
            return plan.Has(method);
        }

        // ===================== 发送 =====================

        internal static void SendPacket(ModPacket packet, int toClient, int ignoreClient)
        {
            if (Main.netMode == 0 || packet?.Mod == null) return;

            var payload = new MemoryStream();
            using (var writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, true))
            {
                writer.Write(KindPacket);
                writer.Write(packet.Mod.Name);
                WriteValues(writer, packet.Values);
            }
            Send(payload.ToArray(), toClient, ignoreClient);
        }

        private static void SendWorld(Mod mod, int toClient, int ignoreClient)
        {
            var values = new NetWriter();
            try { mod.NetSend(values); }
            catch (Exception e) { Safe.Report(mod.Name + ".NetSend", e); return; }
            if (values.Values.Count == 0) return;

            var payload = new MemoryStream();
            using (var writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, true))
            {
                writer.Write(KindWorld);
                writer.Write(mod.Name);
                WriteValues(writer, values.Values);
            }
            Send(payload.ToArray(), toClient, ignoreClient);
        }

        private static void SendNpc(int index, int remoteClient, int ignoreClient)
        {
            if (Main.netMode == 1 || index < 0 || index >= Main.npc.Length) return;
            var npc = Main.npc[index];
            if (npc == null || !npc.active) return;

            var own = new NetWriter();
            (ModContent.Find(ContentKind.Npc, npc.type) as ModNpc)?.SendExtraAI(npc, own);

            var globals = new List<(string Name, NetWriter Writer)>();
            foreach (var global in GlobalNpcRegistry.All)
            {
                var writer = new NetWriter();
                try { global.NetSend(npc, writer); }
                catch (Exception e) { Safe.Report(global.Name + ".NetSend", e); continue; }
                if (writer.Values.Count > 0) globals.Add((global.GetType().Name, writer));
            }
            if (own.Values.Count == 0 && globals.Count == 0) return;

            var payload = new MemoryStream();
            using (var writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, true))
            {
                writer.Write(KindNpc);
                writer.Write(index);
                writer.Write(npc.type);
                WriteValues(writer, own.Values);
                writer.Write((byte)globals.Count);
                foreach (var (name, values) in globals)
                {
                    writer.Write(name);
                    WriteValues(writer, values.Values);
                }
            }
            Send(payload.ToArray(), remoteClient, ignoreClient);
        }

        private static void SendProjectile(int index, int remoteClient, int ignoreClient)
        {
            if (index < 0 || index >= Main.projectile.Length) return;
            var projectile = Main.projectile[index];
            if (projectile == null || !projectile.active) return;
            if (Main.netMode != 1 && projectile.owner != Main.myPlayer && projectile.owner != 255) return;

            var own = new NetWriter();
            (ModContent.Find(ContentKind.Projectile, projectile.type) as ModProjectile)?.SendExtraAI(projectile, own);

            var globals = new List<(string Name, NetWriter Writer)>();
            foreach (var global in GlobalProjectileRegistry.All)
            {
                var writer = new NetWriter();
                try { global.NetSend(projectile, writer); }
                catch (Exception e) { Safe.Report(global.Name + ".NetSend", e); continue; }
                if (writer.Values.Count > 0) globals.Add((global.GetType().Name, writer));
            }
            if (own.Values.Count == 0 && globals.Count == 0) return;

            var key = projectile.key;
            var payload = new MemoryStream();
            using (var writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, true))
            {
                writer.Write(KindProjectile);
                writer.Write(projectile.owner);
                writer.Write(key.Spawner);
                writer.Write(key.Index);
                writer.Write(key.Generation);
                writer.Write(projectile.type);
                WriteValues(writer, own.Values);
                writer.Write((byte)globals.Count);
                foreach (var (name, values) in globals)
                {
                    writer.Write(name);
                    WriteValues(writer, values.Values);
                }
            }
            Send(payload.ToArray(), remoteClient, ignoreClient);
        }

        private static void Send(byte[] payload, int toClient, int ignoreClient)
        {
            if (payload.Length > MaxBytes)
            {
                Safe.Once("modnet.size", $"Mod 网络包 {payload.Length} 字节，超过 {MaxBytes}；已丢弃", true);
                return;
            }

            var packet = new NetPacket(Id, payload.Length);
            packet.Writer.Write(payload);
            packet.ShrinkToFit();

            var net = NetManager.Instance;
            if (Main.netMode == 1) net.SendToServer(packet);
            else if (toClient >= 0) net.SendToClient(packet, toClient);
            else net.Broadcast(packet, ignoreClient);
        }

        // ===================== 接收 =====================

        private static bool ReadPrefix(NetManager __instance, Il2CppSystem.IO.BinaryReader reader, int userId,
                                       int readLength)
        {
            if (readLength < 2) return true;
            var stream = reader.BaseStream;
            var start = stream.Position;
            try
            {
                if (reader.ReadUInt16() != Id)
                {
                    stream.Position = start;
                    return true;
                }

                var kind = reader.ReadByte();
                var from = Main.netMode == 1 ? 256 : userId;
                switch (kind)
                {
                    case KindPacket: ReceivePacket(reader, from); break;
                    case KindWorld: ReceiveWorld(reader); break;
                    case KindNpc: ReceiveNpc(reader); break;
                    case KindProjectile: ReceiveProjectile(reader); break;
                }
                return false;
            }
            catch (Exception e)
            {
                Safe.Report("ModNet 收包", e);
                return false;
            }
        }

        private static void ReceivePacket(Il2CppSystem.IO.BinaryReader reader, int from)
        {
            var modName = reader.ReadString();
            var values = ReadValues(reader);
            if (!ModLoader.TryGetMod(modName, out var mod)) return;
            try { mod.HandlePacket(new NetReader(values), from); }
            catch (Exception e) { Safe.Report(mod.Name + ".HandlePacket", e); }
        }

        private static void ReceiveWorld(Il2CppSystem.IO.BinaryReader reader)
        {
            var modName = reader.ReadString();
            var values = ReadValues(reader);
            if (!ModLoader.TryGetMod(modName, out var mod)) return;
            try { mod.NetReceive(new NetReader(values)); }
            catch (Exception e) { Safe.Report(mod.Name + ".NetReceive", e); }
        }

        private static void ReceiveNpc(Il2CppSystem.IO.BinaryReader reader)
        {
            var index = reader.ReadInt32();
            var type = reader.ReadInt32();
            var ownValues = ReadValues(reader);
            var globalCount = reader.ReadByte();
            var globals = new List<(string Name, object[] Values)>(globalCount);
            for (var i = 0; i < globalCount; i++)
                globals.Add((reader.ReadString(), ReadValues(reader)));

            if (index < 0 || index >= Main.npc.Length) return;
            var npc = Main.npc[index];
            if (npc == null || !npc.active || npc.type != type) return;

            (ModContent.Find(ContentKind.Npc, type) as ModNpc)?.ReceiveExtraAI(npc, new NetReader(ownValues));
            ApplyGlobals(GlobalNpcRegistry.All, globals,
                (global, values) => global.NetReceive(npc, new NetReader(values)));
        }

        private static void ReceiveProjectile(Il2CppSystem.IO.BinaryReader reader)
        {
            var owner = reader.ReadInt32();
            var spawner = reader.ReadInt32();
            var keyIndex = reader.ReadInt32();
            var generation = reader.ReadInt32();
            var type = reader.ReadInt32();
            var ownValues = ReadValues(reader);
            var globalCount = reader.ReadByte();
            var globals = new List<(string Name, object[] Values)>(globalCount);
            for (var i = 0; i < globalCount; i++)
                globals.Add((reader.ReadString(), ReadValues(reader)));

            var projectile = FindProjectile(owner, spawner, keyIndex, generation, type);
            if (projectile == null) return;

            (ModContent.Find(ContentKind.Projectile, type) as ModProjectile)?.ReceiveExtraAI(
                projectile, new NetReader(ownValues));
            ApplyGlobals(GlobalProjectileRegistry.All, globals,
                (global, values) => global.NetReceive(projectile, new NetReader(values)));
        }

        private static Projectile FindProjectile(int owner, int spawner, int keyIndex, int generation, int type)
        {
            foreach (var projectile in Main.projectile)
            {
                if (projectile == null || !projectile.active) continue;
                if (projectile.owner != owner || projectile.type != type) continue;
                var key = projectile.key;
                if (key.Spawner == spawner && key.Index == keyIndex && key.Generation == generation)
                    return projectile;
            }
            return null;
        }

        private static void ApplyGlobals<T>(IReadOnlyList<T> globals, List<(string Name, object[] Values)> data,
                                            Action<T, object[]> apply)
        {
            foreach (var (name, values) in data)
            {
                foreach (var global in globals)
                {
                    if (global.GetType().Name != name) continue;
                    try { apply(global, values); }
                    catch (Exception e) { Safe.Report(name + ".NetReceive", e); }
                }
            }
        }

        // ===================== 原生包前后 =====================

        private static bool SendPrefix(int msgType, int remoteClient, int ignoreClient, NetworkText text,
                                       int number, float number2, float number3, float number4,
                                       int number5, int number6, int number7)
        {
            if (Main.netMode == 0) return true;
            var who = Main.netMode == 2 && remoteClient >= 0 ? remoteClient : 256;
            foreach (var mod in ModLoader.Mods)
            {
                if (!Overrides(mod, nameof(Mod.HijackSendData))) continue;
                try
                {
                    if (mod.HijackSendData(who, msgType, remoteClient, ignoreClient, text, number, number2,
                            number3, number4, number5, number6, number7))
                    {
                        return false;
                    }
                }
                catch (Exception e) { Safe.Report(mod.Name + ".HijackSendData", e); }
            }
            return true;
        }

        private static void SendPostfix(int msgType, int remoteClient, int ignoreClient, int number)
        {
            if (Main.netMode == 0) return;
            switch (msgType)
            {
                case 7:
                    if (Main.netMode != 1)
                        foreach (var mod in ModLoader.Mods)
                            if (Overrides(mod, nameof(Mod.NetSend)))
                                Safe.Run(mod.Name + ".NetSend", () => SendWorld(mod, remoteClient, ignoreClient));
                    break;
                case 23:
                    Safe.Run("ModNet.NPC", () => SendNpc((int)number, remoteClient, ignoreClient));
                    break;
                case 27:
                    Safe.Run("ModNet.Projectile", () => SendProjectile((int)number, remoteClient, ignoreClient));
                    break;
            }
        }

        // ===================== 值序列化 =====================

        private static void WriteValues(BinaryWriter writer, IReadOnlyList<object> values)
        {
            writer.Write((ushort)values.Count);
            for (var i = 0; i < values.Count; i++) WriteValue(writer, values[i]);
        }

        private static object[] ReadValues(Il2CppSystem.IO.BinaryReader reader)
        {
            var count = reader.ReadUInt16();
            var values = new object[count];
            for (var i = 0; i < count; i++) values[i] = ReadValue(reader);
            return values;
        }

        private static void WriteValue(BinaryWriter writer, object value)
        {
            switch (value)
            {
                case null: writer.Write((byte)0); break;
                case byte v: writer.Write((byte)1); writer.Write(v); break;
                case sbyte v: writer.Write((byte)2); writer.Write(v); break;
                case short v: writer.Write((byte)3); writer.Write(v); break;
                case ushort v: writer.Write((byte)4); writer.Write(v); break;
                case int v: writer.Write((byte)5); writer.Write(v); break;
                case uint v: writer.Write((byte)6); writer.Write(v); break;
                case long v: writer.Write((byte)7); writer.Write(v); break;
                case ulong v: writer.Write((byte)8); writer.Write(v); break;
                case float v: writer.Write((byte)9); writer.Write(v); break;
                case double v: writer.Write((byte)10); writer.Write(v); break;
                case bool v: writer.Write((byte)11); writer.Write(v); break;
                case string v: writer.Write((byte)12); writer.Write(v); break;
                case bool[] v:
                    writer.Write((byte)13);
                    writer.Write((ushort)v.Length);
                    foreach (var flag in v) writer.Write(flag);
                    break;
                case Vector2 v:
                    writer.Write((byte)14);
                    writer.Write(v.X);
                    writer.Write(v.Y);
                    break;
                default:
                    writer.Write((byte)12);
                    writer.Write(value.ToString() ?? "");
                    break;
            }
        }

        private static object ReadValue(Il2CppSystem.IO.BinaryReader reader)
        {
            var tag = reader.ReadByte();
            switch (tag)
            {
                case 0: return null;
                case 1: return reader.ReadByte();
                case 2: return reader.ReadSByte();
                case 3: return reader.ReadInt16();
                case 4: return reader.ReadUInt16();
                case 5: return reader.ReadInt32();
                case 6: return reader.ReadUInt32();
                case 7: return reader.ReadInt64();
                case 8: return reader.ReadUInt64();
                case 9: return reader.ReadSingle();
                case 10: return reader.ReadDouble();
                case 11: return reader.ReadBoolean();
                case 12: return reader.ReadString();
                case 13:
                    var count = reader.ReadUInt16();
                    var flags = new bool[count];
                    for (var i = 0; i < count; i++) flags[i] = reader.ReadBoolean();
                    return flags;
                case 14: return new Vector2(reader.ReadSingle(), reader.ReadSingle());
                default: return null;
            }
        }
    }
}
