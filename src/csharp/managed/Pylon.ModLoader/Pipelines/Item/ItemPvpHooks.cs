using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>近战武器来源随受伤包独立传递；转发只传上下文，不重复计算或施加伤害。</summary>
internal static class ItemPvpHooks
{
    private sealed record Attack(Player Player, Item Item);
    private sealed record Received(int Attacker, int Target, int Type, int Prefix, string Key,
                                   object[] Own, object[] Globals, long Stamp);
    [ThreadStatic] private static Attack? _local;
    [ThreadStatic] private static Received? _receiving;
    private static readonly object Gate = new();
    private static readonly Dictionary<int, List<Received>> Pending = [];
    private static bool _installed, _networkInstalled;
    private static NetPipeline? _net;
    private static byte _kind;
    private const int HurtPacket = 117;
    private static readonly HookId[] Pvp = [HookId.CanHitPvp, HookId.ModifyHitPvp, HookId.OnHitPvp];

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Terraria.Player.ItemCheck_MeleeHitPVP += Melee;
        On.Terraria.Player.CanHit += CanHit;
        On.Terraria.NetMessage.SendPlayerHurt += Send;
    }

    internal static void InstallNetwork(NetPipeline net)
    {
        if (_networkInstalled) return;
        _networkInstalled = true;
        _net = net;
        _kind = net.OpenChannel(Receive);
        On.Terraria.MessageBuffer.ProcessData += Process;
        On.Terraria.MessageBuffer.Reset += Reset;
        On.Terraria.WorldGen.clearWorld += Clear;
    }

    internal static bool AnyActive()
    {
        if (PlayerPipeline.Instance.ModsActive(HookId.CanHitPvp) || GlobalItemRegistry.AnyWith(Pvp)) return true;
        foreach (var item in ModContent.GetContent<ModItem>())
            foreach (var hook in Pvp) if (item.Plan.Has(hook)) return true;
        return false;
    }

    private static ModItem? Own(Item item) => ItemPipeline.Instance.InstanceOf(item);
    private static void Melee(On.Terraria.Player.orig_ItemCheck_MeleeHitPVP orig, Player self, Item item,
                              Rectangle rectangle, int damage, float knockback)
    {
        var outer = _local;
        _local = new Attack(self, item);
        try { orig(self, item, rectangle, damage, knockback); }
        finally { _local = outer; }
    }

    private static bool CanHit(On.Terraria.Player.orig_CanHit orig, Player self, Entity entity)
    {
        var result = orig(self, entity);
        if (!result || _local is not { } attack || attack.Player.Pointer != self.Pointer || entity is null) return result;
        var target = entity.TryCast<Player>();
        if (target is null) return result;
        foreach (var global in GlobalItemRegistry.For(attack.Item, HookId.CanHitPvp))
        {
            try { if (!global.CanHitPvp(attack.Item, self, target)) return false; }
            catch (Exception e) { Fail(global, HookId.CanHitPvp, e); }
        }
        var own = Own(attack.Item);
        if (own is not null && own.Plan.Has(HookId.CanHitPvp))
        {
            try { if (!own.CanHitPvp(attack.Item, self, target)) return false; }
            catch (Exception e) { Fail(own, HookId.CanHitPvp, e); }
        }
        var allowed = true;
        if (PlayerPipeline.Instance.ModsActive(HookId.CanHitPvp))
            foreach (var mod in PlayerPipeline.Instance.ModsOf(self))
            {
                if (!mod.HookEnabled(HookId.CanHitPvp)) continue;
                try { allowed &= mod.CanHitPvp(self, attack.Item, target); }
                catch (Exception e) { Fail(mod, HookId.CanHitPvp, e); }
            }
        return allowed;
    }

    internal static bool Source(PlayerDeathReason? reason, int target, out Player player, out Item item)
    {
        player = null!; item = null!;
        if (reason is null || reason._sourceNPCIndex >= 0 || reason._sourceProjectileLocalIndex >= 0 ||
            reason._sourceProjectileType > 0) return false;
        var index = reason._sourcePlayerIndex;
        var type = reason._sourceItemType;
        var prefix = reason._sourceItemPrefix;
        if ((uint)index >= (uint)Main.player.Length || index >= 255 || !ValidType(type)) return false;
        player = Main.player[index];
        if (player is null || !player.active) return false;
        var received = _receiving;
        if (received is not null && received.Target == target && received.Attacker == index &&
            received.Type == type && received.Prefix == prefix)
        {
            item = Fresh(type, prefix);
            ApplyPayload(item, received);
            return true;
        }
        if (_local is { } attack && attack.Player.Pointer == player.Pointer && attack.Item.type == type)
        {
            item = attack.Item;
            return true;
        }
        var slot = player.selectedItem;
        var held = (uint)slot < (uint)player.inventory.Length ? player.inventory[slot] : null;
        item = held is not null && held.type == type && held.prefix == prefix ? held : Fresh(type, prefix);
        return true;
    }

    private static bool ValidType(int type) => type > 0 &&
        (type < Terraria.ID.ItemID.Count || ModContent.Find(ContentKind.Item, type) is ModItem);

    private static Item Fresh(int type, int prefix)
    {
        var item = new Item();
        item.SetDefaults(type, null!);
        if (prefix > 0) item.Prefix(prefix);
        return item;
    }

    internal static void Modify(Item item, Player player, Player target, ref HurtModifiers modifiers)
    {
        var own = Own(item);
        if (own is not null && own.Plan.Has(HookId.ModifyHitPvp))
        {
            var previous = modifiers;
            try { own.ModifyHitPvp(item, player, target, ref modifiers); Require(modifiers); }
            catch (Exception e) { modifiers ??= previous; Fail(own, HookId.ModifyHitPvp, e); }
        }
        foreach (var global in GlobalItemRegistry.For(item, HookId.ModifyHitPvp))
        {
            var previous = modifiers;
            try { global.ModifyHitPvp(item, player, target, ref modifiers); Require(modifiers); }
            catch (Exception e) { modifiers ??= previous; Fail(global, HookId.ModifyHitPvp, e); }
        }
    }

    internal static void After(Item item, Player player, Player target, HurtInfo info)
    {
        var own = Own(item);
        if (own is not null && own.Plan.Has(HookId.OnHitPvp))
        {
            try { own.OnHitPvp(item, player, target, info); }
            catch (Exception e) { Fail(own, HookId.OnHitPvp, e); }
        }
        foreach (var global in GlobalItemRegistry.For(item, HookId.OnHitPvp))
        {
            try { global.OnHitPvp(item, player, target, info); }
            catch (Exception e) { Fail(global, HookId.OnHitPvp, e); }
        }
    }

    private static void Send(On.Terraria.NetMessage.orig_SendPlayerHurt orig, int target, PlayerDeathReason reason,
                             int damage, int direction, bool critical, bool pvp, int context, int remote, int ignore)
    {
        if (pvp && Main.netMode != 0 && _net is not null)
        {
            try
            {
                if (_receiving is { } received && received.Target == target && reason is not null &&
                    received.Attacker == reason._sourcePlayerIndex)
                    Announce(received, remote, ignore);
                else if (AnyActive() && Source(reason, target, out var player, out var item))
                    Announce(Payload(player.whoAmI, target, item), remote, ignore);
            }
            catch (Exception e) { ContentErrors.Report("[itempvp] 来源发送异常:" + Safe.Describe(e)); }
        }
        orig(target, reason!, damage, direction, critical, pvp, context, remote, ignore);
    }

    private static Received Payload(int attacker, int target, Item item)
    {
        var ownValues = new List<object>();
        var globals = new List<object>();
        var own = Own(item);
        if (own is not null && own.Plan.Has(HookId.NetSend))
        {
            var writer = new NetWriter();
            try { own.NetSend(writer); ownValues = writer.Values; }
            catch (Exception e) { Fail(own, HookId.NetSend, e); }
        }
        foreach (var global in GlobalItemRegistry.For(item, HookId.NetSend))
        {
            var writer = new NetWriter();
            try { global.NetSend(item, writer); globals.Add(new List<object> { global.FullKey, writer.Values }); }
            catch (Exception e) { Fail(global, HookId.NetSend, e); }
        }
        return new(attacker, target, item.type, item.prefix, own?.FullKey ?? "#" + item.type,
                   [.. ownValues], [.. globals], Environment.TickCount64);
    }

    private static void ApplyPayload(Item item, Received received)
    {
        var own = Own(item);
        var key = own?.FullKey ?? "#" + item.type;
        if (key != received.Key) return;
        if (own is not null && received.Own.Length > 0)
        {
            try { own.NetReceive(new NetReader([.. received.Own])); }
            catch (Exception e) { Fail(own, HookId.NetReceive, e); }
        }
        foreach (var value in received.Globals)
        {
            if (value is not object[] { Length: 2 } pair || pair[0] is not string name || pair[1] is not object[] data) continue;
            foreach (var global in GlobalItemRegistry.For(item, HookId.NetReceive))
            {
                if (global.FullKey != name) continue;
                try { global.NetReceive(item, new NetReader([.. data])); }
                catch (Exception e) { Fail(global, HookId.NetReceive, e); }
            }
        }
    }

    private static void Announce(Received source, int remote, int ignore) => _net!.SendValues(_kind,
        [source.Attacker, source.Target, source.Type, source.Prefix, source.Key, source.Own, source.Globals], remote, ignore);

    private static void Receive(Il2CppSystem.IO.BinaryReader reader, int from)
    {
        var values = NetCodec.ReadValues(reader);
        if (values.Length != 7 || values[0] is not int attacker || values[1] is not int target ||
            values[2] is not int type || values[3] is not int prefix || values[4] is not string key ||
            values[5] is not object[] own || values[6] is not object[] globals) return;
        if ((uint)attacker >= 255 || (uint)target >= 255 || !ValidType(type) || (uint)prefix > 255 ||
            Main.netMode != 1 && from != attacker) return;
        var source = new Received(attacker, target, type, prefix, key, own, globals, Environment.TickCount64);
        lock (Gate)
        {
            if (!Pending.TryGetValue(from, out var queue)) Pending[from] = queue = [];
            queue.RemoveAll(static r => Environment.TickCount64 - r.Stamp > 5000);
            if (queue.Count >= 16) queue.RemoveAt(0);
            queue.Add(source);
        }
    }

    private static Received? Take(int from, int target)
    {
        lock (Gate)
        {
            if (!Pending.TryGetValue(from, out var queue)) return null;
            queue.RemoveAll(static r => Environment.TickCount64 - r.Stamp > 5000);
            Received? found = null;
            if (queue.Count > 0 && queue[0].Target == target) { found = queue[0]; queue.RemoveAt(0); }
            if (queue.Count == 0) Pending.Remove(from);
            return found;
        }
    }

    private static void Process(On.Terraria.MessageBuffer.orig_ProcessData orig, MessageBuffer self,
                                Il2CppStructArray<byte> data, int length, ref int messageType)
    {
        var outer = _receiving;
        if (data is not null && length >= 2 && length <= data.Length && data[0] == HurtPacket)
            _receiving = Take(Main.netMode == 1 ? 256 : self.whoAmI, data[1]);
        try { orig(self, data!, length, ref messageType); }
        finally { _receiving = outer; }
    }

    private static void Reset(On.Terraria.MessageBuffer.orig_Reset orig, MessageBuffer self, bool setupActive)
    {
        lock (Gate) Pending.Remove(Main.netMode == 1 ? 256 : self.whoAmI);
        orig(self, setupActive);
    }

    private static void Clear(On.Terraria.WorldGen.orig_clearWorld orig)
    {
        lock (Gate) Pending.Clear();
        _local = null; _receiving = null;
        orig();
    }

    private static void Require(HurtModifiers? modifiers)
    {
        if (modifiers is null) throw new InvalidOperationException("PvP 修改器不能替换为 null");
    }

    private static void Fail(object author, HookId hook, Exception e) =>
        ContentErrors.Report($"[itempvp] {author.GetType().Name}.{hook} 异常:{Safe.Describe(e)}");
}
