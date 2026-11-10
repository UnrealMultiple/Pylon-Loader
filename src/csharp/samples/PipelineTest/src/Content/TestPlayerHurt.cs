using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;

namespace PipelineTest.Content;

internal static class PlayerHurtProbe
{
    internal static bool Active, Immune, Cancel, Dodge;
    internal static int ModifyCalls, OnCalls, PostCalls, SeenDamage;
    internal static void Reset()
    {
        Immune = Cancel = Dodge = false;
        ModifyCalls = OnCalls = PostCalls = SeenDamage = 0;
    }
}

public sealed class TestPlayerHurt : ModPlayer
{
    public override bool ImmuneTo(Player player, PlayerDeathReason source, int cooldown, bool dodgeable) =>
        PlayerHurtProbe.Active && PlayerHurtProbe.Immune;

    public override void ModifyHurt(Player player, ref HurtModifiers modifiers)
    {
        if (!PlayerHurtProbe.Active) return;
        PlayerHurtProbe.ModifyCalls++;
        if (PlayerHurtProbe.Cancel) modifiers.Cancel();
        else modifiers.SetMaxDamage(1);
    }

    public override bool FreeDodge(Player player, HurtInfo info) => PlayerHurtProbe.Active && PlayerHurtProbe.Dodge;

    public override void OnHurt(Player player, HurtInfo info)
    {
        if (!PlayerHurtProbe.Active) return;
        PlayerHurtProbe.OnCalls++;
        PlayerHurtProbe.SeenDamage = info.Damage;
    }

    public override void PostHurt(Player player, HurtInfo info)
    {
        if (PlayerHurtProbe.Active) PlayerHurtProbe.PostCalls++;
    }
}
