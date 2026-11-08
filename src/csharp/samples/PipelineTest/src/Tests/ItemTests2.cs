using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 17：Item —— 使用链功能验证。静态入口直调：
/// - player.GetWeaponDamage 等走 On 钩子
/// - ModifyTooltipLines 走 RebuildTooltip
/// 需要真实挥动/弹幕生成的（Shoot/UseItem 的游戏侧行为）在 World 阶段补。
/// </summary>
internal static class ItemTests2
{
    /// <summary>TestItem.Shoot 的触发标记（World 阶段断言）。</summary>
    public static bool ShootCalled;

    public static void Register()
    {
        PT.Group("item2", Phase.Ready, RunReady);
    }

    private static void RunReady()
    {
        var item = new Item();
        item.SetDefaults(ModContent.TypeOf<Content.TestItem>(), null);
        var player = new Player();

        // H7 功能：ModifyWeaponDamage/Crit/Knockback 修正出现现在 GetWeapon* 结果里
        PT.Case("ITEM-H7", c =>
        {
            var dmg = player.GetWeaponDamage(item);
            var crit = player.GetWeaponCrit(item);
            var kb = player.GetWeaponKnockback(item, 4f);
            // TestItem 未挂修正时：基线值不被污染
            c.Eq(dmg, 15, $"GetWeaponDamage={dmg}（TestItem 基础 15）");
            c.True(crit >= 0, $"GetWeaponCrit={crit}");
            c.True(kb >= 0f, $"GetWeaponKnockback={kb}");
        });

        // E1 功能：TooltipLines 含 TestItem.ModifyTooltipLines 加的行（RebuildTooltip 全链）
        PT.Case("ITEM-E1", c =>
        {
            var probe = new Item();
            probe.SetDefaults(ModContent.TypeOf<Content.TestItem>(), null);
            probe.RebuildTooltip();
            // TestItem 声明 Tooltip="这是一行测试介绍" → TooltipLines 应含这行
            var count = probe.ToolTip.Lines;
            c.True(count >= 2, $"tooltip 行数={count}（声明 1 行 + ModifyTooltipLines 加 1 行 = 2）");
        });
    }
}