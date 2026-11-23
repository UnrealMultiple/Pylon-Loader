using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.UI;

namespace Pylon.ModLoader.Pipelines;
/// <summary>饰品槽准入与盔甲绘制：四个 UI 入口的准入按 ModItem→GlobalItem 链全放行；盔甲颜色与手臂辉光在绘制集就绪后问作者。</summary>
internal static class ItemEquipHooks
{
    internal static void Install()
    {
        On.Terraria.UI.ItemSlot.PickItemMovementAction += PickItemMovementAction;
        On.Terraria.UI.ItemSlot.ArmorSwap += ArmorSwap;
        On.Terraria.UI.ItemSlot.CanEquipAccessoryInSlot_Item_int += CanEquipAccessoryInSlot;
        On.Terraria.UI.ItemSlot.CanEquipAccessoryInVanitySlot += CanEquipAccessoryInVanitySlot;
        On.Terraria.UI.ItemSlot.CanEquipBothAccessories += CanEquipBothAccessories;

        On.Terraria.DataStructures.PlayerDrawSet.BoringSetup += BoringSetup;
    }

    private static bool CanEquipBothAccessories(On.Terraria.UI.ItemSlot.orig_CanEquipBothAccessories orig,
                                                Item acc1, Item acc2, bool vanity)
    {
        if (!orig(acc1, acc2, vanity)) return false;
        if (acc1 is null || acc2 is null || acc1.type <= 0 || acc2.type <= 0) return true;

        try
        {
            var player = Main.LocalPlayer;
            return Pair(acc1, acc2, player) && Pair(acc2, acc1, player);
        }
        catch (Exception e)
        {
            ContentErrors.Line("[item] CanAccessoryBeEquippedWith 异常：" + Safe.Dump(e));
            return true;
        }
    }

    private static bool Pair(Item equipped, Item incoming, Player player)
    {
        foreach (var item in new[] { equipped, incoming })
        {
            if (ItemPipeline.Instance.InstanceOf(item) is not { } m || !m.Plan.Has(HookId.CanAccessoryBeEquippedWith)) continue;

            var ok = true;
            try
            {
                ok = m.CanAccessoryBeEquippedWith(equipped, incoming, player);
            }
            catch (Exception e)
            {
                Report(m.FullKey, HookId.CanAccessoryBeEquippedWith, e);
            }
            if (!ok) return false;
        }

        foreach (var g in GlobalItemRegistry.All)
        {
            if (!g.Plan.Has(HookId.CanAccessoryBeEquippedWith)) continue;

            var ok = true;
            try
            {
                ok = g.CanAccessoryBeEquippedWith(equipped, incoming, player);
            }
            catch (Exception e)
            {
                Report(g.FullKey, HookId.CanAccessoryBeEquippedWith, e);
            }
            if (!ok) return false;
        }

        return true;
    }

    private static bool CanEquipAccessory(Item item, int slot, bool modded)
    {
        if (item is null || item.type <= 0) return true;

        var player = Main.LocalPlayer;

        if (ItemPipeline.Instance.InstanceOf(item) is { } m && m.Plan.Has(HookId.CanEquipAccessory))
        {
            try
            {
                if (!m.CanEquipAccessory(player, slot, modded)) return false;
            }
            catch (Exception e) { Report(m.FullKey, HookId.CanEquipAccessory, e); }
        }

        foreach (var g in GlobalItemRegistry.For(item, HookId.CanEquipAccessory))
        {
            try
            {
                if (!g.CanEquipAccessory(item, player, slot, modded)) return false;
            }
            catch (Exception e) { Report(g.FullKey, HookId.CanEquipAccessory, e); }
        }

        return true;
    }

    private static int PickItemMovementAction(On.Terraria.UI.ItemSlot.orig_PickItemMovementAction orig,
                                              Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Item> inv,
                                              int context, int slot, Item checkItem, bool backwardCheck, int initialSlot)
    {
        var action = orig(inv, context, slot, checkItem, backwardCheck, initialSlot);
        var abs = Math.Abs(context);
        if (action == -1 || (abs != ItemSlot.Context.EquipAccessory && abs != ItemSlot.Context.EquipAccessoryVanity)) return action;
        if (checkItem is null || checkItem.type <= 0 || !checkItem.accessory) return action;
        return CanEquipAccessory(checkItem, slot, context < 0) ? action : -1;
    }

    private static Item ArmorSwap(On.Terraria.UI.ItemSlot.orig_ArmorSwap orig, Item item, out bool success, out int targetSlot)
    {
        var result = orig(item, out success, out targetSlot);
        if (!success || targetSlot < 0) return result;

        try
        {
            var armor = Main.LocalPlayer.armor;
            if (armor is null || targetSlot >= armor.Length) return result;
            var incoming = armor[targetSlot];
            if (incoming is null || !incoming.accessory || CanEquipAccessory(incoming, targetSlot, false)) return result;

            armor[targetSlot] = result;
            success = false;
            targetSlot = -1;
            return incoming;
        }
        catch (Exception e)
        {
            ContentErrors.Line($"[item] ArmorSwap 异常：{Safe.Dump(e)}");
            return result;
        }
    }

    private static bool CanEquipAccessoryInSlot(On.Terraria.UI.ItemSlot.orig_CanEquipAccessoryInSlot_Item_int orig,
                                                Item checkItem, int slot) =>
        orig(checkItem, slot) && (checkItem is null || checkItem.type <= 0 || CanEquipAccessory(checkItem, slot, false));

    private static bool CanEquipAccessoryInVanitySlot(On.Terraria.UI.ItemSlot.orig_CanEquipAccessoryInVanitySlot orig,
                                                      Item checkItem, int slot) =>
        orig(checkItem, slot) && (checkItem is null || checkItem.type <= 0 || CanEquipAccessory(checkItem, slot, true));

    private static void BoringSetup(On.Terraria.DataStructures.PlayerDrawSet.orig_BoringSetup orig,
                                    Terraria.DataStructures.PlayerDrawSet self, Player player, ref Vector2 drawPosition,
                                    float shadowOpacity, float rotation, ref Vector2 rotationOrigin, Projectile overrideHeldProjectile)
    {
        orig(self, player, ref drawPosition, shadowOpacity, rotation, ref rotationOrigin, overrideHeldProjectile);

        if (!GlobalItemRegistry.AnyWith(HookId.DrawArmorColor) && !GlobalItemRegistry.AnyWith(HookId.ArmorArmGlowMask)) return;

        try
        {
            Colors(self, player);
        }
        catch (Exception e)
        {
            ContentErrors.Line($"[item] DrawArmorColor 异常：{Safe.Dump(e)}");
        }
    }

    private static void Colors(Terraria.DataStructures.PlayerDrawSet set, Player player)
    {
        var shadow = set.shadow;

        var headColor = set.colorArmorHead;
        var headGlow = set.headGlowMask;
        var headGlowColor = set.headGlowColor;
        Armor(EquipType.Head, player.head, player, shadow, ref headColor, ref headGlow, ref headGlowColor);
        set.colorArmorHead = headColor;
        set.headGlowMask = headGlow;
        set.headGlowColor = headGlowColor;

        var bodyColor = set.colorArmorBody;
        var bodyGlow = set.bodyGlowMask;
        var bodyGlowColor = set.bodyGlowColor;
        Armor(EquipType.Body, player.body, player, shadow, ref bodyColor, ref bodyGlow, ref bodyGlowColor);
        set.colorArmorBody = bodyColor;
        set.bodyGlowMask = bodyGlow;
        set.bodyGlowColor = bodyGlowColor;

        var armGlow = set.armGlowMask;
        var armGlowColor = set.armGlowColor;
        Arm(player.body, player, shadow, ref armGlow, ref armGlowColor);
        set.armGlowMask = armGlow;
        set.armGlowColor = armGlowColor;

        var legsColor = set.colorArmorLegs;
        var legsGlow = set.legsGlowMask;
        var legsGlowColor = set.legsGlowColor;
        Armor(EquipType.Legs, player.legs, player, shadow, ref legsColor, ref legsGlow, ref legsGlowColor);
        set.colorArmorLegs = legsColor;
        set.legsGlowMask = legsGlow;
        set.legsGlowColor = legsGlowColor;
    }

    private static void Armor(EquipType type, int slot, Player player, float shadow, ref Color color,
                              ref int glowMask, ref Color glowColor)
    {
        if (slot <= 0) return;

        var texture = EquipTable.OfSlot(type, slot);
        if (texture is null && !GlobalItemRegistry.AnyWith(HookId.DrawArmorColor)) return;

        var glowBefore = glowColor.PackedValue;
        var tint = color;
        var glow = glowMask;
        var glowTint = glowColor;

        if (texture is not null)
        {
            try { texture.DrawArmorColor(player, shadow, ref tint, ref glow, ref glowTint); }
            catch (Exception e) { Report(texture.Name, HookId.DrawArmorColor, e); }
        }

        foreach (var g in GlobalItemRegistry.All)
        {
            if (!g.Plan.Has(HookId.DrawArmorColor)) continue;

            try { g.DrawArmorColor(type, slot, player, shadow, ref tint, ref glow, ref glowTint); }
            catch (Exception e) { Report(g.FullKey, HookId.DrawArmorColor, e); }
        }

        if ((uint)(color.PackedValue >> 24) != 0) color = tint;
        glowMask = glow;
        if (glowTint.PackedValue != glowBefore) glowColor = player.GetImmuneAlphaPure(glowTint, shadow);
    }

    private static void Arm(int slot, Player player, float shadow, ref int glowMask, ref Color color)
    {
        if (slot <= 0) return;

        var texture = EquipTable.OfSlot(EquipType.Body, slot);
        if (texture is null && !GlobalItemRegistry.AnyWith(HookId.ArmorArmGlowMask)) return;

        var before = color.PackedValue;
        var glow = glowMask;
        var tint = color;

        if (texture is not null)
        {
            try { texture.ArmorArmGlowMask(player, shadow, ref glow, ref tint); }
            catch (Exception e) { Report(texture.Name, HookId.ArmorArmGlowMask, e); }
        }

        foreach (var g in GlobalItemRegistry.All)
        {
            if (!g.Plan.Has(HookId.ArmorArmGlowMask)) continue;

            try { g.ArmorArmGlowMask(slot, player, shadow, ref glow, ref tint); }
            catch (Exception e) { Report(g.FullKey, HookId.ArmorArmGlowMask, e); }
        }

        if (tint.PackedValue != before) color = player.GetImmuneAlphaPure(tint, shadow);
        glowMask = glow;
    }

    private static void Report(string key, HookId hook, Exception e) =>
        ContentErrors.Callback($"[item] {key}.{hook}", e);
}
