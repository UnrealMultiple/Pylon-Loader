using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Achievements;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>
/// 成就管线：建 <c>Achievement</c> → 作者加条件 → 注册进 <c>Main.Achievements</c> → 图标序号与分类。
/// 完成时派 <c>OnCompleted</c>；击杀/挖掘/拾取/制作四条通知与菜单图标都接上。
/// 隐藏文本（"???"）与卸载清理不接，见 docs/pipeline/content-achievement.md。
/// </summary>
internal sealed class AchievementPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static AchievementPipeline Instance { get; } = new();

    public string Name => "achievement";

    private static IModLog Log => LoaderHost.Current.Log;

    private static readonly HashSet<ModAchievement> Registered = [];

    public bool Install()
    {
        On.Terraria.Achievements.Achievement.OnConditionComplete += ConditionComplete;
        // 四条进度通知都从游戏的 AchievementsHelper 进来：条件自己的计数也走这里，
        // 且只有这条通道能覆盖"别的系统直接通知"的情况。
        On.Terraria.GameContent.Achievements.AchievementsHelper.NotifyNPCKilledDirect += NpcKilled;
        On.Terraria.GameContent.Achievements.AchievementsHelper.NotifyItemPickup_Player_Item += ItemPickedUp;
        On.Terraria.GameContent.Achievements.AchievementsHelper.NotifyItemPickup_Player_Item_int += ItemPickedUpStack;
        On.Terraria.GameContent.Achievements.AchievementsHelper.NotifyItemCraft += ItemCrafted;
        On.Terraria.GameContent.Achievements.AchievementsHelper.NotifyTileDestroyed += TileDestroyed;
        On.GUIAchievementsMenu.AchievementDraw += AchievementIconDraw;

        Log.Info("[achievement] 已挂（注册 / 条件 / 完成回调 / 击杀-挖掘-拾取-制作通知 / 菜单图标）");
        return true;
    }

    public void Refresh() => AchievementTable.Rebuild();

    /// <summary>安装期一次：建成就、加条件、注册进游戏。</summary>
    internal void Build()
    {
        AchievementTable.Rebuild();

        foreach (var mod in AchievementTable.Entries) Register(mod);

        if (AchievementTable.Count > 0) Log.Info($"[achievement] 安装：{AchievementTable.Count} 条成就");
    }

    /// <summary>把一条通知发给每条 mod 成就；单条抛异常不影响别的成就。</summary>
    private static void Notify(Action<ModAchievement> call, string what)
    {
        if (AchievementTable.Count == 0) return;

        foreach (var entry in AchievementTable.Entries)
        {
            try
            {
                call(entry);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[achievement] {entry.Name}.{what} 异常：{Safe.Describe(e)}");
            }
        }
    }

    /// <summary>NPC 被打死：成就进度各人各的，只服务本机玩家。</summary>
    private static void NpcKilled(On.Terraria.GameContent.Achievements.AchievementsHelper.orig_NotifyNPCKilledDirect orig,
                                  Player player, int npcNetId)
    {
        orig(player, npcNetId);

        if (player is null || player.whoAmI != Main.myPlayer) return;
        Notify(entry => entry.OnNPCKilled(player, npcNetId), nameof(ModAchievement.OnNPCKilled));
    }

    /// <summary>方块被拆掉：游戏只在真的拆掉时才走这条通知。</summary>
    private static void TileDestroyed(On.Terraria.GameContent.Achievements.AchievementsHelper.orig_NotifyTileDestroyed orig,
                                      Player player, ushort tile)
    {
        orig(player, tile);

        if (player is null || player.whoAmI != Main.myPlayer) return;
        Notify(entry => entry.OnTileDestroyed(player, tile), nameof(ModAchievement.OnTileDestroyed));
    }

    private static void ItemPickedUp(On.Terraria.GameContent.Achievements.AchievementsHelper.orig_NotifyItemPickup_Player_Item orig,
                                     Player player, Item item)
    {
        orig(player, item);

        if (item is null) return;
        Pickup(player, item, item.stack);
    }

    private static void ItemPickedUpStack(On.Terraria.GameContent.Achievements.AchievementsHelper.orig_NotifyItemPickup_Player_Item_int orig,
                                          Player player, Item item, int customStack)
    {
        orig(player, item, customStack);

        if (item is null) return;
        Pickup(player, item, customStack);
    }

    /// <summary>只有本机玩家的拾取才点亮成就；堆数按游戏给的算，合并进已有堆叠也算一次。</summary>
    private static void Pickup(Player player, Item item, int stack)
    {
        if (player is null || player.whoAmI != Main.myPlayer) return;
        if (item.type <= 0 || stack <= 0) return;

        Notify(entry => entry.OnItemPickup(player, item.type, stack), nameof(ModAchievement.OnItemPickup));
    }

    /// <summary>制作出成品：拿成品类型与堆数通知每条成就。</summary>
    private static void ItemCrafted(On.Terraria.GameContent.Achievements.AchievementsHelper.orig_NotifyItemCraft orig,
                                    Recipe recipe)
    {
        orig(recipe);

        if (recipe?.createItem is not { } result || result.type <= 0) return;
        Notify(entry => entry.OnItemCraft(result.type, result.stack), nameof(ModAchievement.OnItemCraft));
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

        // 游戏按 "Achievements.<键>_Name" 这类不存在的键建名字，菜单会原样显示键名。
        // 这里换成带默认文本的 LocalizedText：有同名语言条目就用条目，没有就显示默认文本。
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

        // 作者在这里 AddCondition / AddIntCondition …，条件挂到 _conditions 上。
        try
        {
            mod.SetStaticDefaults();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[achievement] {key}.SetStaticDefaults 异常：{Safe.Describe(e)}");
        }

        // 条件在作者的 Add* 调用里已经进了成就；这里只补追踪器，不再重复挂（同名的第二条会撞 Dictionary）。
        var conditions = mod.Conditions;
        try
        {
            if (!achievement.HasTracker)
            {
                if (conditions.Count > 1)
                {
                    achievement.UseConditionsCompletedTracker();
                }
                else if (conditions.Count == 1 && conditions[0].GetAchievementTracker() is not null)
                {
                    // "发生过"这类条件没有追踪器，硬挂会让 UseTracker 里空指针。
                    achievement.UseTrackerFromCondition(conditions[0].Name);
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
            manager.RegisterAchievementCategory(key, mod.Category);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[achievement] {key} 注册失败：{Safe.Describe(e)}");
            return;
        }
    }

    // ===================== 菜单图标 =====================

    /// <summary>图标表的列宽 / 列数 / 锁定图标的起点：与 <see cref="ModAchievement"/> 文档一致（592×64）。</summary>
    private const int IconStride = 66, IconColumns = 8, IconLockedX = 528, IconSize = 64;

    /// <summary>
    /// 成就菜单的每个格子画完之后，把 mod 成就盖成作者自己的图标（原版画的是它自己的图集格）。
    /// 位置与缩放用游戏给的参数，不自己算布局；拿不到对应成就就什么都不做。
    /// 未解锁画锁定图标（x=528）；"???" 隐藏文本仍归游戏，本版不改。
    /// </summary>
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

        // 隐藏成就：没完成时名字/描述临时换成 "???"，画完恢复（菜单直接读这两个属性）。
        LocalizedText? savedName = null, savedDescription = null;
        bool hidden = false;
        if (!mod.IsCompleted)
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

        // 只换图集，不自己画：位置、缩放、裁剪全交给游戏原有的一次 Draw，
        // 自己摆位置会偏（图标是居中缩放到格子里的）。图集必须按 592×64 排：
        // x = Index×66 是解锁格，x = 528 是锁定格。
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

    /// <summary>"???" 本地化键不存在时 Language.GetText 原样返回键名，正好就是想要的文本。</summary>
    private static readonly LocalizedText HiddenText = Language.GetText("???");

    // ===================== 完成 =====================

    private static void ConditionComplete(On.Terraria.Achievements.Achievement.orig_OnConditionComplete orig,
                                          Achievement self, AchievementCondition condition)
    {
        var wasCompleted = self.IsCompleted;

        orig(self, condition);

        if (wasCompleted || !self.IsCompleted) return;
        if (AchievementTable.Of(self.Name) is not { } mod) return;

        try
        {
            mod.OnCompleted(self);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[achievement] {mod.Name}.OnCompleted 异常：{Safe.Describe(e)}");
        }
    }
}
