using PipelineTest.Content;
using Terraria;

namespace PipelineTest.Tests;

internal static class HealTests
{
    public static void Register() => PT.Group("heal-w", Phase.World, Run);

    private static void Run()
    {
        // 独立角色和物品避免改变正在游玩的生命、魔力与药水状态。
        var player = new Player { statLife = 10, statLifeMax = 100, statLifeMax2 = 100,
                                  statMana = 10, statManaMax = 100, statManaMax2 = 100 };
        var item = new Item { healLife = 20, healMana = 30 };
        HealProbe.LifeCalls = HealProbe.ManaCalls = 0;
        HealProbe.Active = true;
        try
        {
            PT.Case("HEAL-H1", c =>
            {
                player.ApplyLifeAndOrMana(item);
                c.Eq(HealProbe.LifeCalls, 1, "治疗生命回调来自游戏使用入口");
                c.Eq(HealProbe.ManaCalls, 1, "治疗魔力回调来自游戏使用入口");
                c.Eq(player.statLife, 17, "修改后的治疗生命实际生效");
                c.Eq(player.statMana, 19, "修改后的治疗魔力实际生效");
                c.True(!HealProbe.Quick, "普通使用不是快速治疗");
                c.Eq(item.healLife, 20, "使用后恢复物品生命声明值");
                c.Eq(item.healMana, 30, "使用后恢复物品魔力声明值");
            });
        }
        finally { HealProbe.Active = false; }
    }
}
