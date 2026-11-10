using Il2CppMicrosoft.Xna.Framework;
using PipelineTest.Content;
using Terraria;

namespace PipelineTest.Tests;

internal static class ProjectileHitTests
{
    public static void Register() => PT.Group("projectilehit-w", Phase.World, Run);

    private static void Run()
    {
        var npcIndex = -1;
        var projectileIndex = -1;
        for (var i = 0; i < Main.npc.Length; i++)
            if (Main.npc[i] is { active: false }) { npcIndex = i; break; }
        for (var i = 0; i < Main.projectile.Length; i++)
            if (Main.projectile[i] is { active: false }) { projectileIndex = i; break; }
        if (npcIndex < 0 || projectileIndex < 0)
        {
            PT.SkipCase("PROJHIT-H1", "没有可隔离的NPC与弹幕槽位");
            return;
        }
        var oldNpc = Main.npc[npcIndex];
        var oldProjectile = Main.projectile[projectileIndex];
        var npc = new NPC();
        npc.SetDefaults(Terraria.ID.NPCID.BlueSlime, new NPCSpawnParams());
        npc.whoAmI = npcIndex;
        npc.active = true;
        npc.life = npc.lifeMax = 100;
        npc.defense = 10;
        npc.position = Main.LocalPlayer.position;
        var projectile = new Projectile();
        projectile.SetDefaults(Terraria.ID.ProjectileID.WoodenArrowFriendly);
        projectile.whoAmI = projectileIndex;
        projectile.owner = Main.myPlayer;
        projectile.active = true;
        projectile.friendly = true;
        projectile.hostile = false;
        projectile.damage = 40;
        projectile.penetrate = -1;
        projectile.position = npc.position;
        Main.npc[npcIndex] = npc;
        Main.projectile[projectileIndex] = projectile;
        ProjectileHitProbe.Active = true;
        ProjectileHitProbe.TargetIndex = npcIndex;
        ProjectileHitProbe.ProjectileIndex = projectileIndex;
        try
        {
            PT.Case("PROJHIT-H1", c =>
            {
                ProjectileHitProbe.Reset();
                ProjectileHitProbe.Veto = true;
                c.True(!Main.LocalPlayer.CanNPCBeHitByPlayerOrPlayerProjectile(npc, projectile),
                       "真实命中门控收到弹幕来源否决");
                c.Eq(ProjectileHitProbe.GateCalls, 1, "来源门控调用一次");
            });
            PT.Case("PROJHIT-H2", c =>
            {
                ProjectileHitProbe.Reset();
                // 只让这一个目标进入本次原版PVE循环,避免测试弹幕影响场景中的其它实体。
                var hitbox = new Rectangle((int)npc.position.X, (int)npc.position.Y, npc.width, npc.height);
                var before = npc.life;
                projectile.Damage_PVE(ref hitbox, 1f);
                c.Eq(ProjectileHitProbe.IncomingCalls, 1, "目标incoming只调用一次");
                c.Eq(ProjectileHitProbe.ModifyCalls, 1, "真实PVE入口执行来源修饰");
                c.Eq(ProjectileHitProbe.TargetModifyCalls, 1, "目标by-projectile修饰一次");
                c.Eq(ProjectileHitProbe.AfterCalls, 1, "来源命中后通知一次");
                c.Eq(ProjectileHitProbe.TargetAfterCalls, 1, "目标命中后通知一次");
                c.True(ProjectileHitProbe.Done > 0 && ProjectileHitProbe.Done <= 7, "实际伤害受作者上限限制");
                c.Eq(before - npc.life, ProjectileHitProbe.Done, "来源收到真实扣血量");
            });
        }
        finally
        {
            ProjectileHitProbe.Active = false;
            ProjectileHitProbe.ProjectileIndex = ProjectileHitProbe.TargetIndex = -1;
            projectile.active = npc.active = false;
            Main.projectile[projectileIndex] = oldProjectile;
            Main.npc[npcIndex] = oldNpc;
        }
    }
}
