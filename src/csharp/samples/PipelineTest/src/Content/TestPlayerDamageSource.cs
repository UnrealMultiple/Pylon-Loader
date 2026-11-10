using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

internal static class PlayerDamageSourceProbe
{
    internal static bool Active, VetoNpc, VetoProjectile;
    internal static NPC Npc = null!;
    internal static Projectile Projectile = null!;
    internal static int NpcGates, ProjectileGates, PvpGates, NpcModify, ProjectileModify, NpcAfter, ProjectileAfter, Seen;
    internal static void Reset()
    {
        VetoNpc = VetoProjectile = false;
        NpcGates = ProjectileGates = PvpGates = NpcModify = ProjectileModify = NpcAfter = ProjectileAfter = Seen = 0;
    }
}

public sealed class TestPlayerDamageNpc : GlobalNpc
{
    private static bool Matches(NPC npc) => PlayerDamageSourceProbe.Active && npc.Pointer == PlayerDamageSourceProbe.Npc.Pointer;
    public override bool CanHitPlayer(NPC npc, Player target, ref int cooldownSlot)
    {
        if (!Matches(npc)) return true;
        PlayerDamageSourceProbe.NpcGates++;
        cooldownSlot = -1;
        return !PlayerDamageSourceProbe.VetoNpc;
    }
    public override void ModifyHitPlayer(NPC npc, Player target, ref HurtModifiers modifiers)
    {
        if (!Matches(npc)) return;
        PlayerDamageSourceProbe.NpcModify++;
        modifiers.SetMaxDamage(1);
    }
    public override void OnHitPlayer(NPC npc, Player target, HurtInfo hurtInfo)
    {
        if (!Matches(npc)) return;
        PlayerDamageSourceProbe.NpcAfter++;
        PlayerDamageSourceProbe.Seen = hurtInfo.Damage;
    }
}

public sealed class TestPlayerDamageProjectile : GlobalProjectile
{
    private static bool Matches(Projectile projectile) => PlayerDamageSourceProbe.Active && projectile.Pointer == PlayerDamageSourceProbe.Projectile.Pointer;
    public override bool CanHitPlayer(Projectile projectile, Player target)
    {
        if (!Matches(projectile)) return true;
        PlayerDamageSourceProbe.ProjectileGates++;
        return !PlayerDamageSourceProbe.VetoProjectile;
    }
    public override bool CanHitPvp(Projectile projectile, Player target)
    {
        if (!Matches(projectile)) return true;
        PlayerDamageSourceProbe.PvpGates++;
        return !PlayerDamageSourceProbe.VetoProjectile;
    }
    public override void ModifyHitPlayer(Projectile projectile, Player target, ref HurtModifiers modifiers)
    {
        if (!Matches(projectile)) return;
        PlayerDamageSourceProbe.ProjectileModify++;
        modifiers.SetMaxDamage(1);
    }
    public override void OnHitPlayer(Projectile projectile, Player target, HurtInfo hurtInfo)
    {
        if (!Matches(projectile)) return;
        PlayerDamageSourceProbe.ProjectileAfter++;
        PlayerDamageSourceProbe.Seen = hurtInfo.Damage;
    }
}
