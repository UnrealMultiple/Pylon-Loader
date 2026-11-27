using System;
using System.Collections.Generic;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using Terraria.Achievements;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 成就解锁。1:1 对应原版 <c>AchievementUnlockTool</c>（杂项 · 解锁）：
/// 一键把所有成就的全部条件 Complete。
/// </summary>
public sealed class AchievementUnlockFeature : CheatFeature
{
    public override string Id => "misc.achievement";
    public override string Title => "成就解锁";
    public override CheatCategory Category => CheatCategory.Misc;
    public override string SubPage => "解锁";

    public override void DrawMenu()
    {
        ModernUI.BeginCard("解锁全成就", "ACHIEVEMENTS");

        if (ModernUI.PrimaryButton("一键解锁全部成就", default, ModernUI.Icon.Seal, rightAlign: true))
        {
            UnlockAll();
        }
        ModernUI.DimText("解锁后重新进入存档生效");

        ModernUI.EndCard();
    }

    /// <summary>原版 Main::UnlockAchievements：CreateAchievementsList 遍历，每个条件调 Complete。</summary>
    private static void UnlockAll()
    {
        var manager = Main.Achievements;
        if (manager is null)
        {
            ModernUI.Toast("成就管理器不可用");
            return;
        }

        try
        {
            var list = manager.CreateAchievementsList();
            if (list is null)
            {
                ModernUI.Toast("成就列表为空");
                return;
            }

            var unlocked = 0;
            foreach (var achievement in list)
            {
                if (achievement is null) continue;
                try
                {
                    var conditions = achievement._conditions;
                    if (conditions is null) continue;
                    foreach (var condition in conditions.Values)
                    {
                        condition?.Complete();
                    }
                    unlocked++;
                }
                catch
                {
                    // 单个成就失败不影响其余
                }
            }
            ModernUI.Toast($"已解锁 {unlocked} 项成就");
        }
        catch (Exception e)
        {
            ModernUI.Toast("解锁失败：" + e.Message);
        }
    }
}
