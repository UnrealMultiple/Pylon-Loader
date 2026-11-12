using System.Reflection;
using Terraria;
using Terraria.GameContent.Prefixes;
using Terraria.ID;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>
/// 前缀表：类型 ↔ 实例、分类索引、名字表、原版表读取。
/// 安装期与运行时写的是同一张表，所以不依赖管线实例。
/// </summary>
internal static class PrefixTable
{
    private static readonly List<ModPrefix> ByType = [];
    private static readonly Dictionary<int, List<ModPrefix>> ByCategory = [];
    private static readonly Dictionary<int, int[]> CategoriesByType = [];
    private static readonly Dictionary<int, Name> Names = [];
    private static readonly HashSet<int> Reported = [];
    private static int _vanilla = -1;

    internal readonly record struct Name(string Key, string Text);

    internal static int VanillaCount => _vanilla >= 0 ? _vanilla : _vanilla = PrefixID.Count;

    internal static int Count => VanillaCount + ByType.Count;

    internal static ModPrefix? Of(int type)
    {
        var index = type - VanillaCount;
        if (index < 0 || index >= ByType.Count) return null;
        return ByType[index];
    }

    internal static List<ModPrefix> InCategory(int category) =>
        ByCategory.TryGetValue(category, out var list) ? list : [];

    /// <summary>注册期只记账：那时语言还没加载，写进 Lang.prefix 会让启动卡住。</summary>
    internal static void Register(ModPrefix prefix)
    {
        if (prefix is null) return;

        var category = prefix.Category;
        if (category < ModPrefix.CategoryMelee || category > ModPrefix.CategoryCustom)
        {
            // 注册是一次遍历：抛出去会带走同批内容，所以退成 Custom + 报一条。
            Report(prefix, $"Category 要用 0..6，收到 {category}；按 Custom 处理");
            category = ModPrefix.CategoryCustom;
        }

        if (!ByCategory.TryGetValue(category, out var list)) ByCategory[category] = list = [];
        list.Add(prefix);

        // 槽表：运行时按号反查全靠它（all[type - VanillaCount] = prefix）。
        var slot = prefix.Type - VanillaCount;
        if (slot < 0)
        {
            Report(prefix, $"前缀号 {prefix.Type} 落在原版区间（原版 {VanillaCount} 个）");
        }
        else
        {
            while (ByType.Count <= slot) ByType.Add(null!);
            ByType[slot] = prefix;
        }

        var text = string.IsNullOrEmpty(prefix.DisplayName) ? prefix.Name : prefix.DisplayName;
        var key = "Mods." + (prefix.Mod is not null ? prefix.Mod.Name : "Mod") + ".Prefixes." + prefix.Name + ".DisplayName";
        Names[prefix.Type] = new Name(key, text);

        // item.prefix 是 byte：超过 255 会串号，存档也跟着错位。
        if (prefix.Type > 255)
            Report(prefix, $"前缀号 {prefix.Type} 超过 255（item.prefix 是 byte），存档会错位");
    }

    /// <summary>内容就绪：写表 → 标记 mod 物品可加前缀 → 跑静态默认值。</summary>
    internal static void Build(IModLog log)
    {
        var all = ModContent.GetContent<ModPrefix>();
        if (all.Count == 0) return;

        WriteNames();
        MarkPrefixable(log);

        foreach (var prefix in all)
        {
            prefix.Hooks = HookScan.Of(prefix.GetType(), typeof(ModPrefix));

            try
            {
                prefix.SetStaticDefaults();
            }
            catch (Exception e)
            {
                log.Warn($"{prefix.Name}.SetStaticDefaults 异常：{e.Message}");
            }
        }

        log.Info($"[prefix] 安装：{all.Count} 个（类型 {VanillaCount}..{Count - 1}）");
    }

    /// <summary>Lang.prefix 与 ReducedNaturalChance 跟着前缀总数长；换语言后游戏会重建，所以每次都要重填。</summary>
    internal static void WriteNames()
    {
        var total = Count;

        var names = Lang.prefix;
        if (names is null || names.Length < total) Lang.prefix = Grow(names, total);

        var reduced = PrefixID.Sets.ReducedNaturalChance;
        if (reduced is null || reduced.Length < total) PrefixID.Sets.ReducedNaturalChance = Grow(reduced, total);

        var table = Lang.prefix;
        if (table is null) return;

        foreach (var pair in Names)
        {
            var type = pair.Key;
            if (type < 0 || type >= table.Length) continue;

            var entry = pair.Value;
            LocalizationKeys.Register(entry.Key, new LocalizedText(entry.Key, entry.Text));
            table[type] = Language.GetText(entry.Key);
        }
    }

    /// <summary>mod 物品在 CanGetPrefixes 里是扩表时按 false 填的，会连 Item.Prefix 都进不去。</summary>
    private static void MarkPrefixable(IModLog log)
    {
        var canGet = ItemID.Sets.CanGetPrefixes;
        if (canGet is null)
        {
            log.Warn("[prefix] 没有 ItemID.Sets.CanGetPrefixes，mod 物品前缀不可用");
            return;
        }

        var marked = 0;
        foreach (var item in ModContent.GetContent<ModItem>())
        {
            var type = item.Type;
            if (type <= 0 || type >= canGet.Length) continue;

            bool prefixable;
            try
            {
                prefixable = IsPrefixable(type);
            }
            catch (Exception e)
            {
                Safe.Report("prefix." + item.Name, e);
                continue;
            }

            if (!prefixable) continue;
            canGet[type] = true;
            marked++;
        }

        if (marked > 0) log.Info($"[prefix] mod 物品可加前缀：{marked} 件");
    }

    private static bool IsPrefixable(int type)
    {
        var sample = new Item();
        sample.SetDefaults(type, null);
        if (sample.accessory) return sample.IsAPrefixableAccessory();

        return sample.damage > 0 && sample.ammo == 0 && !sample.consumable;
    }

    private static readonly Dictionary<Type, HookMask> ItemMasks = [];

    /// <summary>ModItem 的钩子位集（按基类 ModItem 算，缓存在这里，不动物品管线的基类）。</summary>
    internal static bool HasHook(ModItem item, HookId hook)
    {
        var type = item.GetType();
        if (!ItemMasks.TryGetValue(type, out var mask))
        {
            mask = HookScan.Of(type, typeof(ModItem));
            ItemMasks[type] = mask;
        }

        return mask.Has(hook);
    }

    /// <summary>物品能拿哪些分类的前缀：原版写死的 id 集合 + 作者在 ModItem 上的问询，任一命中即算。</summary>
    internal static int[] CategoriesOf(Item item)
    {
        var type = item.type;
        if (type > 0 && CategoriesByType.TryGetValue(type, out var cached)) return cached;

        var list = new List<int>(3);
        var modItem = ModContent.Find(ContentKind.Item, type) as ModItem;
        var weapon = modItem is not null && item.damage > 0 && item.ammo == 0 && !item.consumable;

        void Take(int category, bool legacy, Func<bool> ask)
        {
            if (legacy) list.Add(category);
            else if (weapon && Ask(modItem!, ask)) list.Add(category);
        }

        Take(ModPrefix.CategoryMelee, LegacySet("SwordsHammersAxesPicks", type), () => modItem!.MeleePrefix(item));
        Take(ModPrefix.CategoryRanged, LegacySet("GunsBows", type), () => modItem!.RangedPrefix(item));
        Take(ModPrefix.CategoryMagic, LegacySet("Magic", type), () => modItem!.MagicPrefix(item));
        Take(ModPrefix.CategorySummon, LegacySet("Summon", type), () => modItem!.SummonPrefix(item));

        var anyWeapon = LegacySet("SpearsMacesChainsawsDrillsPunchCannon", type) ||
                        LegacySet("BoomerangsChakrams", type) ||
                        LegacySet("ItemsThatCanHaveLegendary2", type) ||
                        (weapon && Ask(modItem!, () => modItem!.WeaponPrefix(item))) ||
                        list.Count > 0;
        if (anyWeapon) list.Add(ModPrefix.CategoryAnyWeapon);

        try
        {
            if (item.IsAPrefixableAccessory()) list.Add(ModPrefix.CategoryAccessory);
        }
        catch (Exception e)
        {
            Safe.Report("prefix.accessory", e);
        }

        var result = list.ToArray();
        if (type > 0) CategoriesByType[type] = result;
        return result;
    }

    private static bool Ask(ModItem modItem, Func<bool> question)
    {
        try
        {
            return question();
        }
        catch (Exception e)
        {
            Safe.Report("prefix." + modItem.Name + ".category", e);
            return false;
        }
    }

    /// <summary>某个分类在游戏里对应的原版前缀表（mod 物品要靠它拿到原版前缀）。</summary>
    internal static int[] VanillaPrefixes(int category) => category switch
    {
        ModPrefix.CategoryMelee => Legacy("Prefixes", "PrefixesForSwords") as int[] ?? [],
        ModPrefix.CategoryRanged => Legacy("Prefixes", "PrefixesForGunsBows") as int[] ?? [],
        ModPrefix.CategoryMagic => Legacy("Prefixes", "PrefixesForMagic") as int[] ?? [],
        ModPrefix.CategorySummon => Legacy("Prefixes", "PrefixesForSummons") as int[] ?? [],
        ModPrefix.CategoryAnyWeapon => Legacy("Prefixes", "PrefixesForSpears") as int[] ?? [],
        ModPrefix.CategoryAccessory => Legacy("Prefixes", "PrefixesForAccessories") as int[] ?? [],
        _ => [],
    };

    private static void Report(ModPrefix prefix, string message)
    {
        if (!Reported.Add(prefix.Name.GetHashCode())) return;
        LoaderHost.Current.Log.Warn($"[prefix] {prefix.Name}: {message}");
    }

    // PrefixLegacy 的两个嵌套类在 interop 里是 internal：反射读一次，结果缓存。
    private static readonly Dictionary<string, Type?> LegacyTypes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, object?> LegacyValues = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LegacyDumped = new(StringComparer.Ordinal);

    private static Type? LegacyType(string nested)
    {
        if (LegacyTypes.TryGetValue(nested, out var cached)) return cached;

        Type? found = null;
        try
        {
            found = typeof(PrefixLegacy).GetNestedType(nested, BindingFlags.Public | BindingFlags.NonPublic);
        }
        catch
        {
            // 没有这个嵌套类型时按缺表处理。
        }

        LegacyTypes[nested] = found;
        return found;
    }

    private static object? Legacy(string nested, string member)
    {
        var key = nested + "." + member;
        if (LegacyValues.TryGetValue(key, out var cached)) return cached;

        object? value = null;
        try
        {
            var type = LegacyType(nested);
            if (type is not null)
            {
                value = type.GetField(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                        ?? type.GetProperty(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);

                if (value is null) Dump(nested, type);
            }
        }
        catch (Exception e)
        {
            LoaderHost.Current.Log.Warn($"[prefix] 读 PrefixLegacy.{key} 失败：{e.Message}");
        }

        LegacyValues[key] = value;
        return value;
    }

    /// <summary>表名对不上时唯一能自证的线索：把真实成员名打一条。</summary>
    private static void Dump(string nested, Type type)
    {
        if (!LegacyDumped.Add(nested)) return;

        try
        {
            var names = new List<string>();
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (member.MemberType is MemberTypes.Field or MemberTypes.Property) names.Add(member.Name);
            }

            LoaderHost.Current.Log.Warn($"[prefix] PrefixLegacy.{nested} → {type.FullName}，成员：{string.Join(",", names)}");
        }
        catch (Exception e)
        {
            LoaderHost.Current.Log.Warn("[prefix] 列出 PrefixLegacy 成员失败：" + e.Message);
        }
    }

    private static bool LegacySet(string member, int type)
    {
        try
        {
            if (Legacy("ItemSets", member) is not Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool> set) return false;
            return type >= 0 && type < set.Length && set[type];
        }
        catch
        {
            return false;
        }
    }

    // 在发布原生表之前完成副本；引用数组的隐式转换仍逐项走运行时索引器与 GC 写屏障。
    private static LocalizedText[] Grow(LocalizedText[]? old, int length)
    {
        var grown = new LocalizedText[length];
        if (old is not null)
        {
            for (var i = 0; i < old.Length && i < length; i++) grown[i] = old[i];
        }

        return grown;
    }

    private static bool[] Grow(bool[]? old, int length)
    {
        var grown = new bool[length];
        if (old is not null)
        {
            for (var i = 0; i < old.Length && i < length; i++) grown[i] = old[i];
        }

        return grown;
    }
}
