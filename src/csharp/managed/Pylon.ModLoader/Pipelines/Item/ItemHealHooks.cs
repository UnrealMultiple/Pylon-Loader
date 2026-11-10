using Terraria;

namespace Pylon.ModLoader;

/// <summary>治疗量在原版使用与选药窗口中临时替换，结束后恢复物品声明值。</summary>
internal static class ItemHealHooks
{
    [ThreadStatic] private static nint _quickPlayer;
    [ThreadStatic] private static Player? _selectingPlayer;
    private static bool _installed;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Terraria.Player.QuickHeal += QuickHeal;
        On.Terraria.Player.QuickMana += QuickMana;
        On.Terraria.Player.ApplyLifeAndOrMana += Apply;
        On.Terraria.Player.QuickHeal_GetItemToUse += Choose;
        On.Terraria.Player.QuickHeal_GetItemToUse_TryChoosingItem += Candidate;
    }

    private static bool Active(HookId hook) => PlayerPipeline.Instance.ModsActive(hook) ||
        GlobalItemRegistry.AnyWith(hook) || ModContent.GetContent<ModItem>().Any(item => item.Plan.Has(hook));

    private static int Heal(Item item, Player player, HookId hook, int value, bool quick, int current)
    {
        if (value <= 0) return value;
        var own = ItemPipeline.Instance.InstanceOf(item);
        if (own is not null && own.Plan.Has(hook))
        {
            try
            {
                if (hook == HookId.GetHealLife) own.GetHealLife(item, player, quick, ref value);
                else own.GetHealMana(item, player, quick, ref value);
            }
            catch (Exception e) { Fail(own, hook, e); }
        }
        foreach (var global in GlobalItemRegistry.For(item, hook))
        {
            try
            {
                if (hook == HookId.GetHealLife) global.GetHealLife(item, player, quick, ref value);
                else global.GetHealMana(item, player, quick, ref value);
            }
            catch (Exception e) { Fail(global, hook, e); }
        }
        if (PlayerPipeline.Instance.ModsActive(hook))
        {
            foreach (var mod in PlayerPipeline.Instance.ModsOf(player))
            {
                if (!mod.HookEnabled(hook)) continue;
                try
                {
                    if (hook == HookId.GetHealLife) mod.GetHealLife(player, item, quick, ref value);
                    else mod.GetHealMana(player, item, quick, ref value);
                }
                catch (Exception e) { Fail(mod, hook, e); }
            }
        }
        return (int)Math.Clamp((long)value, 0, (long)int.MaxValue - Math.Max(0, current));
    }

    private static void QuickHeal(On.Terraria.Player.orig_QuickHeal orig, Player self)
    {
        var outer = _quickPlayer;
        _quickPlayer = self.Pointer;
        try { orig(self); }
        finally { _quickPlayer = outer; }
    }

    private static void QuickMana(On.Terraria.Player.orig_QuickMana orig, Player self)
    {
        var outer = _quickPlayer;
        _quickPlayer = self.Pointer;
        try { orig(self); }
        finally { _quickPlayer = outer; }
    }

    private static void Apply(On.Terraria.Player.orig_ApplyLifeAndOrMana orig, Player self, Item item)
    {
        if (item is null || (!Active(HookId.GetHealLife) && !Active(HookId.GetHealMana)))
        {
            orig(self, item!);
            return;
        }
        var life = item.healLife;
        var mana = item.healMana;
        try
        {
            var quick = _quickPlayer == self.Pointer;
            item.healLife = Heal(item, self, HookId.GetHealLife, life, quick, self.statLife);
            item.healMana = Heal(item, self, HookId.GetHealMana, mana, quick, self.statMana);
            orig(self, item);
        }
        finally
        {
            // 同一物品可能在下一帧用于提示或比较，临时治疗值不能永久污染模板。
            item.healLife = life;
            item.healMana = mana;
        }
    }

    private static Item Choose(On.Terraria.Player.orig_QuickHeal_GetItemToUse orig, Player self)
    {
        var outer = _selectingPlayer;
        _selectingPlayer = self;
        try { return orig(self); }
        finally { _selectingPlayer = outer; }
    }

    private static void Candidate(On.Terraria.Player.orig_QuickHeal_GetItemToUse_TryChoosingItem orig,
                                  int lifeDifference, ref Item bestItem, ref int bestDifference, Item nextItem)
    {
        if (_selectingPlayer is not { } player || nextItem is null || nextItem.stack <= 0 ||
            !nextItem.potion || nextItem.healLife <= 0 || !Active(HookId.GetHealLife))
        {
            orig(lifeDifference, ref bestItem, ref bestDifference, nextItem!);
            return;
        }
        var saved = nextItem.healLife;
        try
        {
            nextItem.healLife = Heal(nextItem, player, HookId.GetHealLife, saved, true, player.statLife);
            orig(lifeDifference, ref bestItem, ref bestDifference, nextItem);
        }
        finally { nextItem.healLife = saved; }
    }

    private static void Fail(object author, HookId hook, Exception e) =>
        ContentErrors.Report($"[heal] {author.GetType().Name}.{hook} 异常:{Safe.Describe(e)}");
}
