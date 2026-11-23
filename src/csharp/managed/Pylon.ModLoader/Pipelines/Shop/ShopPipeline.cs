using Pylon.ModLoader.Content;
using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Globals;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader.Pipelines;
/// <summary>商店管线：货架装货（InventoryStorage.SetupShop）——原版店首开时收编条目并跑 ModifyShop/晶塔注入，mod 店按条目装；旅行商店在原版装货之后分派全局模板。</summary>
internal sealed class ShopPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static ShopPipeline Instance { get; } = new();

    private static readonly HashSet<NpcShop> Modified = [];

    public string Name => "shop";

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>shop 管线的安装门：有 mod 店、晶塔货架或商店改写钩子时才接管装货，有旅行商店覆写时才接管旅行店。</summary>
    internal static class Gates
    {
        internal struct Setup : IGate
        {
            public static bool Wanted() => HasShopContent();

            public static void Install()
            {
                On.Terraria.InventoryStorage.SetupShop += SetupShop;

                Log.Info("[shop] 已挂（货架装货 / 原版商店收编 / 旅行商店）");
            }
        }

        internal struct Travel : IGate
        {
            public static bool Wanted() => TravelShopOverridden();

            public static void Install() => On.Terraria.InventoryStorage.SetupTravelShop += SetupTravelShop;
        }
    }

    public bool Install() => GateHub<ShopPipeline>.Run();

    private static bool HasShopContent() =>
        HookGuard.Wants<ModNpc>(HookId.AddShops, HookId.ModifyActiveShop) ||
        ModContent.GetContent<ModPylon>().Count > 0 ||
        GlobalNpcRegistry.AnyWith(HookId.ModifyShop, HookId.ModifyActiveShop) ||
        NpcShopDatabase.AllShops.Any(static shop => shop is NpcShop npcShop && !npcShop.Vanilla);

    private static bool TravelShopOverridden() =>
        GlobalNpcRegistry.All.Any(static global => HookGuard.Overrides(global.GetType(), typeof(GlobalNpc), nameof(GlobalNpc.SetupTravelShop)));

    public void Refresh()
    {
    }

    internal void Build()
    {
    }

    private static void SetupTravelShop(On.Terraria.InventoryStorage.orig_SetupTravelShop orig)
    {
        orig();
        var native = Main.travelShop;
        if (native is null) return;

        var items = new int[native.Length];
        for (var i = 0; i < items.Length; i++) items[i] = native[i];
        var nextSlot = 0;
        while (nextSlot < items.Length && items[nextSlot] != 0) nextSlot++;

        foreach (var global in GlobalNpcRegistry.All)
        {
            try
            {
                global.SetupTravelShop(items, ref nextSlot);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[shop] {global.Name}.SetupTravelShop 异常:{Safe.Dump(e)}");
            }
        }

        for (var i = 0; i < items.Length; i++) native[i] = items[i];
    }

    private static void SetupShop(On.Terraria.InventoryStorage.orig_SetupShop orig, InventoryStorage self, int type)
    {
        orig(self, type);
        NpcShopDatabase.Initialize();

        if (!NpcShopDatabase.ByIndex.TryGetValue(type, out var shop))
        {
            MarkShopItems(self, NpcOf(type), "Shop");
            return;
        }

        try
        {
            Fill(self, shop);
        }
        catch (Exception e)
        {
            ContentErrors.Line($"[shop] NPC shop {shop.FullName}：{Safe.Dump(e)}");
        }
    }

    private static void Fill(InventoryStorage storage, NpcShop shop)
    {
        var native = storage.item;
        if (native is null) return;

        var npc = TalkingTo();
        if (npc is null || npc.type != shop.NpcType) npc = null;

        if (shop.Vanilla && !shop.Modified && LoaderHost.Current.Phase == LoaderPhase.Ready)
        {
            shop.Modified = true;
            for (var i = 0; i < native.Length; i++)
            {
                var item = native[i];
                if (item is not null && item.type > 0) shop.Add(new NpcShop.Entry(item.Clone()) { Native = true });
            }
            ModifyShop(shop);
            AddShopEntries(shop);
            shop.FinishSetup();
        }

        var items = new Item[native.Length];
        var slot = 0;
        var limit = Math.Max(0, items.Length - (shop.FillLastSlot ? 0 : 1));

        if (shop.Vanilla)
        {
            for (var i = 0; i < native.Length && slot < limit; i++)
            {
                var item = native[i];
                if (item is null || item.type <= 0) continue;

                NpcShop.Entry? entry = null;
                foreach (var candidate in shop.Entries)
                {
                    if (candidate.Native && candidate.Item.type == item.type)
                    {
                        entry = candidate;
                        break;
                    }
                }

                if (entry is not null)
                {
                    if (entry.Disabled || !entry.ConditionsMet()) continue;
                    item = entry.Item.Clone();
                    native[slot] = item;
                    if (npc is not null) entry.OnShopOpen(item, npc);
                }

                native[slot] = item;
                items[slot++] = item;
            }
        }

        foreach (var entry in shop.Entries)
        {
            if (entry.Native || entry.Disabled) continue;

            var available = entry.ConditionsMet();
            if (!available && !entry.SlotReserved) continue;
            if (slot >= limit)
            {
                Log.Warn($"[shop] NPC shop {shop.FullName}: item {entry.Item.type} exceeds the mobile inventory capacity");
                break;
            }

            var item = available ? entry.Item.Clone() : NpcShop.Entry.Create(0);
            native[slot] = item;
            if (available && npc is not null) entry.OnShopOpen(item, npc);
            items[slot++] = item;
        }

        if (npc is not null) ModifyActiveShop(npc, shop.FullName, items);

        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i] ?? NpcShop.Entry.Create(0);
            item.isAShopItem = true;
            native[i] = item;
        }
    }

    internal static void ModifyShop(NpcShop shop)
    {
        if (!Modified.Add(shop)) return;

        if (!GlobalNpcRegistry.AnyWith(HookId.ModifyShop)) return;

        NPC? sample = null;
        if (ContentSamples.NpcsByNetId is { } samples && samples.TryGetValue(shop.NpcType, out var found)) sample = found;

        foreach (var global in GlobalNpcRegistry.All)
        {
            if (!global.Plan.Has(HookId.ModifyShop)) continue;
            if (sample is not null && !global.AppliesToEntity(sample, false)) continue;

            try
            {
                global.ModifyShop(shop);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[shop] {global.Name}.ModifyShop 异常：{Safe.Dump(e)}");
            }
        }
    }

    internal static void AddShopEntries(NpcShop shop)
    {
        if (NpcShopDatabase.NoPylons.Contains(shop.FullName)) return;

        foreach (var pylon in ModContent.GetContent<ModPylon>())
        {
            if (pylon.PylonType < 0) continue;

            try
            {
                if (pylon.GetNPCShopEntry() is { } entry) shop.Add(entry);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[pylon] {pylon.Name}.GetNPCShopEntry 异常：{Safe.Dump(e)}");
            }
        }
    }

    private static void ModifyActiveShop(NPC npc, string shopName, Item[] items)
    {
        if (NpcPipeline.Instance.InstanceOf(npc) is { } mod && mod.HookEnabled(HookId.ModifyActiveShop))
        {
            try
            {
                mod.ModifyActiveShop(shopName, items);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[shop] {mod.FullKey}.ModifyActiveShop 异常：{Safe.Dump(e)}");
            }
        }

        foreach (var global in GlobalNpcRegistry.For(npc, HookId.ModifyActiveShop))
        {
            try
            {
                global.ModifyActiveShop(npc, shopName, items);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[shop] {global.FullKey}.ModifyActiveShop 异常：{Safe.Dump(e)}");
            }
        }
    }

    private static void MarkShopItems(InventoryStorage storage, NPC? npc, string shopName)
    {
        var slots = storage.item;
        if (slots is null) return;

        var items = new Item[slots.Length];
        for (var i = 0; i < items.Length; i++) items[i] = slots[i];

        if (npc is not null) ModifyActiveShop(npc, shopName, items);

        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item is not null && item.type > 0) item.isAShopItem = true;
            slots[i] = item;
        }
    }

    private static NPC? TalkingTo()
    {
        var talk = Main.player[Main.myPlayer].talkNPC;
        var npcs = Main.npc;
        if (talk < 0 || npcs is null || talk >= npcs.Length) return null;
        return npcs[talk];
    }

    private static NPC? NpcOf(int npcType)
    {
        var npcs = Main.npc;
        if (npcs is null) return null;

        for (var index = 0; index < npcs.Length; index++)
        {
            var npc = npcs[index];
            if (npc is not null && npc.active && npc.type == npcType) return npc;
        }

        return null;
    }
}
