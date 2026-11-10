using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

internal static class NpcHitProbe
{
    internal static bool Active;
    internal static int IncomingCalls, ItemGateCalls;
    internal static int TargetIndex = -1;
}

public sealed class TestNpcIncomingHit : GlobalNpc
{
    public override void ModifyIncomingHit(NPC npc, ref HitModifiers modifiers)
    {
        if (!NpcHitProbe.Active || npc.whoAmI != NpcHitProbe.TargetIndex) return;
        NpcHitProbe.IncomingCalls++;
        modifiers.SetMaxDamage(7);
        modifiers.HideCombatText();
    }
}

public sealed class TestItemHitGate : GlobalItem
{
    public override bool? CanHitNPC(Item item, Player player, NPC target)
    {
        if (!NpcHitProbe.Active || target.whoAmI != NpcHitProbe.TargetIndex) return null;
        NpcHitProbe.ItemGateCalls++;
        return false;
    }
}
