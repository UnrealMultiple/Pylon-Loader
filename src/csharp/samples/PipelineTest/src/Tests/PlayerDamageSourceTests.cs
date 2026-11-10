using PipelineTest.Content;
using Terraria;
using Terraria.DataStructures;

namespace PipelineTest.Tests;

internal static class PlayerDamageSourceTests
{
    public static void Register() => PT.Group("playerdamage-source-w", Phase.World, Run);

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
            PT.SkipCase("SOURCE-H1", "没有空闲来源槽位，避免修改游玩实体");
            return;
        }
        var player = new Player { whoAmI = Main.myPlayer, statLife = 100, statLifeMax = 100, statLifeMax2 = 100 };
        var projectile = Main.projectile[projectileIndex];
        var savedOwner = projectile.owner;
        PlayerDamageSourceProbe.Npc = Main.npc[npcIndex];
        PlayerDamageSourceProbe.Projectile = projectile;
        projectile.owner = Main.myPlayer;
        PlayerDamageSourceProbe.Active = true;
        void Prepare()
        {
            PlayerDamageSourceProbe.Reset();
            player.statLife = 100;
            player.dead = false;
            player.immune = false;
            player.immuneTime = 0;
            if (player.hurtCooldowns is { } cooldowns)
                for (var i = 0; i < cooldowns.Length; i++) cooldowns[i] = 0;
        }
        try
        {
            PT.Case("SOURCE-H1", c =>
            {
                Prepare();
                PlayerDamageSourceProbe.VetoNpc = true;
                var damage = player.Hurt(PlayerDeathReason.ByNPC(npcIndex), 20, 0, false, true, false, -1, false);
                c.Eq(damage, 0d, "NPC来源否决真实玩家Hurt入口");
                c.Eq(PlayerDamageSourceProbe.NpcGates, 1, "来源门控一次");
                c.Eq(PlayerDamageSourceProbe.NpcModify, 0, "否决不运行修饰");
            });
            PT.Case("SOURCE-H2", c =>
            {
                Prepare();
                var damage = player.Hurt(PlayerDeathReason.ByNPC(npcIndex), 20, 0, false, true, false, -1, false);
                c.True(damage > 0 && damage <= 1, "NPC来源修饰限制实际伤害");
                c.Eq(PlayerDamageSourceProbe.NpcAfter, 1, "NPC来源后置一次");
                c.Eq(PlayerDamageSourceProbe.Seen, (int)damage, "来源收到原版实际伤害");
            });
            PT.Case("SOURCE-H3", c =>
            {
                Prepare();
                PlayerDamageSourceProbe.VetoProjectile = true;
                var damage = player.Hurt(PlayerDeathReason.ByProjectile(Main.myPlayer, projectileIndex), 20, 0, true, true, false, -1, false);
                c.Eq(damage, 0d, "PvP弹幕来源否决");
                c.Eq(PlayerDamageSourceProbe.PvpGates, 1, "使用PvP来源门控");
                c.Eq(PlayerDamageSourceProbe.ProjectileGates, 0, "不混用非PvP门控");
            });
            PT.Case("SOURCE-H4", c =>
            {
                Prepare();
                var damage = player.Hurt(PlayerDeathReason.ByProjectile(Main.myPlayer, projectileIndex), 20, 0, false, true, false, -1, false);
                c.True(damage > 0 && damage <= 1, "弹幕来源修饰限制实际伤害");
                c.Eq(PlayerDamageSourceProbe.ProjectileAfter, 1, "弹幕来源后置一次");
                c.Eq(PlayerDamageSourceProbe.Seen, (int)damage, "弹幕来源收到实际伤害");
            });
        }
        finally
        {
            projectile.owner = savedOwner;
            PlayerDamageSourceProbe.Active = false;
            PlayerDamageSourceProbe.Reset();
        }
    }
}
