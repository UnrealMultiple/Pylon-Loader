using System.Collections.Generic;
using Terraria;
using Terraria.Achievements;
using Terraria.GameContent.Achievements;

namespace Pylon.ModLoader
{
    /// <summary>自定义成就（挂在游戏的 Main.Achievements 上，键是 &lt;mod&gt;/&lt;类名&gt;）；贴图是 592×64 的图标表：x=0 解锁、x=528 锁定。</summary>
    public abstract class ModAchievement : ModType
    {
        /// <summary>游戏这条成就（进度存游戏的成就文件里；管线先建好它、挂本地化文本，再调 SetStaticDefaults）。</summary>
        public Achievement Achievement { get; internal set; }

        /// <summary>图标表相对 Mod 目录的路径（592×64：x=0 解锁、x=528 锁定；Index 是列/行，每列 66 宽，一行八列）；ContentTexture 默认值归管线。</summary>
        public override string TexturePath => $"assets/achievements/{Name}.png";

        /// <summary>图标在表里的位置（列/行）。</summary>
        public virtual int Index => 0;

        /// <summary>分类：Slayer、Collector、Explorer 或 Challenger（Terraria.Achievements.AchievementCategory）。</summary>
        public virtual AchievementCategory Category => AchievementCategory.Slayer;

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

        private readonly List<AchievementCondition> _conditions = new();

        /// <summary>加一个"发生/没发生"的条件；返回游戏的条件（CustomFlagCondition.Create）。</summary>
        public AchievementCondition AddCondition(string key = "Condition") =>
            AddRaw(CustomFlagCondition.Create(key));

        /// <summary>加一个计数（整数）的条件；key 省略 = 'Condition'，返回的条件有 .Value。</summary>
        public CustomIntCondition AddIntCondition(int maxValue) => AddIntCondition("Condition", maxValue);

        /// <summary>加一个计数（整数）的条件；key 省略 = 'Condition'，返回的条件有 .Value。</summary>
        public CustomIntCondition AddIntCondition(string key, int maxValue) =>
            AddRaw((CustomIntCondition)CustomIntCondition.Create(key, maxValue));

        /// <summary>加一个计数（小数）的条件；key 省略 = 'Condition'，返回的条件有 .Value。</summary>
        public CustomFloatCondition AddFloatCondition(float maxValue) => AddFloatCondition("Condition", maxValue);

        /// <summary>加一个计数（小数）的条件；key 省略 = 'Condition'，返回的条件有 .Value。</summary>
        public CustomFloatCondition AddFloatCondition(string key, float maxValue) =>
            AddRaw((CustomFloatCondition)CustomFloatCondition.Create(key, maxValue));

        /// <summary>加一个"做过这些东西"的条件；单个或一列物品 ID（照游戏的 short Create）。</summary>
        public AchievementCondition AddItemCraftCondition(short itemId) =>
            AddRaw(ItemCraftCondition.Create(LocalUser.Active, itemId));

        /// <summary>加一个"做过这些东西"的条件；单个或一列物品 ID（照游戏的 short[] Create）。</summary>
        public AchievementCondition AddItemCraftCondition(short[] itemIds) =>
            AddRaw(ItemCraftCondition.Create(LocalUser.Active, itemIds));

        /// <summary>加一个"捡过这些东西"的条件；单个或一列物品 ID（照游戏的 short Create）。</summary>
        public AchievementCondition AddItemPickupCondition(short itemId) =>
            AddRaw(ItemPickupCondition.Create(LocalUser.Active, itemId));

        /// <summary>加一个"捡过这些东西"的条件；单个或一列物品 ID（照游戏的 short[] Create）。</summary>
        public AchievementCondition AddItemPickupCondition(short[] itemIds) =>
            AddRaw(ItemPickupCondition.Create(LocalUser.Active, itemIds));

        /// <summary>加一个"杀过这些东西"的条件；单个或一列 NPC ID（照游戏的 short Create）。</summary>
        public AchievementCondition AddNPCKilledCondition(short npcId) =>
            AddRaw(NPCKilledCondition.Create(LocalUser.Active, npcId));

        /// <summary>加一个"杀过这些东西"的条件；单个或一列 NPC ID（照游戏的 short[] Create）。</summary>
        public AchievementCondition AddNPCKilledCondition(short[] npcIds) =>
            AddRaw(NPCKilledCondition.Create(LocalUser.Active, npcIds));

        /// <summary>加一个"拆过这些方块"的条件；单个或一列 tile ID（本版游戏只有数组 Create，单个包成数组）。</summary>
        public AchievementCondition AddTileDestroyedCondition(ushort tileId) =>
            AddTileDestroyedCondition(new[] { tileId });

        /// <summary>加一个"拆过这些方块"的条件；单个或一列 tile ID（照游戏的 ushort[] Create）。</summary>
        public AchievementCondition AddTileDestroyedCondition(ushort[] tileIds) =>
            AddRaw(TileDestroyedCondition.Create(LocalUser.Active, tileIds));

        /// <summary>挂上成就、记进 Conditions、原样返回（泛型保住具体类型，好让 .Value 可用）。</summary>
        private T AddRaw<T>(T condition) where T : AchievementCondition
        {
            Achievement.AddCondition(condition);
            _conditions.Add(condition);
            return condition;
        }

        // static register 故意没翻：校验、Templates.Adopt、ContentTexture、AchievementLoader.Add 都是注册管线（ModType 派生类由管线自动发现注册，进度重读归 Harmony 层）。
    }
}