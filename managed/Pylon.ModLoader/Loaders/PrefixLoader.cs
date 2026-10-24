// 层：Mod 框架 / Loaders —— PrefixLoader。
//
// 手机版 Item.Prefix 与 PC 版相同，要紧的片段拆成了方法（GetRollablePrefixes / RollAPrefix /
// TryGetPrefixStatMultipliersForItem），钩子挂在这些方法上，其余由游戏做；宝箱按物品样例掷，走同一批钩子。
// 表只要把 Lang.prefix 与 ReducedNaturalChance 扩长；Item.prefix 是 byte，mod 前缀号不能超过 255。
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;
using Terraria.GameContent.Prefixes;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Utilities;

namespace Pylon.ModLoader
{
    /// <summary>ModPrefix 的占号、表扩容、掷前缀/数值/应用钩子。</summary>
    internal static class PrefixLoader
    {
        // IsWeaponSubCategory 用到的分类值（PrefixCategory 是本版游戏没有的枚举，值照 tModLoader）。
        internal const int CategoryMelee = 0, CategoryRanged = 1, CategoryMagic = 2, CategorySummon = 3,
                             CategoryAnyWeapon = 4, CategoryAccessory = 5, CategoryCustom = 6;

        // 注册表
        private static readonly List<ModPrefix> ByType = new();                       // 下标 = Type - BaseCount()
        private static readonly Dictionary<int, List<ModPrefix>> ByCategory = new();   // PrefixCategory → 前缀
        private static readonly Dictionary<int, List<int>> CategoriesByType = new();   // 物品类型 → 分类（按类型缓存）
        private static readonly Dictionary<int, Name> Names = new();                   // 前缀类型 → 名字的键与文本
        private static readonly List<ModPrefix> NoPrefixes = new();
        private static int _vanilla = -1;                                              // 原版前缀数（只读一次）

        private sealed class Name
        {
            public string Key;
            public string Text;
        }

        private static Harmony _harmony;
        private static ModLogger _logger;
        private static bool _rolling;        // GetRollablePrefixes / CanRollPrefix / RollAPrefix / 数值
        private static bool _apply;          // Item.Prefix
        private static bool _accessories;    // Player.GrantPrefixBenefits
        private static readonly Dictionary<string, bool> HookWanted = new(StringComparer.Ordinal);   // 谁覆写了哪个钩子

        // ===================== 注册表 =====================

        internal static int BaseCount()
        {
            if (_vanilla < 0) _vanilla = PrefixID.Count;
            return _vanilla;
        }

        internal static int PrefixCount => BaseCount() + ByType.Count;

        internal static ModPrefix Of(int type)
        {
            var index = type - BaseCount();
            return index >= 0 && index < ByType.Count ? ByType[index] : null;
        }

        private static List<ModPrefix> InCategory(int category) =>
            ByCategory.TryGetValue(category, out var list) ? list : NoPrefixes;

        // ===================== 注册（ModContent 占号后立刻） =====================

        /// <summary>Mod.cs 早阶段调：只记 harmony/logger，钩子按注册内容的需要装。</summary>
        internal static void Install(Harmony harmony, ModLogger logger)
        {
            _harmony ??= harmony;
            _logger ??= logger;
        }

        // Register：分类查表、名字进本地化、按覆着装钩子。
        internal static void Register(ModPrefix prefix)
        {
            if (prefix == null) return;
            _harmony ??= prefix.Mod != null ? prefix.Mod.Harmony : null;
            _logger ??= prefix.Mod != null ? prefix.Mod.Logger : null;

            var category = prefix.Category;
            if (category < CategoryMelee || category > CategoryCustom)
            {
                // 非法分类退成 Custom + 报错：占号一次遍历做完，抛出去会带走同一批的其它内容。
                _logger?.Error($"{prefix.Name}.Category: 要用 PrefixCategory（0..6），收到 {category}；按 Custom 处理");
                category = CategoryCustom;
            }

            ByType.Add(prefix);
            if (!ByCategory.TryGetValue(category, out var list))
            {
                list = new List<ModPrefix>();
                ByCategory[category] = list;
            }
            list.Add(prefix);

            // 名字：DisplayName，否则类名；⚠ 注册期只记账、不写语言表（游戏还没 SetLanguage，写进去会让
            // ProcessCopyCommandsInTexts 空引用、卡在启动画面），真正写进表在内容就绪后的 Tables()。
            var text = string.IsNullOrEmpty(prefix.DisplayName) ? prefix.Name : prefix.DisplayName;
            var key = "Mods." + (prefix.Mod != null ? prefix.Mod.Name : "Mod") + ".Prefixes." + prefix.Name + ".DisplayName";
            Names[prefix.Type] = new Name { Key = key, Text = text };

            // 掷前缀的两组钩子：重铸 / 宝箱 / 世界生成都从这几个方法进。
            WantRolling();
            if (prefix.Plan != null && prefix.Plan.Has("Apply")) WantApply();
            if (prefix.Plan != null && prefix.Plan.Has("ApplyAccessoryEffects")) HookAccessories();

            // mod 前缀的号必须留在 byte 里（Item.prefix 是 byte）。
            if (prefix.Type > 255)
                _logger?.Error($"{prefix.Name} 的前缀号 {prefix.Type} 超过 255（item.prefix 是 byte），存档会错位");
        }

        // ===================== 内容就绪：表 + 名字 =====================

        internal static void Setup(ModLogger logger)
        {
            if (ByType.Count == 0) return;
            _logger ??= logger;

            Tables();
            MarkPrefixableItems();

            foreach (var prefix in ByType)
                Safe.Run(prefix.Name + ".SetStaticDefaults", prefix.SetStaticDefaults);

            // 换语言会重跑 Lang.InitializeLegacyLocalization（Lang.prefix 重建、全 Empty），之后要填回名字。
            if (_harmony != null)
            {
                Hooks.Once("prefix.tables", () =>
                {
                    var target = GameHooks.Find(typeof(Lang), "InitializeLegacyLocalization", Type.EmptyTypes);
                    if (target == null)
                    {
                        _logger?.Warn("[prefix] 找不到 Lang.InitializeLegacyLocalization，换语言后前缀名可能丢失");
                        return;
                    }
                    GameHooks.Patch(_harmony, _logger, typeof(PrefixLoader), target, postfixHandler: nameof(AfterLegacyLocalization));
                });
            }

            _logger?.Info($"[prefix] 前缀安装：{ByType.Count} 个（类型 {BaseCount()}..{PrefixCount - 1}）");
        }

        /// <summary>mod 物品在 CanGetPrefixes 里是扩表时按 [0] 填的 false，会连 Item.Prefix 都进不去；按规则补 true：有伤害、不是弹药/消耗品，或是可加前缀的饰品。</summary>
        private static void MarkPrefixableItems()
        {
            var canGet = ItemID.Sets.CanGetPrefixes;
            if (canGet == null) { _logger?.Warn("[prefix] 没有 ItemID.Sets.CanGetPrefixes，mod 物品前缀不可用"); return; }

            var marked = 0;
            foreach (var item in ModContent.GetContent<ModItem>())
            {
                var type = item.Type;
                if (type <= 0 || type >= canGet.Length)
                {
                    _logger?.Warn($"[prefix] {item.Name}: type={type} 超出 CanGetPrefixes 长度 {canGet.Length}，跳过");
                    continue;
                }
                if (canGet[type]) { marked++; continue; }
                var prefixable = IsPrefixable(item);
                _logger?.Info($"[prefix] {item.Name}: type={type} 可加前缀={prefixable}");
                if (!prefixable) continue;
                canGet[type] = true;
                marked++;
            }
            _logger?.Info($"[prefix] mod 物品可加前缀：{marked} 件（CanGetPrefixes 长度 {canGet.Length}）");
        }

        /// <summary>该 mod 物品能不能拿前缀（武器，或可加前缀的饰品）。</summary>
        private static bool IsPrefixable(ModItem item)
        {
            try
            {
                var sample = new Item();
                sample.SetDefaults(item.Type, null);
                if (sample.accessory && sample.IsAPrefixableAccessory()) return true;
                return sample.damage > 0 && sample.ammo == 0 && !sample.consumable;
            }
            catch (Exception e)
            {
                Safe.Report(item.Name + ".IsPrefixable", e);
                return false;
            }
        }

        private static void AfterLegacyLocalization() => Safe.Run("PrefixLoader: 名字", Tables);

        // #Tables：两张表跟着 Count 长；名字按 key 从语言器取（换语言文本跟着变），不塞写死的 LocalizedText。
        private static void Tables()
        {
            var total = PrefixCount;

            var names = Lang.prefix;
            if (names == null || names.Length < total) Lang.prefix = Grow(names, total);

            var reduced = PrefixID.Sets.ReducedNaturalChance;
            if (reduced == null || reduced.Length < total) PrefixID.Sets.ReducedNaturalChance = Grow(reduced, total);

            if (Lang.prefix == null) return;
            foreach (var pair in Names)
            {
                var type = pair.Key;
                if (type < 0 || type >= Lang.prefix.Length) continue;
                var entry = pair.Value;
                LocalizationKeys.Register(entry.Key, new LocalizedText(entry.Key, entry.Text));
                Lang.prefix[type] = Language.GetText(entry.Key) ?? new LocalizedText(entry.Key, entry.Text);
            }
        }

        /// <summary>托管数组一次赋值（interop 自带隐式转换）；绝不能对 Il2CppStructArray 逐元素写。</summary>
        private static LocalizedText[] Grow(LocalizedText[] old, int length)
        {
            var grown = new LocalizedText[length];
            if (old != null) for (var i = 0; i < old.Length && i < length; i++) grown[i] = old[i];
            return grown;
        }

        private static bool[] Grow(bool[] old, int length)
        {
            var grown = new bool[length];
            if (old != null) for (var i = 0; i < old.Length && i < length; i++) grown[i] = old[i];
            return grown;
        }

        // ===================== 哪些物品能拿哪些前缀 =====================

        private static List<int> Categories(Item item)
        {
            var type = item.type;
            if (CategoriesByType.TryGetValue(type, out var cached)) return cached;

            var mod = ModContent.Find(ContentKind.Item, type) as ModItem;
            // 武器判定：有伤害、不是弹药、不是消耗品；手机版 maxStack 全是 9999 不能用来判，
            // mod 物品在 ItemID.Sets 的槽是扩表时按 [0] 填的 false，不能当唯一依据。
            var weapon = mod != null && CanBeModWeapon(item);
            bool Ask(string method) => weapon && AskModItem(mod, method, item);

            var list = new List<int>(5);
            if (LegacySet("SwordsHammersAxesPicks", type) || Ask("MeleePrefix")) list.Add(CategoryMelee);
            if (LegacySet("GunsBows", type) || Ask("RangedPrefix")) list.Add(CategoryRanged);
            if (LegacySet("Magic", type) || Ask("MagicPrefix")) list.Add(CategoryMagic);
            if (LegacySet("Summon", type) || Ask("SummonPrefix")) list.Add(CategorySummon);
            if (LegacySet("SpearsMacesChainsawsDrillsPunchCannon", type) || LegacySet("BoomerangsChakrams", type) ||
                LegacySet("ItemsThatCanHaveLegendary2", type) || Ask("WeaponPrefix") || list.Count > 0)
                list.Add(CategoryAnyWeapon);
            if (IsPrefixableAccessory(item)) list.Add(CategoryAccessory);

            if (type > 0) CategoriesByType[type] = list;
            return list;
        }

        /// <summary>ModItem 的 MeleePrefix/RangedPrefix/…：直接虚调用（基类给的是默认值），不用反射。</summary>
        private static bool AskModItem(ModItem mod, string method, Item item)
        {
            try
            {
                switch (method)
                {
                    case "MeleePrefix": return mod.MeleePrefix(item);
                    case "RangedPrefix": return mod.RangedPrefix(item);
                    case "MagicPrefix": return mod.MagicPrefix(item);
                    case "SummonPrefix": return mod.SummonPrefix(item);
                    case "WeaponPrefix": return mod.WeaponPrefix(item);
                    default: return false;
                }
            }
            catch (Exception e)
            {
                Safe.Report(mod.Name + "." + method, e);
                return false;
            }
        }

        // #CanBeModWeapon
        private static bool CanBeModWeapon(Item item)
        {
            try
            {
                if (item.damage <= 0 || item.ammo != 0 || item.consumable) return false;
                var set = ItemID.Sets.CanGetPrefixes;
                if (set == null || item.type < 0 || item.type >= set.Length) return true;   // mod 槽：默认可以
                return set[item.type];
            }
            catch { return false; }
        }

        // VanillaPrefixes
        private static int[] VanillaPrefixes(int category)
        {
            switch (category)
            {
                case CategoryMelee: return LegacyPrefixes("PrefixesForSwords");
                case CategoryRanged: return LegacyPrefixes("PrefixesForGunsBows");
                case CategoryMagic: return LegacyPrefixes("PrefixesForMagic");
                case CategorySummon: return LegacyPrefixes("PrefixesForSummons");
                case CategoryAnyWeapon: return LegacyPrefixes("PrefixesForSpears");
                case CategoryAccessory: return LegacyPrefixes("PrefixesForAccessories");
                default: return null;
            }
        }

        // PrefixLegacy 的两个嵌套类在 interop 里是 internal：反射取，结果缓存。
        private static readonly Dictionary<string, Type> LegacyTypes = new(StringComparer.Ordinal);

        private static Type LegacyType(string nested)
        {
            if (LegacyTypes.TryGetValue(nested, out var cached)) return cached;

            var outer = typeof(PrefixLegacy);
            Type found = null;
            try { found = outer.GetNestedType(nested, BindingFlags.Public | BindingFlags.NonPublic); }
            catch { /* fall through */ }

            if (found == null) _logger?.Warn($"[prefix] 找不到 PrefixLegacy.{nested} 表（前缀分类会缺少原版条目）");

            LegacyTypes[nested] = found;
            return found;
        }

        /// <summary>读一张 PrefixLegacy 表：字段先试再试属性（interop 偶尔把静态字段生成成属性），都没有就把成员打进日志。</summary>
        private static object ReadLegacy(string nested, string name)
        {
            var key = nested + "." + name;
            if (LegacyValues.TryGetValue(key, out var cached)) return cached;

            object value = null;
            try
            {
                var type = LegacyType(nested);
                if (type != null)
                {
                    var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (field != null) value = field.GetValue(null);
                    else
                    {
                        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        if (property != null) value = property.GetValue(null);
                    }
                    if (value == null) DumpLegacyMembers(nested, type);
                }
            }
            catch (Exception e) { _logger?.Warn($"[prefix] 读 PrefixLegacy.{key} 失败：{e.Message}"); }

            LegacyValues[key] = value;
            return value;
        }

        private static readonly Dictionary<string, object> LegacyValues = new(StringComparer.Ordinal);
        private static readonly HashSet<string> DumpedLegacy = new(StringComparer.Ordinal);

        /// <summary>一次性把表里真实的成员名记下来（名字对不上时唯一能自证的线索）。</summary>
        private static void DumpLegacyMembers(string nested, Type type)
        {
            if (!DumpedLegacy.Add(nested)) return;
            try
            {
                var names = new List<string>();
                foreach (var m in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (m.MemberType is MemberTypes.Field or MemberTypes.Property) names.Add(m.Name);
                }
                _logger?.Warn($"[prefix] PrefixLegacy.{nested} → {type.FullName}，成员：{string.Join(",", names)}");
            }
            catch (Exception e) { _logger?.Warn("[prefix] 列出成员失败：" + e.Message); }
        }

        private static bool LegacySet(string field, int type)
        {
            try
            {
                if (ReadLegacy("ItemSets", field) is not Il2CppStructArray<bool> set) return false;
                return type >= 0 && type < set.Length && set[type];
            }
            catch { return false; }
        }

        private static int[] LegacyPrefixes(string field)
        {
            try
            {
                if (ReadLegacy("Prefixes", field) is not Il2CppStructArray<int> list) return null;
                var result = new int[list.Length];
                for (var i = 0; i < list.Length; i++) result[i] = list[i];
                return result;
            }
            catch { return null; }
        }

        private static bool IsPrefixableAccessory(Item item)
        {
            try { return item.IsAPrefixableAccessory(); }
            catch { return false; }
        }

        // ===================== 掷前缀：列表 =====================

        private static void WantRolling()
        {
            if (_harmony == null || _rolling) return;
            _rolling = true;
            Hooks.Once("prefix.rolling", HookRolling);
        }

        private static void HookRolling()
        {
            GameHooks.Patch(_harmony, _logger, typeof(PrefixLoader), typeof(Item), "GetRollablePrefixes", Type.EmptyTypes,
                null, nameof(RollablePostfix));
            GameHooks.Patch(_harmony, _logger, typeof(PrefixLoader), typeof(Item), "CanRollPrefix", new[] { typeof(int) },
                nameof(CanRollPrefixPrefix), null);
            GameHooks.Patch(_harmony, _logger, typeof(PrefixLoader), typeof(Item), "RollAPrefix",
                new[] { typeof(UnifiedRandom), typeof(int).MakeByRefType() }, nameof(RollPrefix), null);
            GameHooks.Patch(_harmony, _logger, typeof(PrefixLoader), typeof(Item), "TryGetPrefixStatMultipliersForItem",
                new[]
                {
                    typeof(int), typeof(float).MakeByRefType(), typeof(float).MakeByRefType(),
                    typeof(float).MakeByRefType(), typeof(float).MakeByRefType(), typeof(float).MakeByRefType(),
                    typeof(float).MakeByRefType(), typeof(int).MakeByRefType(), typeof(int).MakeByRefType(),
                    typeof(int).MakeByRefType(), typeof(float).MakeByRefType(),
                }, nameof(StatsPrefix), null);
        }

        // #Rollable：游戏的列表 + mod 前缀并去重；有人覆写 AllowPrefix 才逐条问。
        private static void RollablePostfix(Item __instance, ref Il2CppStructArray<int> __result)
        {
            try
            {
                var item = __instance;
                var categories = Categories(item);
                var modItem = ModContent.Find(ContentKind.Item, item.type) != null;
                var filter = HasHook("AllowPrefix");

                var extra = new List<int>();
                foreach (var category in categories)
                {
                    foreach (var prefix in InCategory(category))
                    {
                        if (Safe.Run(prefix.Name + ".CanRoll", () => prefix.CanRoll(item), false)) extra.Add(prefix.Type);
                    }
                }
                if (!modItem && extra.Count == 0 && !filter) return;

                var output = new List<int>();
                var seen = new HashSet<int>();
                void Add(int id)
                {
                    if (id <= 0 || !seen.Add(id)) return;
                    if (filter && !AllowPrefix(item, id)) return;
                    output.Add(id);
                }

                if (__result != null) for (var i = 0; i < __result.Length; i++) Add(__result[i]);
                if (modItem)
                {
                    foreach (var category in categories)
                    {
                        var vanilla = VanillaPrefixes(category);
                        if (vanilla == null) continue;
                        foreach (var id in vanilla) Add(id);
                    }
                }
                foreach (var id in extra) Add(id);

                __result = output.Count > 0 ? output.ToArray() : null;
            }
            catch (Exception e) { Safe.Report("GetRollablePrefixes", e); }
        }

        // CanRollPrefix：Custom 不进普通掷，但在这里认。
        private static bool CanRollPrefixPrefix(Item __instance, int prefix, ref bool __result)
        {
            try
            {
                var mod = Of(prefix);
                if (mod == null || mod.Category != CategoryCustom) return true;
                __result = AllowPrefix(__instance, prefix) &&
                           Safe.Run(mod.Name + ".CanRoll", () => mod.CanRoll(__instance), false);
                return false;
            }
            catch (Exception e)
            {
                Safe.Report("CanRollPrefix", e);
                return true;
            }
        }

        // RollAPrefix：ChoosePrefix 优先；有 mod 前缀就按 RollChance 加权抽，全是原版时交回游戏。
        private static bool RollPrefix(Item __instance, UnifiedRandom random, ref int rolledPrefix, ref bool __result)
        {
            try
            {
                var item = __instance;

                var forced = ChoosePrefix(item, random);
                if (forced > 0 && item.CanRollPrefix(forced))
                {
                    rolledPrefix = forced;
                    __result = true;
                    return false;
                }

                var list = item.GetRollablePrefixes();
                if (list == null || list.Length == 0)
                {
                    __result = false;
                    return false;
                }

                var ids = new List<int>(list.Length);
                var weights = new List<int>(list.Length);
                var total = 0;
                var anyMod = false;
                for (var i = 0; i < list.Length; i++)
                {
                    var id = list[i];
                    var mod = Of(id);
                    var weight = mod == null ? 1 : Math.Max(0, Safe.Run(mod.Name + ".RollChance", () => mod.RollChance(item), 0));
                    if (mod != null) anyMod = true;
                    if (weight <= 0) continue;
                    ids.Add(id);
                    weights.Add(weight);
                    total += weight;
                }
                if (!anyMod) return true;          // 交回游戏掷
                if (ids.Count == 0)
                {
                    __result = false;
                    return false;
                }

                var roll = random.NextDouble() * total;
                var index = 0;
                while (index < ids.Count - 1 && roll >= weights[index]) roll -= weights[index++];
                rolledPrefix = ids[index];
                __result = true;
                return false;
            }
            catch (Exception e)
            {
                Safe.Report("RollAPrefix", e);
                return true;
            }
        }

        // ===================== 数值 =====================

        private struct Stats
        {
            public float Damage, Knockback, Speed, Size, ShootSpeed, Mana, Value;
            public int Crit, TagDamage, ArmorPenetration;
            public bool Ok;
        }

        private static bool StatsPrefix(Item __instance, int rolledPrefix,
            ref float dmg, ref float kb, ref float spd, ref float size, ref float shtspd, ref float mcst,
            ref int crt, ref int tagdmg, ref int arpen, ref float value, ref bool __result)
        {
            try
            {
                var prefix = Of(rolledPrefix);
                if (prefix == null) return true;

                var stats = StatsOf(prefix, __instance);
                dmg = stats.Damage;
                kb = stats.Knockback;
                spd = stats.Speed;
                size = stats.Size;
                shtspd = stats.ShootSpeed;
                mcst = stats.Mana;
                crt = stats.Crit;
                tagdmg = stats.TagDamage;
                arpen = stats.ArmorPenetration;
                value = stats.Value;
                __result = stats.Ok;
                return false;
            }
            catch (Exception e)
            {
                Safe.Report("TryGetPrefixStatMultipliersForItem", e);
                return true;
            }
        }

        // #StatsOf：复刻游戏算法，含"每个碰到的数值都必须真的在物品上变"（否则前缀装不上）。
        private static Stats StatsOf(ModPrefix prefix, Item item)
        {
            var stats = new Stats
            {
                Damage = 1f, Knockback = 1f, Speed = 1f, Size = 1f, ShootSpeed = 1f, Mana = 1f, Value = 1f,
            };

            if (!Safe.Run(prefix.Name + ".AllStatChangesHaveEffectOn",
                    () => prefix.AllStatChangesHaveEffectOn(item), false)) return stats;

            var damage = 1f;
            var knockback = 1f;
            var speed = 1f;
            var size = 1f;
            var shootSpeed = 1f;
            var mana = 1f;
            var crit = 0;
            var tagDamage = 0;
            var armorPenetration = 0;
            Safe.Run(prefix.Name + ".SetStats", () => prefix.SetStats(ref damage, ref knockback, ref speed, ref size,
                ref shootSpeed, ref mana, ref crit, ref tagDamage, ref armorPenetration));

            stats.Damage = Finite(damage);
            stats.Knockback = Finite(knockback);
            stats.Speed = Finite(speed);
            stats.Size = Finite(size);
            stats.ShootSpeed = Finite(shootSpeed);
            stats.Mana = Finite(mana);
            stats.Crit = crit;
            stats.TagDamage = tagDamage;
            stats.ArmorPenetration = armorPenetration;

            var valueMult = stats.Damage * (2f - stats.Speed) * (2f - stats.Mana) * stats.Size * stats.Knockback *
                            stats.ShootSpeed * (1f + stats.Crit * 0.02f) * (1f + stats.ArmorPenetration * 0.015f) *
                            (1f + stats.TagDamage * 0.03f);
            Safe.Run(prefix.Name + ".ModifyValue", () => prefix.ModifyValue(ref valueMult));
            stats.Value = Finite(valueMult);

            stats.Ok = (stats.Damage == 1f || RoundEven(item.damage * stats.Damage) != item.damage) &&
                       (stats.Speed == 1f || RoundEven(item.useAnimation * stats.Speed) != item.useAnimation) &&
                       (stats.Mana == 1f || RoundEven(item.mana * stats.Mana) != item.mana) &&
                       (stats.Knockback == 1f || item.knockBack != 0f);
            return stats;
        }

        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 1f : value;

        /// <summary>C# 的 Math.Round：.5 进偶数。</summary>
        private static int RoundEven(float value) => (int)Math.Round(value, MidpointRounding.ToEven);

        // ===================== ChoosePrefix / PrefixChance / AllowPrefix =====================

        // #ChoosePrefix：先 Global 再物品自己，第一个正数赢。
        private static int ChoosePrefix(Item item, UnifiedRandom random)
        {
            var chosen = -1;
            foreach (var global in GlobalItemRegistry.For(item, "ChoosePrefix"))
            {
                var value = Safe.Run(global.Name + ".ChoosePrefix", () => global.ChoosePrefix(item, random), -1);
                if (chosen <= 0 && value > 0) chosen = value;
            }
            if (chosen > 0) return chosen;

            var mod = ModContent.Find(ContentKind.Item, item.type) as ModItem;
            if (mod == null || mod.Plan == null || !mod.Plan.Has("ChoosePrefix")) return -1;
            var own = Safe.Run(mod.Name + ".ChoosePrefix", () => mod.ChoosePrefix(item, random), -1);
            return own > 0 ? own : -1;
        }

        // #PrefixChance：false（有人拦）压 true（有人保），null = 游戏决定。
        private static bool? PrefixChance(Item item, int prefix, UnifiedRandom random)
        {
            bool? result = null;
            void Take(bool? value)
            {
                if (value != true && value != false) return;
                result = value.Value && (result ?? true);
            }

            foreach (var global in GlobalItemRegistry.For(item, "PrefixChance"))
                Take(Safe.Run(global.Name + ".PrefixChance", () => global.PrefixChance(item, prefix, random), (bool?)null));

            var mod = ModContent.Find(ContentKind.Item, item.type) as ModItem;
            if (mod != null && mod.Plan != null && mod.Plan.Has("PrefixChance"))
                Take(Safe.Run(mod.Name + ".PrefixChance", () => mod.PrefixChance(item, prefix, random), (bool?)null));
            return result;
        }

        // AllowPrefix：物品自己先说，然后每个 Global 都要放行。
        private static bool AllowPrefix(Item item, int prefix)
        {
            var mod = ModContent.Find(ContentKind.Item, item.type) as ModItem;
            if (mod != null && mod.Plan != null && mod.Plan.Has("AllowPrefix") &&
                !Safe.Run(mod.Name + ".AllowPrefix", () => mod.AllowPrefix(item, prefix), true)) return false;

            foreach (var global in GlobalItemRegistry.For(item, "AllowPrefix"))
            {
                if (!Safe.Run(global.Name + ".AllowPrefix", () => global.AllowPrefix(item, prefix), true)) return false;
            }
            return true;
        }

        /// <summary>有没有 ModItem/GlobalItem 覆写了这个方法（第一次用到时算一次）。</summary>
        private static bool HasHook(string method)
        {
            if (HookWanted.TryGetValue(method, out var cached)) return cached;
            var found = GlobalItemRegistry.AnyWith(method);
            if (!found)
            {
                foreach (var item in ModContent.GetContent<ModItem>())
                {
                    if (item.Plan != null && item.Plan.Has(method)) { found = true; break; }
                }
            }
            HookWanted[method] = found;
            return found;
        }

        // ===================== 应用 =====================

        // #WantApply：hook 里先算 -1/-2 的 PrefixChance 再 Apply；Harmony 的 prefix 没法中途调 original，所以拆成 prefix＋postfix，这不是冗余。
        private static void WantApply()
        {
            if (_harmony == null || _apply) return;
            _apply = true;
            Hooks.Once("prefix.apply", () =>
            {
                var args = new[] { typeof(int), typeof(bool).MakeByRefType() };
                GameHooks.Patch(_harmony, _logger, typeof(PrefixLoader), typeof(Item), "Prefix", args,
                    nameof(ApplyPrefixPrefix), nameof(ApplyPrefixPostfix));
            });
        }

        private static bool ApplyPrefixPrefix(Item __instance, ref int prefixWeWant)
        {
            try
            {
                // -1（新物品/宝箱）可被 PrefixChance 拦；-2 是重铸（游戏自己掷），-3 只问"能不能"。
                if (prefixWeWant < 0 && prefixWeWant > -3 && HasHook("PrefixChance"))
                {
                    var random = WorldGen.isGeneratingOrLoadingWorld ? WorldGen.genRand : Main.rand;
                    var chance = PrefixChance(__instance, prefixWeWant, random);
                    if (chance == false) return false;
                    if (chance == true && prefixWeWant == -1) prefixWeWant = -2;
                }
                return true;
            }
            catch (Exception e)
            {
                Safe.Report("Item.Prefix", e);
                return true;
            }
        }

        private static void ApplyPrefixPostfix(Item __instance, int prefixWeWant, bool __result)
        {
            if (!__result) return;
            // -3 只问"能不能"（重铸用它探路）；prefix 0 = 什么都没装上。
            if (prefixWeWant == -3 || __instance.prefix == 0) return;

            var applied = __instance.prefix;
            var prefix = Of(applied);
            if (prefix != null) Safe.Run(prefix.Name + ".Apply", () => prefix.Apply(__instance));

            var mod = ModContent.Find(ContentKind.Item, __instance.type) as ModItem;
            if (mod != null && mod.Plan != null && mod.Plan.Has("ApplyPrefix"))
                Safe.Run(mod.Name + ".ApplyPrefix", () => mod.ApplyPrefix(__instance, applied));
            foreach (var global in GlobalItemRegistry.For(__instance, "ApplyPrefix"))
                Safe.Run(global.Name + ".ApplyPrefix", () => global.ApplyPrefix(__instance, applied));
        }

        // #HookAccessories：带 mod 前缀的饰品，每帧。
        private static void HookAccessories()
        {
            if (_harmony == null || _accessories) return;
            _accessories = true;
            Hooks.Once("prefix.accessory", () =>
            {
                var target = GameHooks.Find(typeof(Player), "GrantPrefixBenefits", new[] { typeof(Item) });
                if (target == null)
                {
                    _logger?.Warn("[prefix] 找不到 Player.GrantPrefixBenefits，饰品的 ApplyAccessoryEffects 不可用");
                    return;
                }
                GameHooks.Patch(_harmony, _logger, typeof(PrefixLoader), target, postfixHandler: nameof(AccessoryPostfix));
            });
        }

        private static void AccessoryPostfix(Player __instance, Item item)
        {
            if (item == null) return;
            var prefix = Of(item.prefix);
            if (prefix == null || prefix.Plan == null || !prefix.Plan.Has("ApplyAccessoryEffects")) return;
            Safe.Run(prefix.Name + ".ApplyAccessoryEffects", () => prefix.ApplyAccessoryEffects(__instance));
        }


    }
}
