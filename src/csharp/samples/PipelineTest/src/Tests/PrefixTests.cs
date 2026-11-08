using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 27：Prefix —— 功能验证。发号 > 原版；SetStats/ModifyValue/Apply 直接调作者回调验证数值。
/// </summary>
internal static class PrefixTests
{
    public static void Register()
    {
        PT.Group("prefix", Phase.Ready, Run);
        PT.Group("prefix-w", Phase.World, RunWorld);
    }

    /// <summary>进世界后：把测试前缀真装到热键栏第 1 格的测试物品上（游戏内能看到改名与数值变化）。</summary>
    private static void RunWorld()
    {
        var type = ModContent.PrefixType<Content.TestPrefix>();
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("PREFIX-W1", "玩家还没就绪");
            return;
        }

        PT.Case("PREFIX-W1", c =>
        {
            var item = player.inventory[0];
            if (item is null || item.type != ModContent.TypeOf<Content.TestItem>())
            {
                c.Fail("热键栏第 1 格不是测试物品");
                return;
            }

            var beforeDamage = item.damage;
            // 走 2 参重载：这才是游戏重铸走的那条路径（1 参在本版是另一份原生实现）。
            var ok = item.Prefix(type, out var topTier);
            c.True(ok, "Item.Prefix(type, out bool) 返回 false");
            c.Eq(item.prefix, type, "item.prefix");
            c.True(item.damage > beforeDamage, $"damage {beforeDamage} -> {item.damage}（期望 ×1.5）");

            PT.Info($"测试前缀已装上：damage={item.damage} crit={item.crit} value={item.value}");
        });
    }

    private static void Run()
    {
        var type = ModContent.PrefixType<Content.TestPrefix>();

        PT.Case("PREFIX-R1", c =>
        {
            // 注册会把 PrefixID.Count 抬到"我们的号 + 1"，所以这里不能拿它当原版上界；
            // 判据改成：号在合法区间、能按号反查回本前缀、名字表里是它。
            c.InRange(type, 0, 255, "type");
            c.True(Terraria.ID.PrefixID.Count > type, $"PrefixID.Count={Terraria.ID.PrefixID.Count} 应 > type={type}");
            c.True(ModContent.Find(ContentKind.Prefix, type) is Content.TestPrefix, "按号反查");
            c.NotNull(ModContent.GetInstance<Content.TestPrefix>(), "GetInstance");
        });

        // H1 功能：SetStats 直接调 → 数值按声明修正
        PT.Case("PREFIX-H1", c =>
        {
            float dmg = 1f, kb = 1f, ut = 1f, scale = 1f, ss = 1f, mana = 1f;
            int crit = 0, tag = 0, ap = 0;
            ModContent.GetInstance<Content.TestPrefix>()
                .SetStats(ref dmg, ref kb, ref ut, ref scale, ref ss, ref mana, ref crit, ref tag, ref ap);
            c.Eq(dmg, 1.5f, "damageMult");
            c.Eq(crit, 10, "crit");
        });

        // H2：ModifyValue ×2
        PT.Case("PREFIX-H2", c =>
        {
            float value = 1f;
            ModContent.GetInstance<Content.TestPrefix>().ModifyValue(ref value);
            c.Eq(value, 2f, "valueMult");
        });

        // H3：Apply 标记置位
        PT.Case("PREFIX-H3", c =>
        {
            var item = new Item();
            item.SetDefaults(ModContent.TypeOf<Content.TestItem>(), null);
            ModContent.GetInstance<Content.TestPrefix>().Apply(item);
            c.True(Content.TestPrefix.Applied, "Apply 未被调");
        });
    }
}
