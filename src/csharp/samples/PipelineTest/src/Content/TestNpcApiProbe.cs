using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

internal static class NpcApiProbe
{
    internal static bool Active;
    internal static int Index = -1;
    internal static int HitEffects, RegenCalls, CollisionCalls;
    internal static int LastHitDamage = -1, LastHitDirection = 0;
    internal static float CollisionScale = 2f;

    internal static void Reset()
    {
        Active = false;
        Index = -1;
        HitEffects = RegenCalls = CollisionCalls = 0;
        LastHitDamage = -1;
        LastHitDirection = 0;
        CollisionScale = 2f;
    }
}

public sealed class TestNpcApiGlobal : GlobalNpc
{
    public override void HitEffect(NPC npc, HitInfo hit)
    {
        if (!NpcApiProbe.Active || npc.whoAmI != NpcApiProbe.Index) return;
        NpcApiProbe.HitEffects++;
        NpcApiProbe.LastHitDamage = hit.Damage;
        NpcApiProbe.LastHitDirection = hit.HitDirection;
    }

    public override void UpdateLifeRegen(NPC npc, ref int damage)
    {
        if (!NpcApiProbe.Active || npc.whoAmI != NpcApiProbe.Index) return;
        NpcApiProbe.RegenCalls++;
        damage += 3;
    }

    public override bool ModifyCollisionData(NPC npc, Rectangle victimHitbox, ref int immunityCooldownSlot,
                                             ref MultipliableFloat damageMultiplier, ref Rectangle npcHitbox)
    {
        if (!NpcApiProbe.Active || npc.whoAmI != NpcApiProbe.Index) return true;
        NpcApiProbe.CollisionCalls++;
        damageMultiplier *= NpcApiProbe.CollisionScale;
        return true;
    }
}
