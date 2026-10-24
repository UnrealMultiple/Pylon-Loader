using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Globals;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipelines;
using Terraria;
using Terraria.GameContent.Creative;

namespace Pylon.ModLoader.Content;
/// <summary>Item.TML.cs 的等价扩展成员；游戏 Item 来自 interop 代理，用 extension block 补作者 API。</summary>
public static class ItemTml
{
    private static readonly Dictionary<int, int> Research = [];

    private static readonly object BagGate = new();
    private static bool[] _preHardmodeLikeBossBag = [];

    extension(Terraria.ID.ItemID.Sets)
    {
        /// <summary>
        /// 「肉前袋」Boss 袋集合（tML <c>ItemID.Sets.PreHardmodeLikeBossBag</c>）：这里的袋子开包时不掷开发者套件。
        /// 首次访问按 BossBag 表长惰性扩容并预填七个原版肉前袋；mod 袋子标 true：
        /// <c>ItemID.Sets.PreHardmodeLikeBossBag[Type] = true;</c>
        /// 消费点：开 mod Boss 袋的开发者套件掷取（Loot 管线，原版袋走原版自己的链）。
        /// </summary>
        public static bool[] PreHardmodeLikeBossBag
        {
            get { lock (BagGate) return GrowPreHardmodeLikeBossBag(); }
        }
    }

    extension(Item item)
    {
        public ModItem? ModItem => ItemPipeline.Instance.InstanceOf(item);

        public IReadOnlyList<GlobalItem> EntityGlobals => GlobalItemRegistry.Of(item);

        public T GetGlobalItem<T>() where T : GlobalItem
        {
            foreach (var global in GlobalItemRegistry.Of(item))
                if (global is T typed) return typed;

            throw new KeyNotFoundException($"{item.type} 上没有 {typeof(T).FullName}");
        }

        public T GetGlobalItem<T>(T baseInstance) where T : GlobalItem
        {
            foreach (var global in GlobalItemRegistry.Of(item))
                if (global is T typed) return typed;

            return baseInstance;
        }

        public bool TryGetGlobalItem<T>(out T? result) where T : GlobalItem
        {
            foreach (var global in GlobalItemRegistry.Of(item))
            {
                if (global is not T typed) continue;
                result = typed;
                return true;
            }

            result = null;
            return false;
        }

        public WorldItem? GetWorldItem() => ItemPickupHooks.WorldItemOf(item);

        public void CloneDefaults(int TypeToClone)
        {
            var type = item.type;
            var material = item.material;
            var mod = ItemPipeline.Instance.InstanceOf(item);
            var globals = GlobalItemRegistry.Of(item);

            item.SetDefaults(TypeToClone, null);
            item.type = type;
            item.material = material;

            ItemPipeline.Instance.Restore(item, mod);
            GlobalItemRegistry.Restore(item, globals);
        }

        public void DefaultToPlaceableWall(int wallToPlace) => item.DefaultToPlaceableWall((ushort)wallToPlace);

        public void DefaultToMusicBox(int tileIDToPlace, int style = 0)
        {
            item.DefaultToMusicBox(style);
            item.createTile = tileIDToPlace;
            item.placeStyle = style;
        }

        public int ResearchUnlockCount
        {
            get => ResearchCountOf(item.type);
            set => SetResearchCount(item.type, value);
        }

        public static Item Create(int setDefaultsToType, int stack = 1, int prefix = 0)
        {
            var created = new Item();
            created.SetDefaults(setDefaultsToType, null);
            if (created.type > 0)
            {
                created.stack = stack;
                if (prefix > 0) created.Prefix(prefix);
            }

            return created;
        }

        public DamageClass DamageType
        {
            get => DamageClassLoader.ItemClass(item);
            set => DamageClassLoader.SetItemClass(item, value);
        }

        public bool CountsAsClass<T>() where T : DamageClass => DamageClassLoader.ItemClass(item).CountsAsClass(DamageClassLoader.InstanceOf<T>());

        public bool CountsAsClass(DamageClass damageClass) => DamageClassLoader.ItemClass(item).CountsAsClass(damageClass);
    }

    internal static void Install()
    {
        try
        {
            On.Terraria.GameContent.Creative.CreativeItemSacrificesCatalog.Initialize += Initialize;
        }
        catch (Exception e)
        {
            Safe.Warn("Item.ResearchUnlockCount 安装失败：" + Safe.Describe(e));
        }
    }

    /// <summary>这个袋子开包时要不要掷开发者套件（原版袋走原版自己的链；这边给 mod 袋用）。</summary>
    internal static bool RollsDevArmor(int type)
    {
        var bossBag = Terraria.ID.ItemID.Sets.BossBag;
        if (bossBag is null || (uint)type >= (uint)bossBag.Length || !bossBag[type]) return false;
        return !IsPreHardmodeLikeBossBag(type);
    }

    private static bool IsPreHardmodeLikeBossBag(int type)
    {
        if (type <= 0) return false;
        lock (BagGate)
        {
            var set = GrowPreHardmodeLikeBossBag();
            return (uint)type < (uint)set.Length && set[type];
        }
    }

    private static bool[] GrowPreHardmodeLikeBossBag()
    {
        var bossBag = Terraria.ID.ItemID.Sets.BossBag;
        var count = bossBag?.Length ?? 0;
        if (count <= 0) return _preHardmodeLikeBossBag;
        if (_preHardmodeLikeBossBag.Length >= count) return _preHardmodeLikeBossBag;

        var first = _preHardmodeLikeBossBag.Length == 0;
        var grown = new bool[count];
        Array.Copy(_preHardmodeLikeBossBag, grown, _preHardmodeLikeBossBag.Length);
        if (first)
        {
            foreach (var type in new short[]
                     {
                         Terraria.ID.ItemID.KingSlimeBossBag,
                         Terraria.ID.ItemID.EyeOfCthulhuBossBag,
                         Terraria.ID.ItemID.EaterOfWorldsBossBag,
                         Terraria.ID.ItemID.BrainOfCthulhuBossBag,
                         Terraria.ID.ItemID.QueenBeeBossBag,
                         Terraria.ID.ItemID.SkeletronBossBag,
                         Terraria.ID.ItemID.DeerclopsBossBag,
                     })
            {
                if ((uint)type < (uint)count) grown[type] = true;
            }
        }

        _preHardmodeLikeBossBag = grown;
        return grown;
    }

    private static void Initialize(On.Terraria.GameContent.Creative.CreativeItemSacrificesCatalog.orig_Initialize orig,
                                   CreativeItemSacrificesCatalog self)
    {
        orig(self);
        foreach (var pair in Research) ApplyResearch(pair.Key, pair.Value);
    }

    private static int ResearchCountOf(int type)
    {
        var table = CreativeItemSacrificesCatalog.Instance?.SacrificeCountNeededByItemId;
        if (table is null) return Research.TryGetValue(type, out var pending) ? pending : 0;
        return table.ContainsKey(type) ? table[type] : 0;
    }

    private static void SetResearchCount(int type, int count)
    {
        Research[type] = count;
        ApplyResearch(type, count);
    }

    private static void ApplyResearch(int type, int count)
    {
        var table = CreativeItemSacrificesCatalog.Instance?.SacrificeCountNeededByItemId;
        if (table is null) return;

        if (count < 1)
        {
            if (table.ContainsKey(type)) table.Remove(type);
            return;
        }

        table[type] = count;
    }
}
