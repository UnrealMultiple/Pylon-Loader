using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>护士三钩子 + 商店买卖四钩子：护士建计划并由 ModifyNurseHeal/Price 链参与算价、付费后派 PostNurseHeal；商店在 HandleShopSlot 建购买作用域，BuyItem/SellItem 做 Can* 全员 AND 与成交后通知。</summary>
internal static class PlayerNurseShopHooks
{
    private sealed class NursePlan
    {
        public Player Player = null!;
        public NPC? Nurse;
        public int Health;
        public bool RemoveDebuffs = true;
        public string Chat = "";
        public int Price;
        public bool Allowed = true;
        public bool Paid;
    }

    private sealed class ShopScope
    {
        public Player Player = null!;
        public NPC? Vendor;
        public Item[] Inventory = [];
        public Item? Item;
        public bool Bought;
    }

    [ThreadStatic] private static NursePlan? _nurse;
    [ThreadStatic] private static ShopScope? _shop;

    private static IModLog Log => LoaderHost.Current.Log;

    internal static void Install()
    {
        On.Terraria.Main.GetNurseHealCost += GetNurseHealCost;
        On.Terraria.Main.NPCChatText_DoNurseHeal += DoNurseHeal;
        On.Terraria.Player.BuyItem += BuyItem;
        On.Terraria.Player.SellItem += SellItem;
        On.Terraria.UI.ItemSlot.HandleShopSlot += HandleShopSlot;
        Log.Info("[nurseshop] 已挂（护士三钩子 + 买卖四钩子）");
    }

    private static bool NurseActive() =>
        PlayerPipeline.Instance.ModsActive(HookId.ModifyNurseHeal) ||
        PlayerPipeline.Instance.ModsActive(HookId.ModifyNursePrice) ||
        PlayerPipeline.Instance.ModsActive(HookId.PostNurseHeal);

    private static NPC? VendorOf(Player player)
    {
        var talk = player.talkNPC;
        return (uint)talk < 200u ? Main.npc[talk] : null;
    }

    private static Item[] CopyOf(Il2CppReferenceArray<Item>? items)
    {
        if (items is null || items.Length == 0) return [];
        var copy = new Item[items.Length];
        for (var i = 0; i < copy.Length; i++) copy[i] = items[i];
        return copy;
    }

    private static NursePlan Plan(Player player)
    {
        var plan = new NursePlan
        {
            Player = player,
            Nurse = VendorOf(player),
            Health = Math.Max(0, player.statLifeMax2 - player.statLife),
            Chat = Main.npcChatText ?? "",
        };

        foreach (var mod in PlayerPipeline.Instance.ModsOf(player))
        {
            if (!mod.HookEnabled(HookId.ModifyNurseHeal)) continue;
            try
            {
                int health = plan.Health;
                bool debuffs = plan.RemoveDebuffs;
                string chat = plan.Chat;
                if (!mod.ModifyNurseHeal(plan.Nurse!, ref health, ref debuffs, ref chat)) plan.Allowed = false;
                plan.Health = Math.Max(0, health);
                plan.RemoveDebuffs = debuffs;
                plan.Chat = chat ?? "";
            }
            catch (Exception e)
            {
                Report(mod, HookId.ModifyNurseHeal, e);
            }
        }
        return plan;
    }

    private static T WithNurse<T>(NursePlan plan, Func<T> run)
    {
        var player = plan.Player;
        int max = player.statLifeMax2;
        var hidden = new Dictionary<int, bool>();
        var debuff = Main.debuff;
        if (!plan.RemoveDebuffs)
        {
            var types = player.buffType;
            for (var i = 0; i < types.Length; i++)
            {
                var type = types[i];
                if (type <= 0 || (uint)type >= (uint)debuff.Length || hidden.ContainsKey(type)) continue;
                hidden[type] = debuff[type];
                debuff[type] = false;
            }
        }
        player.statLifeMax2 = player.statLife + Math.Min(plan.Health, Math.Max(0, max - player.statLife));
        try { return run(); }
        finally
        {
            player.statLifeMax2 = max;
            foreach (var (type, value) in hidden) debuff[type] = value;
        }
    }

    private static int GetNurseHealCost(On.Terraria.Main.orig_GetNurseHealCost orig)
    {
        if (!NurseActive()) return orig();

        var player = Main.LocalPlayer;
        var plan = Plan(player);
        var price = WithNurse(plan, () => orig());
        foreach (var mod in PlayerPipeline.Instance.ModsOf(player))
        {
            if (!mod.HookEnabled(HookId.ModifyNursePrice)) continue;
            try { mod.ModifyNursePrice(plan.Nurse!, plan.Health, plan.RemoveDebuffs, ref price); }
            catch (Exception e) { Report(mod, HookId.ModifyNursePrice, e); }
        }
        return Math.Max(0, price);
    }

    private static void DoNurseHeal(On.Terraria.Main.orig_NPCChatText_DoNurseHeal orig, int healCost)
    {
        if (!NurseActive())
        {
            orig(healCost);
            return;
        }

        var player = Main.LocalPlayer;
        var plan = Plan(player);
        if (!plan.Allowed)
        {
            Main.npcChatText = plan.Chat;
            return;
        }
        plan.Price = healCost;
        var outer = _nurse;
        _nurse = plan;
        try { WithNurse(plan, () => { orig(Math.Max(1, healCost)); return 0; }); }
        finally { _nurse = outer; }
        if (!plan.Paid) return;
        foreach (var mod in PlayerPipeline.Instance.ModsOf(player))
        {
            if (!mod.HookEnabled(HookId.PostNurseHeal)) continue;
            try { mod.PostNurseHeal(plan.Nurse!, plan.Health, plan.RemoveDebuffs, plan.Price); }
            catch (Exception e) { Report(mod, HookId.PostNurseHeal, e); }
        }
    }

    private static bool BuyItem(On.Terraria.Player.orig_BuyItem orig, Player self, long price, int customCurrency)
    {
        var scope = _shop;
        bool shopping = scope is not null && Same(scope.Player, self);
        if (shopping)
        {
            foreach (var mod in PlayerPipeline.Instance.ModsOf(self))
            {
                if (!mod.HookEnabled(HookId.CanBuyItem)) continue;
                try
                {
                    if (!mod.CanBuyItem(scope!.Vendor!, scope.Inventory, scope.Item!)) return false;
                }
                catch (Exception e) { Report(mod, HookId.CanBuyItem, e); }
            }
        }

        var plan = _nurse;
        bool nursing = plan is not null && Same(plan.Player, self);
        var result = orig(self, nursing ? plan!.Price : price, customCurrency);
        if (result && shopping) scope!.Bought = true;
        if (result && nursing) plan!.Paid = true;
        return result;
    }

    private static bool SellItem(On.Terraria.Player.orig_SellItem orig, Player self, Item item, int stack)
    {
        var vendor = VendorOf(self);
        if (vendor is null) return orig(self, item, stack);

        Item[] inventory = [];
        try
        {
            var index = global::LocalUserGameState.Instance?.npcShop ?? 0;
            var storage = Main.instance.shop is null || (uint)index >= (uint)Main.instance.shop.Length ? null : Main.instance.shop[index];
            if (storage is not null) inventory = CopyOf(storage.item);
        }
        catch (Exception e)
        {
            Safe.Once("nurseshop.sellinv", "[nurseshop] 读当前商店库存失败：" + Safe.Describe(e), true);
        }

        if (PlayerPipeline.Instance.ModsActive(HookId.CanSellItem))
        {
            foreach (var mod in PlayerPipeline.Instance.ModsOf(self))
            {
                if (!mod.HookEnabled(HookId.CanSellItem)) continue;
                try
                {
                    if (!mod.CanSellItem(vendor, inventory, item)) return false;
                }
                catch (Exception e) { Report(mod, HookId.CanSellItem, e); }
            }
        }

        var result = orig(self, item, stack);
        if (!result) return result;
        foreach (var mod in PlayerPipeline.Instance.ModsOf(self))
        {
            if (!mod.HookEnabled(HookId.PostSellItem)) continue;
            try { mod.PostSellItem(vendor, inventory, item); }
            catch (Exception e) { Report(mod, HookId.PostSellItem, e); }
        }
        return result;
    }

    private static void HandleShopSlot(On.Terraria.UI.ItemSlot.orig_HandleShopSlot orig,
        Il2CppReferenceArray<Item> inv, int slot, bool rightClickIsValid, bool leftClickIsValid)
    {
        if (!PlayerPipeline.Instance.ModsActive(HookId.CanBuyItem) && !PlayerPipeline.Instance.ModsActive(HookId.PostBuyItem))
        {
            orig(inv, slot, rightClickIsValid, leftClickIsValid);
            return;
        }

        var player = Main.LocalPlayer;
        var outer = _shop;
        var scope = new ShopScope
        {
            Player = player,
            Vendor = VendorOf(player),
            Inventory = CopyOf(inv),
            Item = (uint)slot < (uint)inv.Length ? inv[slot] : null,
        };
        _shop = scope;
        try { orig(inv, slot, rightClickIsValid, leftClickIsValid); }
        finally { _shop = outer; }

        if (!scope.Bought) return;
        foreach (var mod in PlayerPipeline.Instance.ModsOf(player))
        {
            if (!mod.HookEnabled(HookId.PostBuyItem)) continue;
            try { mod.PostBuyItem(scope.Vendor!, scope.Inventory, Main.mouseItem); }
            catch (Exception e) { Report(mod, HookId.PostBuyItem, e); }
        }
    }

    private static bool Same(Player a, Player b) =>
        a.Pointer == b.Pointer;

    private static readonly HashSet<string> _reported = [];

    private static void Report(ModPlayer mod, HookId hook, Exception exception)
    {
        var key = mod.Mod?.Name + "/" + mod.GetType().Name + "/" + hook;
        if (!_reported.Add(key)) return;
        ContentErrors.Report($"[nurseshop] {mod.Mod?.Name}/{mod.GetType().Name}.{hook} 异常：{Safe.Describe(exception)}");
    }
}
