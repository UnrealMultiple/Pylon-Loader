using System;
using Pylon.ModLoader;
using PipelineTest.Content;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 22：Biome（生物群系）。
/// Ready：编号、默认优先级与静音、初始不在任何群落里。
/// World：进去 → OnEnter 一次 + OnInBiome 跟上 → 抛异常当离开 → 出去补 OnLeave；
/// 两个群落各算各的标记。
/// 判定挂在场景扫描节奏上，所以都用延迟用例等几帧。
/// </summary>
internal static class BiomeTests
{
    public static void Register()
    {
        PT.Group("biome", Phase.Ready, RunReady);
        PT.Group("biome-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("BIOME-R1", c =>
        {
            var one = ModContent.GetInstance<TestBiome>();
            var other = ModContent.GetInstance<TestBiomeOther>();

            c.NotNull(one, "TestBiome 实例");
            c.NotNull(other, "TestBiomeOther 实例");
            c.True(one.Type >= 0 && other.Type >= 0 && one.Type != other.Type,
                $"群落编号={one.Type},{other.Type}（应各不相同）");

            // 群落的默认值：BiomeLow 优先级 + 静音（0），与普通场景效果的 -1 区分开。
            c.Eq(one.Priority, 1, "默认优先级（BiomeLow）");
            c.Eq(one.Music, 0, "默认音乐（静音）");
            c.Eq(one.GetWeight(Main.player[Main.myPlayer]), 0.5f, "默认权重");
        });

        PT.Case("BIOME-R2", c =>
        {
            // 读档前标记全是 false：IsSceneEffectActive 就是群落标记本身。
            var one = ModContent.GetInstance<TestBiome>();
            var player = Main.player[Main.myPlayer];
            if (player is null) { c.Fail("玩家还没就绪"); return; }

            c.True(!one.IsActive(player), "初始不在群落里");
            c.Eq(one.IsSceneEffectActive(player), one.IsActive(player), "IsSceneEffectActive 读标记");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("BIOME-W1", "玩家还没就绪");
            return;
        }

        var one = ModContent.GetInstance<TestBiome>();
        var other = ModContent.GetInstance<TestBiomeOther>();
        TestBiome.Reset();
        TestBiomeOther.Active = false;
        TestBiomeOther.Enters = TestBiomeOther.Leaves = 0;

        PT.Case("BIOME-W1", c =>
        {
            // 进群落：标记由下一次场景扫描翻上来。
            TestBiome.Active = true;
            c.True(!one.IsActive(player), "翻开关的当帧标记还是 false（要等扫描）");
        });

        PT.Later("BIOME-W2", 12, c =>
        {
            c.Eq(TestBiome.Enters, 1, "OnEnter 次数");
            c.True(TestBiome.Insides > 0, $"OnInBiome 调用={TestBiome.Insides}");
            c.True(one.IsActive(player), "扫描后 IsActive 为 true");
            c.True(one.IsSceneEffectActive(player), "场景效果问句同步为 true");

            // 第二个群落没开：各算各的。
            c.Eq(TestBiomeOther.Enters, 0, "另一个群落的 OnEnter 次数");

            // 抛异常当"不在里面"：下一次扫描应当补一次 OnLeave。
            TestBiome.Throw = true;
        });

        PT.Later("BIOME-W3", 26, c =>
        {
            c.True(TestBiome.Leaves >= 1, $"IsBiomeActive 抛异常后补了 OnLeave（{TestBiome.Leaves}）");
            c.True(!one.IsActive(player), "抛异常后标记回 false，不会把玩家困在群落里");
            c.Eq(TestBiome.Enters, 1, "抛异常不会重复派 OnEnter");

            TestBiome.Throw = false;
            TestBiome.Active = false;
        });

        PT.Later("BIOME-W4", 40, c =>
        {
            // 抛异常那次已经算"离开"了，现在再关开关不该补第二次回调：状态机只认边沿。
            c.Eq(TestBiome.Leaves, 1, "已在外时关开关不重复派 OnLeave");
            c.True(!one.IsActive(player), "现在不在群落里");

            TestBiome.Active = true;
        });

        PT.Later("BIOME-W5", 54, c =>
        {
            c.Eq(TestBiome.Enters, 2, "再进去时 OnEnter 次数");
            c.True(one.IsActive(player), "重新在群落里");

            // 另一个群落单独进：回调只派给它自己。
            TestBiomeOther.Active = true;
            TestBiome.Active = false;
        });

        PT.Later("BIOME-W6", 68, c =>
        {
            c.Eq(TestBiomeOther.Enters, 1, "另一个群落的 OnEnter 次数");
            c.True(!one.IsActive(player), "关掉的那个已经不在群落里");
            c.Eq(TestBiome.Leaves, 2, "关掉后补了一次 OnLeave");
            TestBiomeOther.Active = false;
        });
    }
}
