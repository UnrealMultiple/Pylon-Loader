using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

internal static class ItemPvpProbe
{
    internal static bool Active, Veto;
    internal static int Gates, Modifies, After, Damage;
    internal static void Reset() { Gates = Modifies = After = Damage = 0; Veto = false; }
}

public sealed class TestItemPvp : GlobalItem
{
    public override bool CanHitPvp(Item item, Player player, Player target)
    {
        if (!ItemPvpProbe.Active) return true;
        ItemPvpProbe.Gates++;
        return !ItemPvpProbe.Veto;
    }

    public override void ModifyHitPvp(Item item, Player player, Player target, ref HurtModifiers modifiers)
    {
        if (!ItemPvpProbe.Active) return;
        ItemPvpProbe.Modifies++;
        modifiers.SetMaxDamage(1);
    }

    public override void OnHitPvp(Item item, Player player, Player target, HurtInfo info)
    {
        if (!ItemPvpProbe.Active) return;
        ItemPvpProbe.After++;
        ItemPvpProbe.Damage = info.Damage;
    }
}
