using Terraria;
using Terraria.Achievements;
using Terraria.GameContent.Achievements;

namespace Pylon.ModLoader;

/// <summary>自定义成就（挂在游戏的 Main.Achievements 上，键是 &lt;mod&gt;/&lt;类名&gt;）；贴图是 592×64 的图标表：x=0 解锁、x=528 锁定。</summary>
public abstract class ModAchievement : ModType
{
    /// <summary>游戏这条成就（进度存游戏的成就文件里；管线先建好它、挂本地化文本，再调 SetStaticDefaults）。</summary>
    public Achievement Achievement { get; internal set; } = null!;

    /// <summary>图标表相对 Mod 目录的路径（592×64：x=0 解锁、x=528 锁定；Index 是列/行，每列 66 宽，一行八列）；ContentTexture 默认值归管线。</summary>
    public override string TexturePath => $"assets/achievements/{Name}.png";

    /// <summary>图标在表里的位置（列/行）。</summary>
    public virtual int Index => 0;

    /// <summary>分类：Slayer、Collector、Explorer 或 Challenger（Terraria.Achievements.AchievementCategory）。</summary>
    public virtual AchievementCategory Category => AchievementCategory.Slayer;

    /// <summary>菜单里显示的名字；默认按类名拆词（TestFlagAchievement → Test Flag Achievement）。</summary>
    public virtual string DisplayName =>
        System.Text.RegularExpressions.Regex.Replace(Name, "([A-Z])", " $1").Trim();

    /// <summary>菜单里显示的说明；默认空串。</summary>
    public virtual string Description => "";

    /// <summary>没完成时，名字和说明在菜单里显示 "???"。</summary>
    public virtual bool Hidden => false;

    /// <summary>游戏这条成就完成了没（Achievement 还没挂上时是 false）。</summary>
    public virtual bool IsCompleted => Achievement != null && Achievement.IsCompleted;

    /// <summary>在这里加条件（游戏侧照条件数挑追踪器）：AddCondition、AddIntCondition、AddFloatCondition、AddItemCraftCondition、AddItemPickupCondition、AddNPCKilledCondition、AddTileDestroyedCondition。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>完成时一次（从没完成到完成的那一下，挂 Achievement 的条件完成钩子）。</summary>
    public virtual void OnCompleted(Achievement achievement) { }

    /// <summary>游戏的击杀通知（NotifyNPCKilledDirect），只在还没完成时。</summary>
    public virtual void OnNPCKilled(Player player, int npcId) { }

    /// <summary>游戏的拾取通知（NotifyItemPickup），只在还没完成时。</summary>
    public virtual void OnItemPickup(Player player, int itemType, int stack) { }

    /// <summary>游戏的制作通知（NotifyItemCraft），只在还没完成时。</summary>
    public virtual void OnItemCraft(int itemType, int stack) { }

    /// <summary>游戏的拆方块通知（NotifyTileDestroyed），只在还没完成时。</summary>
    public virtual void OnTileDestroyed(Player player, int tileType) { }

    /// <summary>SetStaticDefaults 里加的条件，按调用顺序；游戏侧读它挑追踪器。</summary>
    public IReadOnlyList<AchievementCondition> Conditions => _conditions;

    private readonly List<AchievementCondition> _conditions = [];

    /// <summary>加一个"发生/没发生"的条件；返回游戏的条件（CustomFlagCondition.Create）。</summary>
    public AchievementCondition AddCondition(string key = "Condition") => AddRaw(CustomFlagCondition.Create(key), key);

    /// <summary>加一个计数（整数）的条件；key 省略 = 'Condition'，返回的条件有 .Value。</summary>
    public CustomIntCondition AddIntCondition(int maxValue) => AddIntCondition("Condition", maxValue);

    /// <summary>加一个计数（整数）的条件；key 省略 = 'Condition'，返回的条件有 .Value。</summary>
    public CustomIntCondition AddIntCondition(string key, int maxValue) =>
        AddRaw(AsCustom(CustomIntCondition.Create(key, maxValue)), key);

    /// <summary>加一个计数（小数）的条件；key 省略 = 'Condition'，返回的条件有 .Value。</summary>
    public CustomFloatCondition AddFloatCondition(float maxValue) => AddFloatCondition("Condition", maxValue);

    /// <summary>加一个计数（小数）的条件；key 省略 = 'Condition'，返回的条件有 .Value。</summary>
    public CustomFloatCondition AddFloatCondition(string key, float maxValue) =>
        AddRaw(AsFloat(CustomFloatCondition.Create(key, maxValue)), key);

    private static CustomFloatCondition AsFloat(AchievementCondition condition) =>
        condition is CustomFloatCondition typed ? typed : new CustomFloatCondition(condition.Pointer);

    // Create 的声明返回类型是基类 AchievementCondition，运行时对象是具体类型；
    // 生成层的强制转换做不到向下转，只能按底层指针重新包一层，.Value 才读得到。
    private static CustomIntCondition AsCustom(AchievementCondition condition) =>
        condition is CustomIntCondition typed ? typed : new CustomIntCondition(condition.Pointer);

    /// <summary>加一个"做过这些东西"的条件；key 省略 = 'Condition'。</summary>
    public AchievementCondition AddItemCraftCondition(short itemId) =>
        AddRaw(ItemCraftCondition.Create(LocalUser.Active, itemId), null);

    /// <summary>加一个"做过这些东西"的条件；key 是该条件在成就里的名字（多条条件必须互不相同）。</summary>
    public AchievementCondition AddItemCraftCondition(string key, short itemId) =>
        AddRaw(ItemCraftCondition.Create(LocalUser.Active, itemId), key);

    /// <summary>加一个"做过这些东西"的条件；单个或一列物品 ID（照游戏的 short[] Create）。</summary>
    public AchievementCondition AddItemCraftCondition(short[] itemIds) =>
        AddRaw(ItemCraftCondition.Create(LocalUser.Active, itemIds), null);

    /// <summary>加一个"拣过这些东西"的条件；key 省略 = 'Condition'。</summary>
    public AchievementCondition AddItemPickupCondition(short itemId) =>
        AddRaw(ItemPickupCondition.Create(LocalUser.Active, itemId), null);

    /// <summary>加一个"拣过这些东西"的条件；key 是该条件在成就里的名字（多条条件必须互不相同）。</summary>
    public AchievementCondition AddItemPickupCondition(string key, short itemId) =>
        AddRaw(ItemPickupCondition.Create(LocalUser.Active, itemId), key);

    /// <summary>加一个"拣过这些东西"的条件；单个或一列物品 ID（照游戏的 short[] Create）。</summary>
    public AchievementCondition AddItemPickupCondition(short[] itemIds) =>
        AddRaw(ItemPickupCondition.Create(LocalUser.Active, itemIds), null);

    /// <summary>加一个"杀过这些东西"的条件；key 省略 = 'Condition'。</summary>
    public AchievementCondition AddNPCKilledCondition(short npcId) =>
        AddRaw(NPCKilledCondition.Create(LocalUser.Active, npcId), null);

    /// <summary>加一个"杀过这些东西"的条件；key 是该条件在成就里的名字（多条条件必须互不相同）。</summary>
    public AchievementCondition AddNPCKilledCondition(string key, short npcId) =>
        AddRaw(NPCKilledCondition.Create(LocalUser.Active, npcId), key);

    /// <summary>加一个"杀过这些东西"的条件；单个或一列 NPC ID（照游戏的 short[] Create）。</summary>
    public AchievementCondition AddNPCKilledCondition(short[] npcIds) =>
        AddRaw(NPCKilledCondition.Create(LocalUser.Active, npcIds), null);

    /// <summary>加一个"拆过这些方块"的条件；单个或一列 tile ID（本版游戏只有数组 Create，单个包成数组）。</summary>
    public AchievementCondition AddTileDestroyedCondition(ushort tileId) => AddTileDestroyedCondition([tileId]);

    /// <summary>加一个"拆过这些方块"的条件；单个或一列 tile ID（照游戏的 ushort[] Create）。</summary>
    public AchievementCondition AddTileDestroyedCondition(ushort[] tileIds) =>
        AddRaw(TileDestroyedCondition.Create(LocalUser.Active, tileIds), null);

    /// <summary>加一个"拆过这些方块"的条件；key 是该条件在成就里的名字。</summary>
    public AchievementCondition AddTileDestroyedCondition(string key, ushort[] tileIds) =>
        AddRaw(TileDestroyedCondition.Create(LocalUser.Active, tileIds), key);

    /// <summary>挂上成就、记进 Conditions、原样返回（泛型保住具体类型，好让 .Value 可用）。</summary>
    private T AddRaw<T>(T condition, string? key) where T : AchievementCondition
    {
        if (key is not null) condition.Name = key;
        Achievement.AddCondition(condition);
        _conditions.Add(condition);
        return condition;
    }
}

// 未移植 static register：校验、Templates.Adopt、ContentTexture、AchievementLoader.Add 都是注册管线
// （ModType 派生类由管线自动发现注册，进度重读归 Harmony 层）。
