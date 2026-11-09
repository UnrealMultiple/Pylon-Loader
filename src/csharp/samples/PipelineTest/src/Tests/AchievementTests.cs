using System;
using Pylon.ModLoader;
using Terraria;
using Terraria.Achievements;
using Terraria.GameContent.Achievements;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 24：Achievement（成就）。
/// Ready：注册进游戏的成就管理器、图标序号、分类、条件与追踪器。
/// World：完成回调只派一次、计数条件、杀怪/拣物/制作通知、菜单数据里能看到 mod 成就。
/// </summary>
internal static class AchievementTests
{
    private static string Key<T>() where T : ModAchievement =>
        ModContent.GetInstance<T>().Mod.Name + "/" + ModContent.GetInstance<T>().Name;

    private static Achievement Of<T>() where T : ModAchievement => ModContent.GetInstance<T>().Achievement;

    public static void Register()
    {
        PT.Group("achievement", Phase.Ready, RunReady);
        PT.Group("achievement-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("ACH-R1", c =>
        {
            var manager = Main.Achievements;
            c.NotNull(manager, "Main.Achievements");

            foreach (var key in new[] { Key<Content.TestFlagAchievement>(), Key<Content.TestCountAchievement>(), Key<Content.TestKillAchievement>(), Key<Content.TestMultiAchievement>() })
            {
                var achievement = manager.GetAchievement(key);
                c.True(achievement is not null, $"管理器里有 {key}");
            }

            // 管理器回来的是同一个原生对象的另一个托管壳，比较名字而不是引用。
            c.Eq(manager.GetAchievement(Key<Content.TestFlagAchievement>())?.Name, Of<Content.TestFlagAchievement>().Name, "取回的就是作者那条成就");
        });

        PT.Case("ACH-R2", c =>
        {
            // 菜单显示的是本地化文本对象的值，不是游戏按 "Achievements.<键>_Name" 拼出来的键。
            c.Eq(Of<Content.TestFlagAchievement>().FriendlyName.Value, "Test Flag Achievement", "默认名字按类名拆词");
            c.Eq(Of<Content.TestCountAchievement>().FriendlyName.Value, "Count Test", "作者覆盖的名字");
            c.Eq(Of<Content.TestCountAchievement>().Description.Value, "Reach five.", "作者覆盖的说明");

            c.Eq(Main.Achievements.GetIconIndex(Key<Content.TestFlagAchievement>()), 0, "图标序号（flag）");
            c.Eq(Main.Achievements.GetIconIndex(Key<Content.TestKillAchievement>()), 1, "图标序号（kill）");
            c.Eq(Of<Content.TestFlagAchievement>().Category, AchievementCategory.Slayer, "默认分类");
            c.Eq(Of<Content.TestCountAchievement>().Category, AchievementCategory.Collector, "作者指定的分类");
        });

        PT.Case("ACH-R3", c =>
        {
            var flagMod = ModContent.GetInstance<Content.TestFlagAchievement>();
            var countMod = ModContent.GetInstance<Content.TestCountAchievement>();
            var multiMod = ModContent.GetInstance<Content.TestMultiAchievement>();
            var multi = Of<Content.TestMultiAchievement>();

            c.Eq(flagMod.Conditions.Count, 1, "flag 条件数");
            c.Eq(countMod.Conditions.Count, 1, "count 条件数");
            c.Eq(multiMod.Conditions.Count, 2, "multi 条件数");
            c.NotNull(multi.GetCondition("Gel"), "multi 的 Gel 条件（按名字取）");
            c.NotNull(multi.GetCondition("Torch"), "multi 的 Torch 条件（按名字取）");

            c.True(countMod.Achievement.HasTracker, "单条件成就拿到追踪器");
            c.True(multi.HasTracker, "多条件成就拿到「条件全完成」追踪器");
            c.True(!flagMod.Achievement.HasTracker, "「发生过」的条件没有追踪器");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("ACH-W1", "玩家还没就绪");
            return;
        }

        // 成就是存档级的：上一次跑过的进度会带过来，先清掉再测"从没完成到完成"。
        foreach (var key in new[] { Key<Content.TestFlagAchievement>(), Key<Content.TestCountAchievement>(), Key<Content.TestKillAchievement>(), Key<Content.TestMultiAchievement>() })
            Main.Achievements.Clear(key);
        Content.AchievementProbe.Reset();

        PT.Case("ACH-W1", c =>
        {
            var flag = Of<Content.TestFlagAchievement>();
            c.True(!flag.IsCompleted, "清空后是未完成");

            flag.GetCondition("Condition").Complete();

            c.True(flag.IsCompleted, "条件完成后成就完成");
            c.Eq(Content.AchievementProbe.Completed.FindAll(n => n == "TestFlagAchievement").Count, 1, "OnCompleted 只派一次");
        });

        PT.Case("ACH-W2", c =>
        {
            var instance = ModContent.GetInstance<Content.TestCountAchievement>();
            var achievement = instance.Achievement;
            var condition = instance.Condition;
            if (condition is null)
            {
                c.Fail("计数条件为空（SetStaticDefaults 没跑到）");
                return;
            }

            bool hiddenBefore = instance.Hidden;
            condition.Value += 3;
            bool partial = !achievement.IsCompleted && !instance.Hidden;
            condition.Value += 2;

            c.True(hiddenBefore, "值 0 时 Hidden 为真");
            c.True(partial, "到 3/5 时未完成且不再隐藏");
            c.True(achievement.IsCompleted, "到 5/5 时完成");
            c.True(Content.AchievementProbe.Completed.Contains("TestCountAchievement"), "OnCompleted 派到");
        });

        PT.Case("ACH-W3", c =>
        {
            int kills = Content.AchievementProbe.Kills;
            AchievementsHelper.NotifyNPCKilledDirect(player, NPCID.BlueSlime);

            c.True(Of<Content.TestKillAchievement>().IsCompleted, "杀怪条件完成");
            c.True(Content.AchievementProbe.Kills > kills, $"OnNPCKilled 通知（{kills}→{Content.AchievementProbe.Kills}）");
        });

        PT.Case("ACH-W4", c =>
        {
            var multi = Of<Content.TestMultiAchievement>();
            var gel = new Item();
            gel.SetDefaults(ItemID.Gel, null);
            gel.stack = 3;

            int pickups = Content.AchievementProbe.Pickups;
            AchievementsHelper.NotifyItemPickup(player, gel);

            c.True(multi.GetCondition("Gel").IsCompleted, "Gel 条件完成");
            c.True(!multi.IsCompleted, "只完成一条时成就还没完成");
            c.True(Content.AchievementProbe.Pickups > pickups, $"OnItemPickup 通知（{pickups}→{Content.AchievementProbe.Pickups}）");
        });

        PT.Case("ACH-W5", c =>
        {
            var multi = Of<Content.TestMultiAchievement>();
            multi.GetCondition("Torch").Complete();

            c.True(multi.IsCompleted, "两条条件都完成后成就完成");
            c.True(Content.AchievementProbe.Completed.Contains("TestMultiAchievement"), "OnCompleted 派到");
        });

        PT.Case("ACH-W6", c =>
        {
            var list = Main.Achievements.CreateAchievementsList();
            c.NotNull(list, "成就列表");

            var found = 0;
            for (var i = 0; i < list.Count; i++)
                if (list[i] is { Name: { } name } && name.StartsWith("PipelineTest/", StringComparison.Ordinal)) found++;

            c.Eq(found, 4, "列表里的 mod 成就数");
            c.Eq(Content.AchievementProbe.Completed.Count, 4, "四条成就都派过 OnCompleted");

            // 打开成就菜单：肉眼看图标格（贴图按 592×64 排，x=Index×66 解锁 / x=528 锁定）。
            GUIInstance.Active.GUIAchievementsMenu.Open(0, -1);
            PT.Info("成就菜单已打开：看 mod 成就的图标是否落在格子里");
        });
    }
}
