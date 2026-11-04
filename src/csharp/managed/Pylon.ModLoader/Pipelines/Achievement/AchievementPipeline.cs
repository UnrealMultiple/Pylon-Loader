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
        On.Terraria.NPC.NPCLoot += NpcKilled;
        On.Terraria.WorldGen.KillTile += TileDestroyed;
        On.Terraria.Player.GetItem += ItemPickedUp;
        On.Terraria.Main.CraftItem_GrantItem += ItemCrafted;
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

    /// <summary>NPC 被打死：把类型号发给每条成就（原版成就自己的统计走原版通道，这里只服务 mod 成就）。</summary>
    private static void NpcKilled(On.Terraria.NPC.orig_NPCLoot orig, NPC self)
    {
        orig(self);

        if (AchievementTable.Count == 0 || self is null) return;

        var player = Main.LocalPlayer;
        foreach (var entry in AchievementTable.Entries)
        {
            try
            {
                entry.OnNPCKilled(player, self.type);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[achievement] {entry.Name}.OnNPCKilled 异常：{Safe.Describe(e)}");
            }
        }
    }

    /// <summary>方块被挖掉：只在真的挖掉（fail = false）且不是刷怪时通知。</summary>
    private static void TileDestroyed(On.Terraria.WorldGen.orig_KillTile orig, int i, int j, bool fail, bool effectOnly,
                                      bool noItem)
    {
        var tile = Main.tile[i, j];
        var type = tile.active() ? tile.type : -1;

        orig(i, j, fail, effectOnly, noItem);

        if (fail || effectOnly || noItem) return;
        if (AchievementTable.Count == 0 || type <= 0) return;

        var player = Main.LocalPlayer;
        foreach (var entry in AchievementTable.Entries)
        {
            try
            {
                entry.OnTileDestroyed(player, type);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[achievement] {entry.Name}.OnTileDestroyed 异常：{Safe.Describe(e)}");
            }
        }
    }

    /// <summary>
    /// 物品进背包（掉落物、宝箱里拿、买、捡）：只服务本机玩家——成就进度是各人各的，
    /// 别人的拾取不该点亮你的成就。合并进已有堆叠也算一次，和游戏自己的计数口径一致。
    /// </summary>
    private static Item ItemPickedUp(On.Terraria.Player.orig_GetItem orig, Player self, Item newItem,
                                     GetItemSettings settings, bool disableMerge)
    {
        var before = newItem is null ? 0 : newItem.stack;
        var overflow = orig(self, newItem, settings, disableMerge);

        if (AchievementTable.Count == 0 || newItem is null || newItem.type <= 0) return overflow;
        if (self is null || !self.active || self.whoAmI != Main.myPlayer) return overflow;

        // GetItem 会把 newItem.stack 扣成"没装进去的剩余量"：差值才是真正捡起来的堆数。
        var stack = before - newItem.stack;
        if (stack <= 0) return overflow;

        var player = self;
        var type = newItem.type;

        foreach (var entry in AchievementTable.Entries)
        {
            try
            {
                entry.OnItemPickup(player, type, stack);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[achievement] {entry.Name}.OnItemPickup 异常：{Safe.Describe(e)}");
            }
        }

        return overflow;
    }

    /// <summary>
    /// 制作出成品（游戏发成品的那一步）：拿成品类型与堆数通知每条成就。
    /// 这是唯一一处"成品已经确定"的位置，比在配方表里找结果可靠。
    /// </summary>
    private static void ItemCrafted(On.Terraria.Main.orig_CraftItem_GrantItem orig, Recipe recipe, Item result,
                                    bool quickCraft)
    {
        orig(recipe, result, quickCraft);

        if (AchievementTable.Count == 0 || result is null || result.type <= 0) return;

        var type = result.type;
        var stack = result.stack;

        foreach (var entry in AchievementTable.Entries)
        {
            try
            {
                entry.OnItemCraft(type, stack);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[achievement] {entry.Name}.OnItemCraft 异常：{Safe.Describe(e)}");
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

        // 作者在这里 AddCondition / AddIntCondition …，条件挂到 _conditions 上。
        try
        {
            mod.SetStaticDefaults();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[achievement] {key}.SetStaticDefaults 异常：{Safe.Describe(e)}");
        }

        var conditions = mod.Conditions;
        for (var index = 0; index < conditions.Count; index++)
        {
            try
            {
                achievement.AddCondition(conditions[index]);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[achievement] {key} 的第 {index} 个条件挂不上：{Safe.Describe(e)}");
            }
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
        orig(self, gridLayout, index, position, scale);

        if (AchievementTable.Count == 0 || self is null) return;

        var list = self.SortedAchievementsData;
        if (list is null || (uint)index >= (uint)list.Count) return;

        var achievement = list[index];
        if (achievement is null) return;
        if (AchievementTable.Of(achievement.Name) is not { } mod) return;

        var texture = mod.Texture?.Value;
        if (texture is null) return;

        // 隐藏成就：没完成时名字/描述临时换成 "???"，画完恢复（游戏菜单直接读这两个属性）。
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

        try
        {
            DrawIcon(orig, self, gridLayout, index, position, scale, achievement, mod, texture);
        }
        finally
        {
            if (hidden)
            {
                achievement.FriendlyName = savedName!;
                achievement.Description = savedDescription!;
            }
        }
    }

    /// <summary>"???" 本地化键不存在时 Language.GetText 原样返回键名，正好就是想要的文本。</summary>
    private static readonly LocalizedText HiddenText = Language.GetText("???");

    private static void DrawIcon(On.GUIAchievementsMenu.orig_AchievementDraw orig, GUIAchievementsMenu self,
                                 ItemGrid_Layout gridLayout, int index, Vector2 position, float scale,
                                 Achievement achievement, ModAchievement mod, Texture2D texture)
    {
        if (texture.Width < IconSize || texture.Height < IconSize) return;

        var spriteBatch = Main.spriteBatch;
        if (spriteBatch is null) return;

        bool completed;
        try
        {
            completed = mod.IsCompleted;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[achievement] {mod.Name}.IsCompleted 异常：{Safe.Describe(e)}");
            completed = false;
        }

        var column = 0;
        try
        {
            column = mod.Index;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[achievement] {mod.Name}.Index 异常：{Safe.Describe(e)}");
        }

        if (column < 0 || column >= IconColumns) column = 0;

        var source = completed
            ? new Rectangle(column * IconStride, 0, IconSize, IconSize)
            : new Rectangle(IconLockedX, 0, IconSize, IconSize);

        // 贴图不够宽（作者只画了 8 列没画锁定格）时退回已解锁那一格，至少不是空白。
        if (!completed && IconLockedX + IconSize > texture.Width) source = new Rectangle(column * IconStride, 0, IconSize, IconSize);
        if (source.X + IconSize > texture.Width) source = new Rectangle(0, 0, IconSize, IconSize);

        var sourceRect = new Il2CppSystem.Nullable<Rectangle>(source);
        var drawPosition = new Il2CppSystem.Nullable<Vector2>(position);
        var origin = new Il2CppSystem.Nullable<Vector2>(Vector2.Zero);
        var drawScale = new Il2CppSystem.Nullable<Vector2>(new Vector2(scale, scale));
        var color = new Il2CppSystem.Nullable<Color>(Color.White);

        spriteBatch.Draw(texture, drawPosition, sourceRect, null, origin, 0f, drawScale, color, SpriteEffects.None, 0f);
    }

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
