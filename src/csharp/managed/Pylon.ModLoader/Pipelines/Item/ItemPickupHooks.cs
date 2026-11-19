using Pylon.ModLoader.Content;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader.Pipelines;
/// <summary>拾取族：CanPickup/OnPickup/ItemSpace/GrabStyle 为 GlobalItem 先、ModItem 后，GrabRange 相反；异常按作者隔离并放行。</summary>
internal static class ItemPickupHooks
{
    [ThreadStatic] private static Player? _grabbing;

    [ThreadStatic] private static Item? _cachedItem;
    [ThreadStatic] private static WorldItem? _cachedWorld;

    internal static void Install()
    {
        On.Terraria.Player.GrabItems += Grabbing;
        On.Terraria.Player.CanAcceptItemIntoInventory_Item += CanAccept;
        On.Terraria.Player.GetItemGrabRange_WorldItem += GrabRangeGate;
        On.Terraria.Player.PullItem_Common += PullCommon;
        On.Terraria.Player.PullItem_Pickup += PullPickup;
        On.Terraria.Player.ItemSpace_Item += SpaceValue;
        On.Terraria.Player.ItemSpace_Item_ItemSpaceStatusRef += SpaceRef;
        On.Terraria.Player.PickupItem += Pickup;
    }

    private static void Report(string who, Exception e) =>
        ContentErrors.Report($"[item] {who} 异常：{Safe.Describe(e)}");

    private static bool Wanted(Item item, HookId hook) =>
        GlobalItemRegistry.AnyWith(hook) || ModContent.IsCustom(ContentKind.Item, item.type);

    private static ModItem? Own(Item item) => ItemPipeline.Instance.InstanceOf(item);

    #region 分派

    private static bool CanPickupOf(Item item, Player player)
    {
        foreach (var global in GlobalItemRegistry.For(item, HookId.CanPickup))
        {
            try
            {
                if (!global.CanPickup(item, player)) return false;
            }
            catch (Exception e)
            {
                Report($"{global.FullKey}.CanPickup", e);
            }
        }

        var mod = Own(item);
        if (mod is not null && mod.Plan.Has(HookId.CanPickup))
        {
            try
            {
                return mod.CanPickup(player);
            }
            catch (Exception e)
            {
                Report($"{mod.FullKey}.CanPickup", e);
            }
        }

        return true;
    }

    private static bool OnPickupOf(Item item, Player player)
    {
        foreach (var global in GlobalItemRegistry.For(item, HookId.OnPickup))
        {
            try
            {
                if (!global.OnPickup(item, player)) return false;
            }
            catch (Exception e)
            {
                Report($"{global.FullKey}.OnPickup", e);
            }
        }

        var mod = Own(item);
        if (mod is not null && mod.Plan.Has(HookId.OnPickup))
        {
            try
            {
                return mod.OnPickup(player);
            }
            catch (Exception e)
            {
                Report($"{mod.FullKey}.OnPickup", e);
            }
        }

        return true;
    }

    private static bool GrabStyleOf(Item item, Player player)
    {
        foreach (var global in GlobalItemRegistry.For(item, HookId.GrabStyle))
        {
            try
            {
                if (global.GrabStyle(item, player)) return true;
            }
            catch (Exception e)
            {
                Report($"{global.FullKey}.GrabStyle", e);
            }
        }

        var mod = Own(item);
        if (mod is not null && mod.Plan.Has(HookId.GrabStyle))
        {
            try
            {
                return mod.GrabStyle(player);
            }
            catch (Exception e)
            {
                Report($"{mod.FullKey}.GrabStyle", e);
            }
        }

        return false;
    }

    private static bool ItemSpaceOf(Item item, Player player)
    {
        foreach (var global in GlobalItemRegistry.For(item, HookId.ItemSpace))
        {
            try
            {
                if (global.ItemSpace(item, player)) return true;
            }
            catch (Exception e)
            {
                Report($"{global.FullKey}.ItemSpace", e);
            }
        }

        var mod = Own(item);
        if (mod is not null && mod.Plan.Has(HookId.ItemSpace))
        {
            try
            {
                return mod.ItemSpace(player);
            }
            catch (Exception e)
            {
                Report($"{mod.FullKey}.ItemSpace", e);
            }
        }

        return false;
    }

    private static int GrabRangeOf(Item item, Player player, int baseRange)
    {
        var range = baseRange;

        var mod = Own(item);
        if (mod is not null && mod.Plan.Has(HookId.GrabRange))
        {
            try
            {
                mod.GrabRange(player, ref range);
            }
            catch (Exception e)
            {
                Report($"{mod.FullKey}.GrabRange", e);
            }
        }

        foreach (var global in GlobalItemRegistry.For(item, HookId.GrabRange))
        {
            try
            {
                global.GrabRange(item, player, ref range);
            }
            catch (Exception e)
            {
                Report($"{global.FullKey}.GrabRange", e);
            }
        }

        return range;
    }

    #endregion

    #region 入口

    private static void Grabbing(On.Terraria.Player.orig_GrabItems orig, Player self, int i)
    {
        var outer = _grabbing;
        _grabbing = self;
        try
        {
            orig(self, i);
        }
        finally
        {
            _grabbing = outer;
        }
    }

    private static bool CanAccept(On.Terraria.Player.orig_CanAcceptItemIntoInventory_Item orig, Player self, Item item)
    {
        if (!orig(self, item)) return false;
        if (item is null || !Wanted(item, HookId.CanPickup)) return true;

        try
        {
            var w = WorldItemOf(item);
            if (w is null) return true;

            if (!CanPickupOf(item, self)) return false;
            if (_grabbing != self) return true;
            return Reach(self, w, item);
        }
        catch (Exception e)
        {
            Report("CanPickup/GrabRange", e);
            return true;
        }
    }

    private static bool Reach(Player player, WorldItem w, Item item)
    {
        var vanilla = player.GetItemGrabRange(item);
        var range = GrabRangeOf(item, player, vanilla);
        if (range == vanilla) return true;

        var at = player.position;
        var to = w.position;
        int px = (int)at.X, py = (int)at.Y, pw = player.width, ph = player.height;
        int hx = (int)to.X, hy = (int)to.Y, hw = w.width, hh = w.height;

        bool Meets(int r) => px - r < hx + hw && hx < px + pw + r && py - r < hy + hh && hy < py + ph + r;

        if (Meets(0)) return true;
        if (range < vanilla) return Meets(range);
        if (!Meets(range) || Meets(vanilla)) return true;

        var outer = _grabbing;
        _grabbing = null;
        try { Pull(player, w, item); }
        finally { _grabbing = outer; }
        return true;
    }

    private static void Pull(Player player, WorldItem w, Item item)
    {
        var status = player.ItemSpace(w);
        if (!player.CanPullItem(w, status)) return;

        w.shimmered = false;
        w.beingGrabbed = true;

        var fast = status.ItemIsGoingToVoidVault || ItemID.Sets.NebulaPickup[item.type]
            || (player.goldRing && item.IsACoin);
        if (fast) player.PullItem_Pickup(w, 12f, 5);
        else player.PullItem_Common(w, 0.75f);
    }

    private static int GrabRangeGate(On.Terraria.Player.orig_GetItemGrabRange_WorldItem orig, Player self, WorldItem itemToPickUp)
    {
        var range = orig(self, itemToPickUp);

        var item = itemToPickUp?.inner;
        if (item is null || !Wanted(item, HookId.GrabRange)) return range;

        try
        {
            return GrabRangeOf(item, self, range);
        }
        catch (Exception e)
        {
            Report("GrabRange", e);
            return range;
        }
    }

    private static void PullCommon(On.Terraria.Player.orig_PullItem_Common orig, Player self,
                                    WorldItem itemToPickUp, float xPullSpeed)
    {
        if (Styled(self, itemToPickUp)) return;
        orig(self, itemToPickUp, xPullSpeed);
    }

    private static void PullPickup(On.Terraria.Player.orig_PullItem_Pickup orig, Player self,
                                   WorldItem itemToPickUp, float speed, int acc)
    {
        if (Styled(self, itemToPickUp)) return;
        orig(self, itemToPickUp, speed, acc);
    }

    private static bool Styled(Player self, WorldItem itemToPickUp)
    {
        try
        {
            var item = itemToPickUp?.inner;
            if (item is null || !Wanted(item, HookId.GrabStyle)) return false;
            return GrabStyleOf(item, self);
        }
        catch (Exception e)
        {
            Report("GrabStyle", e);
            return false;
        }
    }

    private static Player.ItemSpaceStatus SpaceValue(On.Terraria.Player.orig_ItemSpace_Item orig, Player self, Item item)
    {
        var status = orig(self, item);
        try
        {
            if (!status.CanTakeItem && Allows(self, item)) status.CanTakeItem = true;
        }
        catch (Exception e)
        {
            Report("ItemSpace", e);
        }

        return status;
    }

    private static void SpaceRef(On.Terraria.Player.orig_ItemSpace_Item_ItemSpaceStatusRef orig, Player self, Item item,
                                 ref Player.ItemSpaceStatus status)
    {
        orig(self, item, ref status);
        try
        {
            if (!status.CanTakeItem && Allows(self, item)) status.CanTakeItem = true;
        }
        catch (Exception e)
        {
            Report("ItemSpace", e);
        }
    }

    private static bool Allows(Player self, Item item)
    {
        if (item is null || !Wanted(item, HookId.ItemSpace)) return false;
        if (item.uniqueStack && self.HasItem(item.type)) return false;
        return ItemSpaceOf(item, self);
    }

    private static void Pickup(On.Terraria.Player.orig_PickupItem orig, Player self, WorldItem itemToPickUp)
    {
        try
        {
            var item = itemToPickUp?.inner;
            if (itemToPickUp is not null && item is not null
                && Wanted(item, HookId.OnPickup) && !OnPickupOf(item, self))
            {
                itemToPickUp.TurnToAirAndSync();
                return;
            }
        }
        catch (Exception e)
        {
            Report("OnPickup", e);
        }

        orig(self, itemToPickUp);
    }

    internal static WorldItem? WorldItemOf(Item item)
    {
        var cachedItem = _cachedItem;
        var cachedWorld = _cachedWorld;
        if (cachedItem is not null && cachedWorld is not null && cachedItem.Pointer == item.Pointer)
        {
            return cachedWorld;
        }

        var all = Main.item;
        if (all is null) return null;

        var pointer = item.Pointer;
        for (var i = 0; i < all.Length; i++)
        {
            var w = all[i];
            if (w is null || !w.active) continue;

            var inner = w.inner;
            if (inner is null || inner.Pointer != pointer) continue;

            _cachedItem = item;
            _cachedWorld = w;
            return w;
        }

        return null;
    }

    #endregion
}
