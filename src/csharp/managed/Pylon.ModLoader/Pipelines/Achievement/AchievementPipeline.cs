using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.UI;
using Terraria.Achievements;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>
/// 成就管线：建 <c>Achievement</c>（分类默认 Slayer，作者可在 SetStaticDefaults 里 SetCategory 改）
/// → 作者加条件 → 注册进 <c>Main.Achievements</c> 与图标序号 → 全部注册完重读一次成就存档。
/// 完成那一下派 OnCompleted；击杀/拾取/制作/拆方块/进度事件通知经 AchievementsHelper 静态事件发出；
/// 成就菜单首开刷新列表、菜单与解锁弹窗的图标换成作者贴图、未完成按 Hidden 显示 "???"。
/// </summary>
internal sealed class AchievementPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static AchievementPipeline Instance { get; } = new();

    public string Name => "achievement";

    private static IModLog Log => LoaderHost.Current.Log;

    private static readonly HashSet<ModAchievement> Registered = [];

    private static readonly HashSet<IntPtr> RefreshedMenus = [];

    public bool Install()
    {
        On.Terraria.Achievements.Achievement.OnConditionComplete += ConditionComplete;
        On.Terraria.GameContent.Achievements.AchievementsHelper.NotifyNPCKilledDirect += NpcKilled;
        On.Terraria.GameContent.Achievements.AchievementsHelper.NotifyItemPickup_Player_Item += ItemPickedUp;
        On.Terraria.GameContent.Achievements.AchievementsHelper.NotifyItemPickup_Player_Item_int += ItemPickedUpStack;
        On.Terraria.GameContent.Achievements.AchievementsHelper.NotifyItemCraft += ItemCrafted;
        On.Terraria.GameContent.Achievements.AchievementsHelper.NotifyTileDestroyed += TileDestroyed;
        On.Terraria.GameContent.Achievements.AchievementsHelper.NotifyProgressionEvent += ProgressionEvent;
        On.GUIAchievementsMenu.Open += MenuOpen;
        On.GUIAchievementsMenu.AchievementDraw += AchievementIconDraw;
        On.Terraria.UI.InGamePopups.AchievementUnlockedPopup.DrawInGame += PopupDrawInGame;
        On.Terraria.UI.InGamePopups.AchievementUnlockedPopup.DrawInNotificationsArea += PopupDrawInNotificationsArea;

        Log.Info("[achievement] 已挂（注册 / 条件 / 完成回调 / 五条通知 / 菜单图标 / 弹窗图标）");
        return true;
    }

    public void Refresh() => AchievementTable.Rebuild();

    /// <summary>安装期一次：建成就、加条件、注册进游戏，最后重读成就存档。</summary>
    internal void Build()
    {
        AchievementTable.Rebuild();

        foreach (var mod in AchievementTable.Entries) Register(mod);

        if (AchievementTable.Count > 0)
        {
            Log.Info($"[achievement] 安装：{AchievementTable.Count} 条成就");
            try
            {
                AchievementLoader.FinishSetup();
            }
            catch (Exception e)
            {
                Log.Warn($"[achievement] 成就存档重读失败：{Safe.Describe(e)}");
            }
        }
    }

    private static void Register(ModAchievement mod)
    {
        if (!Registered.Add(mod)) return;

        var key = AchievementTable.KeyOf(mod);

        Achievement achievement;
        try
        {
            achievement = new Achievement(key);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[achievement] {key} 建成就失败：{Safe.Describe(e)}");
            return;
        }

        mod.Achievement = achievement;

        if (achievement.Category == AchievementCategory.None)
            achievement.SetCategory(AchievementCategory.Slayer);

        var baseKey = $"Mods.{mod.Mod.Name}.Achievements.{mod.Name}";
        try
        {
            achievement.FriendlyName = new LocalizedText(baseKey + ".FriendlyName", mod.DisplayName);
            achievement.Description = new LocalizedText(baseKey + ".Description", mod.Description);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[achievement] {key} 名字/说明设置失败：{Safe.Describe(e)}");
        }

        try
        {
            mod.SetStaticDefaults();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[achievement] {key}.SetStaticDefaults 异常：{Safe.Describe(e)}");
        }

        var conditions = mod.Conditions;
        try
        {
            if (!achievement.HasTracker)
            {
                if (conditions.Count > 1)
                {
                    achievement.UseConditionsCompletedTracker();
                }
                else if (conditions.Count == 1)
                {
                    var tracker = conditions[0].GetAchievementTracker();
                    if (tracker is not null)
                        achievement.UseTracker(tracker);
                }
            }
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[achievement] {key} 挑追踪器失败：{Safe.Describe(e)}");
        }

        var manager = Main.Achievements;
        if (manager is null)
        {
            Log.Warn($"[achievement] Main.Achievements 还没建好：{key} 这条这局不注册");
            return;
        }

        try
        {
            manager.Register(achievement);
            manager.RegisterIconIndex(key, mod.Index);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[achievement] {key} 注册失败：{Safe.Describe(e)}");
        }
    }

    // ===================== 通知 =====================

    private static void NpcKilled(On.Terraria.GameContent.Achievements.AchievementsHelper.orig_NotifyNPCKilledDirect orig,
                                  Player player, int npcNetId)
    {
        orig(player, npcNetId);
        AchievementsHelper.FireNPCKilled(player, (short)npcNetId);
    }

    private static void TileDestroyed(On.Terraria.GameContent.Achievements.AchievementsHelper.orig_NotifyTileDestroyed orig,
                                      Player player, ushort tile)
    {
        orig(player, tile);
        if (!Main.gameMenu && Terraria.GameContent.Achievements.AchievementsHelper._isMining)
            AchievementsHelper.FireTileDestroyed(player, tile);
    }

    private static void ItemPickedUp(On.Terraria.GameContent.Achievements.AchievementsHelper.orig_NotifyItemPickup_Player_Item orig,
                                     Player player, Item item)
    {
        orig(player, item);
        AchievementsHelper.FireItemPickup(player, (short)item.type, item.stack);
    }

    private static void ItemPickedUpStack(On.Terraria.GameContent.Achievements.AchievementsHelper.orig_NotifyItemPickup_Player_Item_int orig,
                                          Player player, Item item, int customStack)
    {
        orig(player, item, customStack);
        AchievementsHelper.FireItemPickup(player, (short)item.type, customStack);
    }

    private static void ItemCrafted(On.Terraria.GameContent.Achievements.AchievementsHelper.orig_NotifyItemCraft orig,
                                    Recipe recipe)
    {
        orig(recipe);
        AchievementsHelper.FireItemCraft((short)recipe.createItem.type, recipe.createItem.stack);
    }

    private static void ProgressionEvent(On.Terraria.GameContent.Achievements.AchievementsHelper.orig_NotifyProgressionEvent orig,
                                         int eventId)
    {
        orig(eventId);
        if (Main.netMode != 2)
            AchievementsHelper.FireProgressionEvent(eventId);
    }

    // ===================== 菜单与弹窗 =====================

    private static void MenuOpen(On.GUIAchievementsMenu.orig_Open orig, GUIAchievementsMenu self, int backTo, int selected)
    {
        if (RefreshedMenus.Add(self.Pointer))
            self._achievements = null;
        orig(self, backTo, selected);
    }

    private static void AchievementIconDraw(On.GUIAchievementsMenu.orig_AchievementDraw orig, GUIAchievementsMenu self,
                                            ItemGrid_Layout gridLayout, int index, Vector2 position, float scale)
    {
        Achievement? achievement = null;
        ModAchievement? mod = null;
        Texture2D? texture = null;

        if (AchievementTable.Count > 0 && self is not null)
        {
            var list = self.SortedAchievementsData;
            if (list is not null && (uint)index < (uint)list.Count)
            {
                achievement = list[index];
                if (achievement is not null)
                {
                    mod = AchievementTable.Of(achievement.Name);
                    texture = mod?.Texture?.Value;
                }
            }
        }

        if (achievement is null || mod is null || texture is null)
        {
            orig(self, gridLayout, index, position, scale);
            return;
        }

        LocalizedText? savedName = null, savedDescription = null;
        bool hidden = false;
        if (!achievement.IsCompleted)
        {
            try { hidden = mod.Hidden; }
            catch (Exception e) { ContentErrors.Report($"[achievement] {mod.Name}.Hidden 异常：{Safe.Describe(e)}"); }
        }
        if (hidden)
        {
            savedName = achievement.FriendlyName;
            savedDescription = achievement.Description;
            achievement.FriendlyName = HiddenText;
            achievement.Description = HiddenText;
        }

        var icons = GUIAchievementsMenu.AchievementsIcons;
        GUIAchievementsMenu.AchievementsIcons = texture;
        try
        {
            orig(self, gridLayout, index, position, scale);
        }
        finally
        {
            GUIAchievementsMenu.AchievementsIcons = icons;

            if (hidden)
            {
                achievement.FriendlyName = savedName!;
                achievement.Description = savedDescription!;
            }
        }
    }

    private static void PopupDrawInGame(On.Terraria.UI.InGamePopups.AchievementUnlockedPopup.orig_DrawInGame orig,
                                        InGamePopups.AchievementUnlockedPopup self, SpriteBatch spriteBatch, Vector2 anchor)
    {
        SwapPopupTexture(self);
        orig(self, spriteBatch, anchor);
    }

    private static void PopupDrawInNotificationsArea(On.Terraria.UI.InGamePopups.AchievementUnlockedPopup.orig_DrawInNotificationsArea orig,
                                                     InGamePopups.AchievementUnlockedPopup self, SpriteBatch spriteBatch,
                                                     Rectangle area, ref int gamepadPointLocalIndexTouse)
    {
        SwapPopupTexture(self);
        orig(self, spriteBatch, area, ref gamepadPointLocalIndexTouse);
    }

    private static void SwapPopupTexture(InGamePopups.AchievementUnlockedPopup self)
    {
        if (self is null)
            return;
        var mod = AchievementLoader.Of(self._theAchievement);
        var texture = mod?.Texture;
        if (texture is null)
            return;
        if (self._achievementTexture?.Pointer != texture.Pointer)
            self._achievementTexture = texture;
    }

    private static readonly LocalizedText HiddenText = Language.GetText("???");

    // ===================== 完成 =====================

    private static void ConditionComplete(On.Terraria.Achievements.Achievement.orig_OnConditionComplete orig,
                                          Achievement self, AchievementCondition condition)
    {
        var mod = AchievementLoader.Of(self);
        bool before = mod is null || self.IsCompleted;

        orig(self, condition);

        if (before || !self.IsCompleted) return;

        try
        {
            mod!.OnCompleted(self);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[achievement] {mod!.Name}.OnCompleted 异常：{Safe.Describe(e)}");
        }
    }
}
