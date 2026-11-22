using Pylon.ModLoader.Content;
using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Globals;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// mod 物品的实例状态跟着原版物品包走。
/// 原版只同步"编号 / 堆叠 / 前缀"，作者在 <see cref="ModItem.NetSend"/> 里写的字段过不了网；
/// 这里在物品包之前补一条附属包，对端在原版把物品摆好之后把它贴上去。
/// 地上实例物品包（90）只摆编号 / 位置 / 堆叠 / 前缀，作者数据同样不在其中，
/// mod 借这条路径生成的特殊物品照同一条附属包路径补齐，否则对端首包看到的是裸物品。
/// 箱子槽（32）在游戏里是精简结构、装不下实例数据，附属值由 <see cref="ChestTable"/> 按
/// (箱子, 槽位) 在本地承接，随包照发；物品从箱子进背包的那一刻起回到普通物品包路径。
/// </summary>
internal sealed class ItemSync
{
    // 原版物品包：5 = 玩家装备 / 背包槽，21 = 地上物品，32 = 箱子槽（精简结构，附属数据走 ChestTable 侧表）。
    private const byte PacketInventory = 5, PacketItem = 21;
    internal const byte PacketChest = 32;
    private const byte PacketInstanced = 90;

    private const int MaxPendingPerPeer = 64;
    private const long ExpiryMs = 10_000;

    // 客户端刚进世界时原版会重发自己的背包，那时对端还没准备好；推迟到进世界 60 帧后再补一次。
    private const int JoinSettleFrames = 60;

    internal sealed class Envelope
    {
        internal byte Packet;
        internal int Index;
        internal int Slot;
        internal int Type;
        internal string Key = "";
        internal List<object>? Own;
        internal readonly List<(int Global, List<object> Values)> Globals = [];
        internal long Stamp;
    }

    private readonly NetPipeline _net;
    private readonly byte _kind;
    private readonly ChestTable _chest;
    private readonly Dictionary<int, List<Envelope>> _pending = [];
    private readonly List<int> _defer = [];
    private int _settle;

    internal ItemSync(NetPipeline net)
    {
        _net = net;
        _kind = NetPipeline.ItemKind;
        _chest = new ChestTable();
    }

    /// <summary>挂接收侧的钩子；发送侧由 <see cref="NetPipeline"/> 在物品包之前调用。</summary>
    internal void Install()
    {
        On.Terraria.MessageBuffer.ProcessData += ProcessData;
        On.Terraria.MessageBuffer.Reset += Reset;
        On.Terraria.Player.Update += Settle;
        _chest.Install();
    }

    #region 发送

    /// <summary>原版物品包发出去之前：有附属数据的物品先单独补一条。</summary>
    internal void BeforeSend(int msgType, int toClient, int ignoreClient, int index, int slot)
    {
        if (Main.netMode == 0) return;

        if (msgType == PacketChest)
        {
            if (_chest.TryGet(index, slot, out var stored))
            {
                stored.Packet = PacketChest;
                stored.Index = index;
                stored.Slot = slot;
                Send(stored, toClient, ignoreClient);
            }

            return;
        }

        if (msgType != PacketInventory && msgType != PacketItem && msgType != PacketInstanced) return;

        var item = At(msgType, index, slot);
        if (item is null || item.stack <= 0) return;

        if (Main.netMode == 1 && _settle < JoinSettleFrames)
        {
            // 进世界初期的重发：记下槽号，等世界数据铺完再补，不然对端拿到的是空槽。
            if (msgType == PacketInventory && index == Main.myPlayer && HasPayload(item) && !_defer.Contains(slot))
                _defer.Add(slot);

            return;
        }

        var envelope = Build(item);
        if (envelope is null) return;

        envelope.Packet = (byte)msgType;
        envelope.Index = index;
        envelope.Slot = slot;

        Send(envelope, toClient, ignoreClient);
    }

    private void Send(Envelope envelope, int toClient, int ignoreClient)
    {
        var count = envelope.Globals.Count;
        var values = new List<object>(7 + count * 2)
        {
            (int)envelope.Packet,
            envelope.Index,
            envelope.Slot,
            envelope.Key,
            envelope.Own is not null,
            envelope.Own is null ? new List<object>() : envelope.Own,
            count,
        };

        for (var i = 0; i < count; i++)
        {
            values.Add(envelope.Globals[i].Global);
            values.Add(envelope.Globals[i].Values);
        }

        _net.SendValues(_kind, values, toClient, ignoreClient);
    }

    /// <summary>有作者数据的物品才建包：没有 NetSend 的普通物品一个字节都不发。</summary>
    internal static Envelope? Build(Item item)
    {
        var mod = ModContent.Find(ContentKind.Item, item.type) as ModItem;
        Envelope? envelope = null;

        if (mod is not null && mod.Plan.Has(HookId.NetSend))
        {
            var own = new NetWriter();
            if (!TryRun(mod.FullKey + ".NetSend", () => mod.NetSend(own))) return null;

            if (own.Values.Count > 0) envelope = new Envelope { Key = mod.FullKey, Own = own.Values };
        }

        var globals = GlobalItemRegistry.For(item, HookId.NetSend);
        for (var i = 0; i < globals.Count; i++)
        {
            var global = globals[i];
            if (!global.Plan.Has(HookId.NetSend)) continue;

            var slot = IndexOf(global);
            if (slot < 0) continue;

            var writer = new NetWriter();
            if (!TryRun(global.Name + ".NetSend", () => global.NetSend(item, writer))) continue;

            envelope ??= new Envelope { Key = mod is not null ? mod.FullKey : "#" + item.type };
            envelope.Globals.Add((slot, writer.Values));
        }

        return envelope;
    }

    /// <summary>一个 mod 抛错不该让这件物品过不了网，也不该带走同包里的其它全局对象。</summary>
    private static bool TryRun(string label, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception e)
        {
            Safe.Report(label, e);
            return false;
        }
    }

    private static bool HasPayload(Item item)
    {
        if (ModContent.Find(ContentKind.Item, item.type) is ModItem mod && mod.Plan.Has(HookId.NetSend)) return true;
        return GlobalItemRegistry.For(item, HookId.NetSend).Count > 0;
    }

    private static int IndexOf(GlobalItem global)
    {
        var all = GlobalItemRegistry.All;
        for (var i = 0; i < all.Count; i++)
            if (ReferenceEquals(all[i], global)) return i;

        return -1;
    }

    /// <summary>进世界 60 帧后把之前记下的槽重发一遍：那时对端的物品表已经建好了。</summary>
    private void Settle(On.Terraria.Player.orig_Update orig, Player self, int i)
    {
        orig(self, i);

        if (Main.netMode != 1 || Main.gameMenu)
        {
            _settle = 0;
            _defer.Clear();
            return;
        }

        if (i != Main.myPlayer || _settle >= JoinSettleFrames) return;
        if (++_settle < JoinSettleFrames) return;

        var me = Main.myPlayer;
        var slots = _defer.ToArray();
        _defer.Clear();

        for (var index = 0; index < slots.Length; index++)
        {
            var slot = slots[index];
            var item = PlayerSlot(Main.player[me], slot);
            if (item is null || item.stack <= 0) continue;

            Safe.Run("itemsync.重发",
                () => NetMessage.SendData(PacketInventory, -1, -1, null!, me, slot, item.prefix, 0f, 0, 0, 0));
        }
    }

    #endregion

    #region 接收

    internal void Receive(Il2CppSystem.IO.BinaryReader reader, int from)
    {
        var values = NetCodec.ReadValues(reader);
        if (values.Length < 7) return;

        var envelope = new Envelope
        {
            Packet = (byte)NetOf(values[0]),
            Index = NetOf(values[1]),
            Slot = NetOf(values[2]),
            Key = values[3] as string ?? "",
            Stamp = Environment.TickCount64,
        };

        if (values[4] is true && values[5] is object[] own) envelope.Own = [.. own];

        var count = NetOf(values[6]);
        for (var i = 0; i < count && 7 + i * 2 + 1 < values.Length; i++)
        {
            var global = NetOf(values[7 + i * 2]);
            if (values[7 + i * 2 + 1] is object[] payload) envelope.Globals.Add((global, [.. payload]));
        }

        if (envelope.Key.Length == 0) return;
        if (envelope.Packet != PacketInventory && envelope.Packet != PacketItem && envelope.Packet != PacketChest &&
            envelope.Packet != PacketInstanced) return;
        if ((envelope.Packet == PacketItem || envelope.Packet == PacketInstanced) &&
            (envelope.Index < 0 || envelope.Index > 400)) return;
        if (envelope.Packet == PacketChest && envelope.Slot < 0) return;

        lock (_pending)
        {
            if (!_pending.TryGetValue(from, out var queue)) _pending[from] = queue = [];

            Expire(queue, envelope.Stamp);
            queue.Add(envelope);
            if (queue.Count > MaxPendingPerPeer) queue.RemoveAt(0);
        }
    }

    private static void Expire(List<Envelope> queue, long now)
    {
        for (var i = queue.Count - 1; i >= 0; i--)
            if (now - queue[i].Stamp > ExpiryMs) queue.RemoveAt(i);
    }

    /// <summary>原版物品包处理完之后贴附属数据：那时槽里的物品才换成新的那件。</summary>
    private void ProcessData(On.Terraria.MessageBuffer.orig_ProcessData orig, MessageBuffer self,
                             Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte> messageData,
                             int length, out int messageType)
    {
        messageType = 0;
        if (NetReceiveHooks.Hijack(self, ref messageData, ref length, ref messageType)) return;

        var packet = -1;
        var index = -1;
        var slot = 0;

        if (messageData is not null && length >= 3 && length <= messageData.Length)
        {
            packet = messageData[0];
            if (packet == PacketInventory && length >= 4)
            {
                index = messageData[1];
                slot = messageData[2] | (messageData[3] << 8);
            }
            else if (packet == PacketItem || packet == PacketInstanced)
            {
                index = messageData[1] | (messageData[2] << 8);
            }
            else if (packet == PacketChest && length >= 4)
            {
                index = messageData[1] | (messageData[2] << 8);
                slot = messageData[3];
            }
            else
            {
                packet = -1;
            }
        }

        var envelope = packet < 0 ? null : Take(Main.netMode == 1 ? 256 : self.whoAmI, packet, index, slot);

        if (envelope is not null && packet == PacketChest)
            Safe.Run("itemsync.收包", () => _chest.Stage(index, slot, envelope));

        orig(self, messageData, length, out messageType);

        if (packet < 0) return;

        if (packet == PacketChest)
        {
            Safe.Run("itemsync.收包", () => _chest.Confirm(index, slot, envelope));
            return;
        }

        if (envelope is null) return;

        Safe.Run("itemsync.收包", () =>
        {
            var item = At(envelope.Packet, envelope.Index, envelope.Slot);
            if (item is null || !Matches(item, envelope.Key)) return;

            Apply(item, envelope);
        });
    }

    private void Reset(On.Terraria.MessageBuffer.orig_Reset orig, MessageBuffer self, bool setupActive)
    {
        var from = Main.netMode == 1 ? 256 : self.whoAmI;

        lock (_pending) _pending.Remove(from);

        orig(self, setupActive);
    }

    /// <summary>同一个槽上可能排着好几条（重发 / 连续更换）；按包号 + 位置取最早的一条。</summary>
    private Envelope? Take(int from, int packet, int index, int slot)
    {
        lock (_pending)
        {
            if (!_pending.TryGetValue(from, out var queue) || queue.Count == 0) return null;

            Expire(queue, Environment.TickCount64);

            for (var i = 0; i < queue.Count; i++)
            {
                var candidate = queue[i];
                if (candidate.Packet != packet) continue;
                if (candidate.Index != index && !(packet == PacketItem && index == 400)) continue;
                if (packet == PacketInventory && candidate.Slot != slot) continue;
                if (packet == PacketChest && candidate.Slot != slot) continue;

                queue.RemoveAt(i);
                return candidate;
            }

            return null;
        }
    }

    internal static void Apply(Item item, Envelope envelope)
    {
        if (envelope.Own is not null && ModContent.Find(ContentKind.Item, item.type) is ModItem mod)
            Safe.Run(mod.FullKey + ".NetReceive", () => mod.NetReceive(new NetReader([.. envelope.Own])));

        var all = GlobalItemRegistry.All;
        for (var i = 0; i < envelope.Globals.Count; i++)
        {
            var (slot, values) = envelope.Globals[i];
            if ((uint)slot >= (uint)all.Count) continue;

            var global = all[slot];
            Safe.Run(global.Name + ".NetReceive", () => global.NetReceive(item, new NetReader([.. values])));
        }
    }

    /// <summary>编号两边必须指向同一件 mod 物品：装载顺序不同时会错位，宁可丢掉这条也不要把数据贴错物品。</summary>
    internal static bool Matches(Item item, string key)
    {
        var actual = ModContent.Find(ContentKind.Item, item.type) as ModItem;

        if (actual is not null && actual.FullKey == key) return true;
        if (actual is null && key == "#" + item.type) return true;

        Safe.Once("itemsync.mismatch:" + key,
            $"[net] 物品 '{key}' 对端编号指向的不是同一件物品（本端 {item.type}），已丢弃这一包；两端 mod 装载顺序不一致");

        return false;
    }

    private static int NetOf(object value) => value is int number ? number : 0;

    /// <summary>附属包的定位键 → 本端物品编号；解析不了返回 0（上层当没有这条数据）。</summary>
    internal static int TypeOf(string key)
    {
        if (key.Length == 0) return 0;

        if (key[0] == '#') return int.TryParse(key.AsSpan(1), out var type) ? type : 0;

        return ModContent.TryFind(key, out ModItem? mod) ? mod.Type : 0;
    }

    #endregion

    #region 槽位

    private static Item? At(int packet, int index, int slot)
    {
        if (packet == PacketItem || packet == PacketInstanced)
        {
            var items = Main.item;
            if (index < 0 || index >= items.Length) return null;

            var world = items[index];
            return world is null || !world.active ? null : world.inner;
        }

        if (packet != PacketInventory) return null;
        if (index < 0 || index >= Main.player.Length) return null;

        var player = Main.player[index];
        return player is null || !player.active ? null : PlayerSlot(player, slot);
    }

    /// <summary>原版槽号 → 那件物品；槽号表就是游戏自己的 <see cref="PlayerItemSlotID"/>，越界一律当没这件东西。</summary>
    private static Item? PlayerSlot(Player player, int slot)
    {
        if (slot < 0) return null;

        if (slot < PlayerItemSlotID.Armor0) return In(player.inventory, slot - PlayerItemSlotID.Inventory0);
        if (slot < PlayerItemSlotID.Dye0) return In(player.armor, slot - PlayerItemSlotID.Armor0);
        if (slot < PlayerItemSlotID.Misc0) return In(player.dye, slot - PlayerItemSlotID.Dye0);
        if (slot < PlayerItemSlotID.MiscDye0) return In(player.miscEquips, slot - PlayerItemSlotID.Misc0);
        if (slot < PlayerItemSlotID.Bank1_0) return In(player.miscDyes, slot - PlayerItemSlotID.MiscDye0);
        if (slot < PlayerItemSlotID.Bank2_0) return In(player.bank?.item, slot - PlayerItemSlotID.Bank1_0);
        if (slot < PlayerItemSlotID.TrashItem) return In(player.bank2?.item, slot - PlayerItemSlotID.Bank2_0);
        if (slot == PlayerItemSlotID.TrashItem) return player.trashItem;
        if (slot < PlayerItemSlotID.Bank4_0) return In(player.bank3?.item, slot - PlayerItemSlotID.Bank3_0);

        return null;
    }

    private static Item? In(Item[]? items, int index) =>
        items is not null && index >= 0 && index < items.Length ? items[index] : null;

    #endregion
}
