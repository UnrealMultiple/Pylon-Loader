using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>物品槽交互（悬停/Shift 点击）：作者拿到背包快照，可改写并决定是否吞掉这次交互。</summary>
internal static class PlayerSlotHooks
{
    private static IModLog Log => LoaderHost.Current.Log;

    private static PlayerPipeline Players => PlayerPipeline.Instance;

    internal static void Install()
    {
        On.Terraria.UI.ItemSlot.OverrideHover_Il2CppReferenceArray_Item_int_int += Hover;
        On.Terraria.UI.ItemSlot.OverrideLeftClick += LeftClick;
        Log.Info("[slots] 已挂（HoverSlot / ShiftClickSlot）");
    }

    private static Item[] Copy(Il2CppReferenceArray<Item>? items)
    {
        if (items is null || items.Length == 0) return [];
        var copy = new Item[items.Length];
        for (var i = 0; i < copy.Length; i++) copy[i] = items[i];
        return copy;
    }

    private static void Back(Il2CppReferenceArray<Item>? items, Item[] copy)
    {
        if (items is null) return;
        var count = Math.Min(items.Length, copy.Length);
        for (var i = 0; i < count; i++)
        {
            if (Same(copy[i], items[i])) continue;
            items[i] = copy[i];
        }
    }

    private static bool Same(Item? left, Item? right) =>
        left is null ? right is null : right is not null && left.Pointer == right.Pointer;

    private static bool Any(Player player, HookId hook, Item[] inventory, int context, int slot)
    {
        if (!Players.ModsActive(hook)) return false;
        var hit = false;
        foreach (var mod in Players.ModsOf(player))
        {
            if (!mod.HookEnabled(hook)) continue;
            try { hit |= Run(mod, hook, inventory, context, slot); }
            catch (Exception e) { Fail(mod, hook, e); }
        }
        return hit;
    }

    private static bool Run(ModPlayer mod, HookId hook, Item[] inventory, int context, int slot) =>
        hook == HookId.HoverSlot
            ? mod.HoverSlot(inventory, context, slot)
            : mod.ShiftClickSlot(inventory, context, slot);

    private static void Hover(On.Terraria.UI.ItemSlot.orig_OverrideHover_Il2CppReferenceArray_Item_int_int orig,
        Il2CppReferenceArray<Item> inventory, int context, int slot)
    {
        var copy = Copy(inventory);
        var handled = Any(Main.LocalPlayer, HookId.HoverSlot, copy, context, slot);
        Back(inventory, copy);
        if (!handled) orig(inventory, context, slot);
    }

    private static bool LeftClick(On.Terraria.UI.ItemSlot.orig_OverrideLeftClick orig,
        Il2CppReferenceArray<Item> inventory, int context, int slot)
    {
        if (Terraria.UI.ItemSlot.ShiftInUse)
        {
            var copy = Copy(inventory);
            var handled = Any(Main.LocalPlayer, HookId.ShiftClickSlot, copy, context, slot);
            Back(inventory, copy);
            if (handled) return true;
        }
        return orig(inventory, context, slot);
    }

    private static void Fail(ModPlayer mod, HookId hook, Exception e) =>
        ContentErrors.Report($"[slots] {mod.GetType().Name}.{hook} 异常:{Safe.Describe(e)}");
}
