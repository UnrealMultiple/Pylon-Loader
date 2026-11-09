using System.Collections.Generic;
using Pylon.ModLoader;
using Terraria;
using Terraria.Achievements;
using Terraria.GameContent.Achievements;
using Terraria.ID;

namespace PipelineTest.Content;

/// <summary>成就管线的探针：完成顺序与四条通知的计数。</summary>
public static class AchievementProbe
{
    public static readonly List<string> Completed = [];
    public static int Kills, Pickups, Crafts, Tiles;

    public static void Reset()
    {
        Completed.Clear();
        Kills = Pickups = Crafts = Tiles = 0;
    }
}

/// <summary>单条"发生过"的条件：OnCompleted 只应在从未完成变完成的那一下派一次。</summary>
public sealed class TestFlagAchievement : ModAchievement
{
    public override int Index => 0;

    public override void SetStaticDefaults() => AddCondition();

    public override void OnCompleted(Achievement achievement) => AchievementProbe.Completed.Add(Name);

    public override void OnNPCKilled(Player player, int npcId) => AchievementProbe.Kills++;

    public override void OnItemPickup(Player player, int itemType, int stack) => AchievementProbe.Pickups += stack;

    public override void OnItemCraft(int itemType, int stack) => AchievementProbe.Crafts += stack;

    public override void OnTileDestroyed(Player player, int tileType) => AchievementProbe.Tiles++;
}

/// <summary>计数条件 + 隐藏文本 + 非默认分类。</summary>
public sealed class TestCountAchievement : ModAchievement
{
    public CustomIntCondition? Condition;

    public override string DisplayName => "Count Test";

    public override string Description => "Reach five.";

    public override bool Hidden => Condition is null || Condition.Value == 0;

    public override AchievementCategory Category => AchievementCategory.Collector;

    public override void SetStaticDefaults() => Condition = AddIntCondition(5);

    public override void OnCompleted(Achievement achievement) => AchievementProbe.Completed.Add(Name);
}

/// <summary>杀怪条件：进度由游戏自己的 AchievementsHelper 推。</summary>
public sealed class TestKillAchievement : ModAchievement
{
    public override int Index => 1;

    public override void SetStaticDefaults() => AddNPCKilledCondition(NPCID.BlueSlime);

    public override void OnCompleted(Achievement achievement) => AchievementProbe.Completed.Add(Name);
}

/// <summary>两条命名条件：先完成的只推进一条，两条都完成才算成就完成。</summary>
public sealed class TestMultiAchievement : ModAchievement
{
    public override void SetStaticDefaults()
    {
        AddItemPickupCondition("Gel", ItemID.Gel);
        AddItemCraftCondition("Torch", ItemID.Torch);
    }

    public override void OnCompleted(Achievement achievement) => AchievementProbe.Completed.Add(Name);
}
