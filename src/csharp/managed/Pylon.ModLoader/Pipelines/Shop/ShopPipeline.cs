using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 商店管线：只接管 mod 槽号的装货（`InventoryStorage.SetupShop`），原版商店原样交给游戏。
/// NPC 聊天按钮、原版商店改写本版不接，见 docs/pipeline/content-shop.md。
/// </summary>
internal sealed class ShopPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static ShopPipeline Instance { get; } = new();

    public string Name => "shop";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.InventoryStorage.SetupShop += SetupShop;

        Log.Info("[shop] 已挂（mod 槽号装货）；聊天按钮与原版商店改写本版不接");
        return true;
    }

    public void Refresh() => ShopTable.Rebuild();

    /// <summary>安装期一次：收表。</summary>
    internal void Build()
    {
        ShopTable.Rebuild();

        if (ShopTable.Count > 0) Log.Info($"[shop] 安装：{ShopTable.Count} 家店");
    }

    // ===================== 装货 =====================

    private static void SetupShop(On.Terraria.InventoryStorage.orig_SetupShop orig, InventoryStorage self, int type)
    {
        if (ShopTable.Of(type) is not { } shop)
        {
            orig(self, type);
            return;
        }

        Fill(self, shop);
    }

    /// <summary>按作者的表现填：条件逐条求值，价格/货币覆盖游戏自己的。</summary>
    private static void Fill(InventoryStorage storage, NpcShop shop)
    {
        var entries = shop.Entries;

        storage.Resize(Math.Max(1, entries.Count));
        storage.FillWithEmptyInstances();

        // AddItemToShop 在这版不会追加（货架永远是空的）；直接写 item 槽。
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

            // 手机端货架只认 isAShopItem 的条目；不设就是空店。
            item.isAShopItem = true;

            if (slots is not null && slot < slots.Length) slots[slot++] = item;
            else storage.AddItemToShop(item);
        }

        if (slots is not null && slot > 0) storage.maxItems = Math.Max(storage.maxItems, slot);
    }

    /// <summary>
    /// 价格乘好感度：<c>Main.ShopHelper.GetShoppingSettings</c> 给出这个 NPC 对当前玩家的 <c>PriceAdjustment</c>
    /// （0.75 ~ 1.5，原版城镇商店用的就是这个）。拿不到（不在店里 / 没有 NPC）就原价。
    /// </summary>
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

    /// <summary>当前对话的那个 NPC（按类型号找活着的那一个）；没有就 null。</summary>
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
