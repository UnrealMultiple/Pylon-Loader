using PipelineTest.Content;
using Terraria;
using Terraria.DataStructures;

namespace PipelineTest.Tests;

internal static class PlayerHurtTests
{
    public static void Register() => PT.Group("playerhurt-w", Phase.World, Run);

    private static void Run()
    {
        // 独立玩家对象不进入 Main.player，不修改正在游玩的生命、免疫或冷却状态。
        var player = new Player { whoAmI = Main.myPlayer, statLife = 100, statLifeMax = 100, statLifeMax2 = 100 };
        var source = PlayerDeathReason.ByCustomReason("PipelineTest hurt probe");
        void Prepare()
        {
            PlayerHurtProbe.Reset();
            player.statLife = 100;
            player.dead = false;
            player.immune = false;
            player.immuneTime = 0;
            if (player.hurtCooldowns is { } cooldowns)
                for (var i = 0; i < cooldowns.Length; i++) cooldowns[i] = 0;
        }

        PlayerHurtProbe.Active = true;
        try
        {
            PT.Case("HURT-H1", c =>
            {
                Prepare();
                PlayerHurtProbe.Immune = true;
                c.Eq(player.Hurt(source, 20, 0, false, true, false, -1, true), 0d, "作者免疫阻止真实受伤入口");
                c.Eq(player.statLife, 100, "免疫不扣生命");
                c.Eq(PlayerHurtProbe.OnCalls, 0, "免疫不发受伤后事件");
            });
            PT.Case("HURT-H2", c =>
            {
                Prepare();
                PlayerHurtProbe.Cancel = true;
                c.Eq(player.Hurt(source, 20, 0, false, true, false, -1, true), 0d, "修饰器取消阻止真实受伤入口");
                c.Eq(PlayerHurtProbe.ModifyCalls, 1, "受伤修饰执行一次");
                c.Eq(player.statLife, 100, "取消不扣生命");
            });
            PT.Case("HURT-H3", c =>
            {
                Prepare();
                PlayerHurtProbe.Dodge = true;
                c.Eq(player.Hurt(source, 20, 0, false, true, false, -1, true), 0d, "免费闪避阻止真实受伤入口");
                c.Eq(PlayerHurtProbe.OnCalls, 0, "闪避不发受伤后事件");
            });
            PT.Case("HURT-H4", c =>
            {
                Prepare();
                var done = player.Hurt(source, 20, 0, false, true, false, -1, false);
                c.True(done > 0 && done <= 1, $"伤害上限作用于原版实际结果 done={done}");
                c.Eq(PlayerHurtProbe.SeenDamage, (int)done, "OnHurt收到原版实际伤害");
                c.Eq(PlayerHurtProbe.OnCalls, 1, "受伤后事件一次");
                c.Eq(PlayerHurtProbe.PostCalls, 1, "仍存活时后置事件一次");
                c.True(!player.dead, "测试不产生致命伤害");
            });
        }
        finally
        {
            PlayerHurtProbe.Active = false;
            PlayerHurtProbe.Reset();
        }
    }
}
