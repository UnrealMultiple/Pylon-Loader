using Pylon.ModLoader.Pipelines;
using Terraria;
using Terraria.Achievements;

namespace Pylon.ModLoader.Content;
/// <summary>
/// 成就的静态查询面：注册数量与游戏成就对象到内容的反查；存档重读在全部成就注册完之后执行，
/// 让 mod 成就接上已保存的进度，同时保住原版成就已读出的计数。
/// </summary>
public static class AchievementLoader
{
    public static int AchievementCount => AchievementTable.Count;

    internal static ModAchievement? Of(Achievement achievement)
    {
        if (achievement is null)
            return null;
        return AchievementTable.Of(achievement.Name);
    }

    internal static void FinishSetup()
    {
        if (AchievementTable.Count == 0)
            return;
        var manager = Main.Achievements;
        if (manager is null)
            return;

        var kept = new List<(Achievement Achievement, int Count)>();
        var list = manager.CreateAchievementsList();
        for (int i = 0; i < list.Count; i++)
        {
            Achievement achievement = list[i];
            if (Of(achievement) is null)
                kept.Add((achievement, achievement._completedCount));
        }
        try
        {
            manager.Load(manager._savePath, manager._isCloudSave);
        }
        finally
        {
            foreach (var (achievement, count) in kept)
                achievement._completedCount = count;
        }
    }
}
