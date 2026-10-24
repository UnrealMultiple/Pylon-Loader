using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>挥击碰撞盒与特效：原版算出挥击矩形后交作者改（UseItemHitbox），特效阶段再告知同一矩形（MeleeEffects）；ModItem 先、GlobalItem 后。</summary>
internal static class ItemHitboxHooks
{
    private static bool _globalHitbox;
    private static bool _globalEffects;

    internal static void Install()
    {
        RefreshGlobals();

        On.Terraria.Player.ItemCheck_GetMeleeHitbox += GetMeleeHitbox;
        On.Terraria.Player.ItemCheck_EmitUseVisuals += EmitUseVisuals;
    }

    internal static void RefreshGlobals()
    {
        _globalHitbox = GlobalItemRegistry.AnyWith(HookId.UseItemHitbox);
        _globalEffects = GlobalItemRegistry.AnyWith(HookId.MeleeEffects);
    }

    private static bool Wanted(bool global, Item? item) =>
        item is not null && item.type > 0 && (global || ModContent.IsCustom(ContentKind.Item, item.type));

    private static void GetMeleeHitbox(On.Terraria.Player.orig_ItemCheck_GetMeleeHitbox orig,
                                       Player self, Item sItem, Rectangle heldItemFrame, out bool dontAttack, out Rectangle itemRectangle)
    {
        orig(self, sItem, heldItemFrame, out dontAttack, out itemRectangle);

        if (!Wanted(_globalHitbox, sItem)) return;

        if (ItemPipeline.Instance.InstanceOf(sItem) is { } m && m.Plan.Has(HookId.UseItemHitbox))
        {
            try { m.UseItemHitbox(self, ref itemRectangle, ref dontAttack); }
            catch (Exception e) { Report(m.FullKey, HookId.UseItemHitbox, e); }
        }

        foreach (var g in GlobalItemRegistry.For(sItem, HookId.UseItemHitbox))
        {
            try { g.UseItemHitbox(sItem, self, ref itemRectangle, ref dontAttack); }
            catch (Exception e) { Report(g.FullKey, HookId.UseItemHitbox, e); }
        }
    }

    private static void EmitUseVisuals(On.Terraria.Player.orig_ItemCheck_EmitUseVisuals orig,
                                       Player self, Item sItem, Rectangle itemRectangle)
    {
        orig(self, sItem, itemRectangle);

        if (!Wanted(_globalEffects, sItem)) return;

        if (ItemPipeline.Instance.InstanceOf(sItem) is { } m && m.Plan.Has(HookId.MeleeEffects))
        {
            try { m.MeleeEffects(self, itemRectangle); }
            catch (Exception e) { Report(m.FullKey, HookId.MeleeEffects, e); }
        }

        foreach (var g in GlobalItemRegistry.For(sItem, HookId.MeleeEffects))
        {
            try { g.MeleeEffects(sItem, self, itemRectangle); }
            catch (Exception e) { Report(g.FullKey, HookId.MeleeEffects, e); }
        }

        var mods = PlayerPipeline.Instance.ModsOf(self);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
            if (!mod.HookEnabled(HookId.MeleeEffects)) continue;

            try { mod.MeleeEffects(sItem, itemRectangle); }
            catch (Exception e) { ContentErrors.Callback($"[item] {mod.Mod?.Name}/{mod.GetType().Name}.MeleeEffects", e); }
        }
    }

    private static void Report(string key, HookId hook, Exception e) =>
        ContentErrors.Callback($"[item] {key}.{hook}", e);
}
