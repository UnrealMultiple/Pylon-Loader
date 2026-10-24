using PipelineTest.Content;
using Terraria;
using Terraria.DataStructures;

namespace PipelineTest.Tests;

internal static class ItemPvpTests
{
    public static void Register() => PT.Group("itempvp-w", Phase.World, Run);

    private static void Run()
    {
        // 独立目标只复用本地玩家序号满足权威路径，不放入玩家表也不伤害游玩角色。
        var attacker = Main.LocalPlayer;
        var target = new Player { whoAmI = Main.myPlayer, statLife = 100, statLifeMax = 100,
                                  statLifeMax2 = 100, hostile = true };
        var source = PlayerDeathReason.ByPlayer(attacker.whoAmI);
        source._sourceItemType = Terraria.ID.ItemID.CopperShortsword;
        source._sourceItemPrefix = 0;
        ItemPvpProbe.Active = true;
        ItemPvpProbe.Reset();
        try
        {
            PT.Case("ITEMPVP-H1", c =>
            {
                target.immune = false;
                target.immuneTime = 0;
                if (target.hurtCooldowns is { } cooldowns)
                    for (var i = 0; i < cooldowns.Length; i++) cooldowns[i] = 0;
                var done = target.Hurt(source, 20, 0, true, true, false, -1, false);
                c.Eq(ItemPvpProbe.Modifies, 1, "真实Hurt解析物品来源并修改一次");
                c.True(done > 0 && done <= 1, $"武器PvP伤害上限应用 done={done}");
                c.Eq(ItemPvpProbe.After, 1, "仅一次实际物品PvP通知");
                c.Eq(ItemPvpProbe.Damage, (int)done, "来源通知使用实际伤害");
                c.True(!target.dead, "独立测试角色存活");
            });
        }
        finally { ItemPvpProbe.Active = false; ItemPvpProbe.Reset(); }
    }
}
