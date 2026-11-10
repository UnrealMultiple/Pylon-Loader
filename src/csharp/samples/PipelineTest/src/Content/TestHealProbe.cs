using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

internal static class HealProbe
{
    internal static bool Active;
    internal static int LifeCalls, ManaCalls;
    internal static bool Quick;
}

public sealed class TestHealProbe : GlobalItem
{
    public override void GetHealLife(Item item, Player player, bool quickHeal, ref int healValue)
    {
        if (!HealProbe.Active) return;
        HealProbe.LifeCalls++;
        HealProbe.Quick = quickHeal;
        healValue = 7;
    }

    public override void GetHealMana(Item item, Player player, bool quickHeal, ref int healValue)
    {
        if (!HealProbe.Active) return;
        HealProbe.ManaCalls++;
        healValue = 9;
    }
}
