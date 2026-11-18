using System.Reflection;
using Terraria;
using Terraria.GameContent.Prefixes;
using Terraria.ID;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>前缀表：类型 ↔ 实例、分类索引、名字表、原版表读取；安装期与运行时共用同一张表，不依赖管线实例。</summary>
internal static class PrefixTable
{
    private static readonly List<ModPrefix> ByType = [];
    private static readonly Dictionary<PrefixCategory, List<ModPrefix>> ByCategory = [];
    private static readonly Dictionary<int, PrefixCategory[]> CategoriesByType = [];
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

    internal static List<ModPrefix> InCategory(PrefixCategory category) =>
        ByCategory.TryGetValue(category, out var list) ? list : [];

    internal static void Register(ModPrefix prefix)
    {
        if (prefix is null) return;

        var category = prefix.Category;
        if (category is < PrefixCategory.AnyWeapon or > PrefixCategory.Custom)
        {
            Report(prefix, $"Category 超出枚举范围（收到 {category}）；按 Custom 处理");
            category = PrefixCategory.Custom;
        }

        if (!ByCategory.TryGetValue(category, out var list)) ByCategory[category] = list = [];
        list.Add(prefix);

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

        if (prefix.Type > 255)
            Report(prefix, $"前缀号 {prefix.Type} 超过 255（item.prefix 是 byte），存档会错位");
    }

    internal static void Build(IModLog log)
    {
        var all = ModContent.GetContent<ModPrefix>();
        if (all.Count == 0) return;

        WriteNames();
        MarkPrefixable(log);

        foreach (var prefix in all)
        {
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

    private static readonly Dictionary<Type, HookMask> PrefixMasks = [];

    internal static bool HasHook(ModPrefix prefix, HookId hook)
    {
        var type = prefix.GetType();
        if (!PrefixMasks.TryGetValue(type, out var mask))
        {
            mask = HookScan.Of(type, typeof(ModPrefix));
            PrefixMasks[type] = mask;
        }

        return mask.Has(hook);
    }

    internal static PrefixCategory[] CategoriesOf(Item item)
    {
        var type = item.type;
        if (type > 0 && CategoriesByType.TryGetValue(type, out var cached)) return cached;

        var list = new List<PrefixCategory>(3);
        var modItem = ItemPipeline.Instance.InstanceOf(item);
        var weapon = modItem is not null && item.damage > 0 && item.ammo == 0 && !item.consumable;

        void Take(PrefixCategory category, bool legacy, Func<bool> ask)
        {
            if (legacy) list.Add(category);
            else if (weapon && Ask(modItem!, ask)) list.Add(category);
        }

        Take(PrefixCategory.Melee, LegacySet("SwordsHammersAxesPicks", type), () => modItem!.MeleePrefix());
        Take(PrefixCategory.Ranged, LegacySet("GunsBows", type), () => modItem!.RangedPrefix());
        Take(PrefixCategory.Magic, LegacySet("Magic", type), () => modItem!.MagicPrefix());
        Take(PrefixCategory.Summon, LegacySet("Summon", type), () => modItem!.SummonPrefix());

        var anyWeapon = LegacySet("SpearsMacesChainsawsDrillsPunchCannon", type) ||
                        LegacySet("BoomerangsChakrams", type) ||
                        LegacySet("ItemsThatCanHaveLegendary2", type) ||
                        (weapon && Ask(modItem!, () => modItem!.WeaponPrefix())) ||
                        list.Count > 0;
        if (anyWeapon) list.Add(PrefixCategory.AnyWeapon);

        try
        {
            if (item.IsAPrefixableAccessory()) list.Add(PrefixCategory.Accessory);
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

    internal static int[] VanillaPrefixes(PrefixCategory category) => category switch
    {
        PrefixCategory.Melee => Legacy("Prefixes", "PrefixesForSwords") as int[] ?? [],
        PrefixCategory.Ranged => Legacy("Prefixes", "PrefixesForGunsBows") as int[] ?? [],
        PrefixCategory.Magic => Legacy("Prefixes", "PrefixesForMagic") as int[] ?? [],
        PrefixCategory.Summon => Legacy("Prefixes", "PrefixesForSummons") as int[] ?? [],
        PrefixCategory.AnyWeapon => Legacy("Prefixes", "PrefixesForSpears") as int[] ?? [],
        PrefixCategory.Accessory => Legacy("Prefixes", "PrefixesForAccessories") as int[] ?? [],
        _ => [],
    };

    private static void Report(ModPrefix prefix, string message)
    {
        if (!Reported.Add(prefix.Name.GetHashCode())) return;
        LoaderHost.Current.Log.Warn($"[prefix] {prefix.Name}: {message}");
    }

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
