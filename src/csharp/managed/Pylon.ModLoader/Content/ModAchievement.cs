using Il2CppInterop.Runtime.InteropTypes;
using Terraria;
using Terraria.Achievements;
using Terraria.GameContent.Achievements;

namespace Pylon.ModLoader.Content;
/// <summary>
/// 自定义成就（挂在游戏的成就管理器上，键是 &lt;mod&gt;/&lt;类名&gt;，进度存游戏的成就文件）。
/// 条件只能在 SetStaticDefaults 里加；完成那一下派 OnCompleted；
/// 击杀/拾取/制作/拆方块/进度事件的通知走 <see cref="AchievementsHelper"/> 的静态事件。
/// 贴图是 592×64 的图标表：x=Index×66 解锁、x=528 锁定。
/// </summary>
public abstract class ModAchievement : ModType
{
    public Achievement Achievement { get; internal set; } = null!;

    public override string TexturePath => $"assets/achievements/{Name}.png";

    public virtual int Index => 0;

    public string LocalizationCategory => "Achievements";

    public virtual bool Hidden => false;

    public virtual string DisplayName =>
        System.Text.RegularExpressions.Regex.Replace(Name, "([A-Z])", " $1").Trim();

    public virtual string Description => "";

    public override void SetStaticDefaults() { }

    public virtual void OnCompleted(Achievement achievement) { }

    internal IReadOnlyList<AchievementCondition> Conditions => _conditions;

    private readonly List<AchievementCondition> _conditions = [];

    static LocalUser User => LocalUser.Active is null ? Main.Achievements.User : LocalUser.Active;

    static short[] Shorts(int[] ids) => ids.Select(x => (short)x).ToArray();

    static ushort[] UShorts(int[] ids) => ids.Select(x => (ushort)x).ToArray();

    public CustomFlagCondition AddCondition(string key = "Condition") => Add(AsFlag(CustomFlagCondition.Create(key)), key);

    public CustomIntCondition AddIntCondition(int maxValue) => AddIntCondition("Condition", maxValue);

    public CustomIntCondition AddIntCondition(string key, int maxValue) =>
        Add(AsInt(CustomIntCondition.Create(key, maxValue)), key);

    public CustomFloatCondition AddFloatCondition(float maxValue) => AddFloatCondition("Condition", maxValue);

    public CustomFloatCondition AddFloatCondition(string key, float maxValue) =>
        Add(AsFloat(CustomFloatCondition.Create(key, maxValue)), key);

    public T AddCondition<T>(T condition) where T : Il2CppObjectBase
    {
        Add(new AchievementCondition(condition.Pointer), null);
        return condition;
    }

    public ItemCraftCondition AddItemCraftCondition(int itemId) => Add(AsCraft(ItemCraftCondition.Create(User, (short)itemId)), null);

    public ItemCraftCondition AddItemCraftCondition(string key, int itemId) => Add(AsCraft(ItemCraftCondition.Create(User, (short)itemId)), key);

    public ItemCraftCondition AddItemCraftCondition(int[] itemIds) => Add(AsCraft(ItemCraftCondition.Create(User, Shorts(itemIds))), null);

    public ItemCraftCondition AddItemCraftCondition(string key, int[] itemIds) => Add(AsCraft(ItemCraftCondition.Create(User, Shorts(itemIds))), key);

    public ItemCraftCondition[] AddManyItemCraftCondition(int[] itemIds) => itemIds.Select(AddItemCraftCondition).ToArray();

    public ItemPickupCondition AddItemPickupCondition(int itemId) => Add(AsPickup(ItemPickupCondition.Create(User, (short)itemId)), null);

    public ItemPickupCondition AddItemPickupCondition(string key, int itemId) => Add(AsPickup(ItemPickupCondition.Create(User, (short)itemId)), key);

    public ItemPickupCondition AddItemPickupCondition(int[] itemIds) => Add(AsPickup(ItemPickupCondition.Create(User, Shorts(itemIds))), null);

    public ItemPickupCondition AddItemPickupCondition(string key, int[] itemIds) => Add(AsPickup(ItemPickupCondition.Create(User, Shorts(itemIds))), key);

    public ItemPickupCondition[] AddManyItemPickupCondition(int[] itemIds) => itemIds.Select(AddItemPickupCondition).ToArray();

    public NPCKilledCondition AddNPCKilledCondition(int npcID) => Add(AsNpc(NPCKilledCondition.Create(User, (short)npcID)), null);

    public NPCKilledCondition AddNPCKilledCondition(string key, int npcID) => Add(AsNpc(NPCKilledCondition.Create(User, (short)npcID)), key);

    public NPCKilledCondition AddNPCKilledCondition(int[] npcIDs) => Add(AsNpc(NPCKilledCondition.Create(User, Shorts(npcIDs))), null);

    public NPCKilledCondition AddNPCKilledCondition(string key, int[] npcIDs) => Add(AsNpc(NPCKilledCondition.Create(User, Shorts(npcIDs))), key);

    public NPCKilledCondition[] AddManyNPCKilledCondition(int[] npcIDs) => npcIDs.Select(AddNPCKilledCondition).ToArray();

    public TileDestroyedCondition AddTileDestroyedCondition(int[] tileIds) => Add(AsTile(TileDestroyedCondition.Create(User, UShorts(tileIds))), null);

    public TileDestroyedCondition AddTileDestroyedCondition(string key, int[] tileIds) => Add(AsTile(TileDestroyedCondition.Create(User, UShorts(tileIds))), key);

    public abstract class Position { }

    public static Position BeforeFirstVanillaAchievement => new Before("TIMBER");

    public static Position AfterLastVanillaAchievement => new Default();

    public static Position BeforeFirstAdvisorAchievement => new Default();

    public static Position AfterLastAdvisorAchievement => new Default();

    public sealed class Default : Position { }

    public sealed class Before : Position
    {
        public Achievement Achievement { get; }

        public Before(Achievement achievement) => Achievement = achievement;

        public Before(ModAchievement modAchievement) => Achievement = modAchievement.Achievement;

        public Before(string achievementIdentifier) => Achievement = Main.Achievements.GetAchievement(achievementIdentifier);
    }

    public sealed class After : Position
    {
        public Achievement Achievement { get; }

        public After(Achievement achievement) => Achievement = achievement;

        public After(ModAchievement modAchievement) => Achievement = modAchievement.Achievement;

        public After(string achievementIdentifier) => Achievement = Main.Achievements.GetAchievement(achievementIdentifier);
    }

    public virtual Position GetDefaultPosition() => AfterLastVanillaAchievement;

    public virtual Position? GetAdvisorPosition() => null;

    public virtual IEnumerable<Position>? GetModdedConstraints() => null;

    private T Add<T>(T condition, string? key) where T : AchievementCondition
    {
        if (Achievement is null)
            throw new InvalidOperationException($"achievement {Name}: conditions can only be added in SetStaticDefaults");
        if (key is not null)
            condition.Name = key;
        Achievement.AddCondition(condition);
        _conditions.Add(condition);
        return condition;
    }

    private static CustomFlagCondition AsFlag(AchievementCondition condition) =>
        condition is CustomFlagCondition typed ? typed : new CustomFlagCondition(condition.Pointer);

    private static CustomIntCondition AsInt(AchievementCondition condition) =>
        condition is CustomIntCondition typed ? typed : new CustomIntCondition(condition.Pointer);

    private static CustomFloatCondition AsFloat(AchievementCondition condition) =>
        condition is CustomFloatCondition typed ? typed : new CustomFloatCondition(condition.Pointer);

    private static ItemCraftCondition AsCraft(AchievementCondition condition) =>
        condition is ItemCraftCondition typed ? typed : new ItemCraftCondition(condition.Pointer);

    private static ItemPickupCondition AsPickup(AchievementCondition condition) =>
        condition is ItemPickupCondition typed ? typed : new ItemPickupCondition(condition.Pointer);

    private static NPCKilledCondition AsNpc(AchievementCondition condition) =>
        condition is NPCKilledCondition typed ? typed : new NPCKilledCondition(condition.Pointer);

    private static TileDestroyedCondition AsTile(AchievementCondition condition) =>
        condition is TileDestroyedCondition typed ? typed : new TileDestroyedCondition(condition.Pointer);
}
