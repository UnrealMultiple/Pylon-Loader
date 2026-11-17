using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>商店管线：只接管 mod 槽号的装货（InventoryStorage.SetupShop），原版普通商店交给游戏，旅行商店在原版装货之后分派全局模板。</summary>
internal sealed class ShopPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static ShopPipeline Instance { get; } = new();

    private static readonly HashSet<NpcShop> Modified = [];

    public string Name => "shop";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.InventoryStorage.SetupShop += SetupShop;
        On.Terraria.InventoryStorage.SetupTravelShop += SetupTravelShop;

        Log.Info("[shop] 已挂（mod 槽号装货 / 原版商店改写 / 旅行商店）");
        return true;
    }

    public void Refresh() => ShopTable.Rebuild();

    internal void Build()
    {
        ShopTable.Rebuild();

        if (ShopTable.Count > 0) Log.Info($"[shop] 安装：{ShopTable.Count} 家店");
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
                ContentErrors.Report($"[shop] {global.Name}.SetupTravelShop 异常:{Safe.Describe(e)}");
            }
        }

        for (var i = 0; i < items.Length; i++) native[i] = items[i];
    }

    private static void SetupShop(On.Terraria.InventoryStorage.orig_SetupShop orig, InventoryStorage self, int type)
    {
        if (ShopTable.Of(type) is not { } shop)
        {
            orig(self, type);
            ActiveShop(self, NpcOf(type), "Shop");
            return;
        }

        ModifyShop(shop);
        Fill(self, shop);
        ActiveShop(self, NpcOf(shop.NpcType), shop.Name);
    }

    private static void ModifyShop(NpcShop shop)
    {
        if (!Modified.Add(shop) || !GlobalNpcRegistry.AnyWith(HookId.ModifyShop)) return;

        NPC? sample = null;
        if (ContentSamples.NpcsByNetId is { } samples && samples.TryGetValue(shop.NpcType, out var found)) sample = found;

        foreach (var global in GlobalNpcRegistry.All)
        {
            if (!global.Plan.Has(HookId.ModifyShop)) continue;
            if (sample is not null && !global.AppliesToEntity(sample, false)) continue;

            try { global.ModifyShop(shop); }
            catch (Exception e) { ContentErrors.Report($"[shop] {global.Name}.ModifyShop 异常：{Safe.Describe(e)}"); }
        }
    }

    private static void ActiveShop(InventoryStorage storage, NPC? npc, string shopName)
    {
        var slots = storage.item;
        if (slots is null) return;

        var items = new Item[slots.Length];
        for (var i = 0; i < items.Length; i++) items[i] = slots[i];

        if (npc is not null)
        {
            if (NpcPipeline.Instance.InstanceOf(npc) is { } mod && mod.HookEnabled(HookId.ModifyActiveShop))
            {
                try { mod.ModifyActiveShop(shopName, items); }
                catch (Exception e) { ContentErrors.Report($"[shop] {mod.FullKey}.ModifyActiveShop 异常：{Safe.Describe(e)}"); }
            }

            foreach (var global in GlobalNpcRegistry.For(npc, HookId.ModifyActiveShop))
            {
                try { global.ModifyActiveShop(npc, shopName, items); }
                catch (Exception e) { ContentErrors.Report($"[shop] {global.FullKey}.ModifyActiveShop 异常：{Safe.Describe(e)}"); }
            }
        }

        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            if (item is not null && item.type > 0) item.isAShopItem = true;
            slots[i] = item;
        }
    }

    private static void Fill(InventoryStorage storage, NpcShop shop)
    {
        var entries = shop.Entries;

        storage.Resize(Math.Max(1, entries.Count));
        storage.FillWithEmptyInstances();

        var slots = storage.item;
        var slot = 0;

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];

            if (entry.Condition is not null && !Allow(entry.Condition, shop)) continue;

            var item = new Item();
            item.SetDefaults(entry.Type, null);

            if (entry.Price.HasValue)
            {
                var price = entry.Price.Value;
                if (entry.HappinessPrice) price = WithHappiness(price, shop.NpcType);
                item.shopCustomPrice = new Il2CppSystem.Nullable<int>(price);
            }
            if (entry.Currency.HasValue) item.shopSpecialCurrency = entry.Currency.Value;

            item.isAShopItem = true;

            if (slots is not null && slot < slots.Length) slots[slot++] = item;
            else storage.AddItemToShop(item);
        }

        if (slots is not null && slot > 0) storage.maxItems = Math.Max(storage.maxItems, slot);
    }

    private static int WithHappiness(int price, int npcType)
    {
        try
        {
            var helper = Main.ShopHelper;
            var npc = NpcOf(npcType);
            if (helper is null || npc is null) return price;

            var multiplier = helper.GetShoppingSettings(Main.LocalPlayer, npc).PriceAdjustment;
            if (multiplier <= 0f) return price;

            var adjusted = (int)Math.Round(price * multiplier);
            return adjusted < 1 ? 1 : adjusted;
        }
        catch (Exception e)
        {
            ContentErrors.Report("[shop] 好感度价格计算异常：" + Safe.Describe(e));
            return price;
        }
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

    private static bool Allow(Func<bool> condition, NpcShop shop)
    {
        try
        {
            return condition();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[shop] {shop.Name} 的商品条件异常：{Safe.Describe(e)}");
            return false;
        }
    }
}
