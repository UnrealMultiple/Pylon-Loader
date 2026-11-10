using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

internal static class NpcContactProbe
{
    internal static bool Active, Veto;
    internal static int Source = -1, Target = -1, Gates, Modifies, Afters;
}

public sealed class TestNpcContact : GlobalNpc
{
    public override bool CanHitNPC(NPC npc, NPC target)
    {
        if (!NpcContactProbe.Active || npc.whoAmI != NpcContactProbe.Source || target.whoAmI != NpcContactProbe.Target) return true;
        NpcContactProbe.Gates++;
        return !NpcContactProbe.Veto;
    }

    public override void ModifyHitNPC(NPC npc, NPC target, ref HitModifiers modifiers)
    {
        if (!NpcContactProbe.Active || npc.whoAmI != NpcContactProbe.Source || target.whoAmI != NpcContactProbe.Target) return;
        NpcContactProbe.Modifies++;
        modifiers.SetMaxDamage(3);
    }

    public override void OnHitNPC(NPC npc, NPC target, HitInfo hit)
    {
        if (NpcContactProbe.Active && npc.whoAmI == NpcContactProbe.Source && target.whoAmI == NpcContactProbe.Target)
            NpcContactProbe.Afters++;
    }
}
