using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

internal static class ProjectileHitProbe
{
    internal static bool Active, Veto;
    internal static int ProjectileIndex = -1, TargetIndex = -1;
    internal static int GateCalls, ModifyCalls, AfterCalls, IncomingCalls, TargetModifyCalls, TargetAfterCalls;
    internal static int Done;
    internal static void Reset()
    {
        GateCalls = ModifyCalls = AfterCalls = IncomingCalls = TargetModifyCalls = TargetAfterCalls = Done = 0;
        Veto = false;
    }
}

public sealed class TestProjectileNpcHit : GlobalProjectile
{
    private static bool Matches(Projectile projectile, NPC target) => ProjectileHitProbe.Active &&
        projectile.whoAmI == ProjectileHitProbe.ProjectileIndex && target.whoAmI == ProjectileHitProbe.TargetIndex;

    public override bool? CanHitNPC(Projectile projectile, NPC target)
    {
        if (ProjectileHitProbe.Active && projectile.whoAmI == ProjectileHitProbe.ProjectileIndex &&
            target.whoAmI != ProjectileHitProbe.TargetIndex) return false;
        if (!Matches(projectile, target)) return null;
        ProjectileHitProbe.GateCalls++;
        return !ProjectileHitProbe.Veto;
    }

    public override void ModifyHitNPC(Projectile projectile, NPC target, ref HitModifiers modifiers)
    {
        if (!Matches(projectile, target)) return;
        ProjectileHitProbe.ModifyCalls++;
        modifiers.SetMaxDamage(7);
        modifiers.HideCombatText();
    }

    public override void OnHitNPC(Projectile projectile, NPC target, HitInfo hit, int damageDone)
    {
        if (!Matches(projectile, target)) return;
        ProjectileHitProbe.AfterCalls++;
        ProjectileHitProbe.Done = damageDone;
    }
}

public sealed class TestNpcProjectileHit : GlobalNpc
{
    private static bool Matches(NPC npc) => ProjectileHitProbe.Active && npc.whoAmI == ProjectileHitProbe.TargetIndex;
    public override void ModifyIncomingHit(NPC npc, ref HitModifiers modifiers)
    {
        if (Matches(npc)) ProjectileHitProbe.IncomingCalls++;
    }
    public override void ModifyHitByProjectile(NPC npc, Projectile projectile, ref HitModifiers modifiers)
    {
        if (Matches(npc)) ProjectileHitProbe.TargetModifyCalls++;
    }
    public override void OnHitByProjectile(NPC npc, Projectile projectile, HitInfo hit, int damageDone)
    {
        if (Matches(npc)) ProjectileHitProbe.TargetAfterCalls++;
    }
}
