using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;
using Terraria.ID;
using Terraria.Utilities;

namespace Pylon.ModLoader;

/// <summary>
/// 前缀管线：可滚列表 / 能否滚 / 滚哪个 / 数值倍率 / 应用 / 饰品效果 / 名字表。
/// 前缀是物品上的一个 byte，没有实体也没有每帧路径，所以没有默认值接管和绑定表。
/// </summary>
internal sealed class PrefixPipeline : ILoaderModule
{
    public static PrefixPipeline Instance { get; } = new();

    private readonly HashSet<int> _reported = [];

    public string Name => "prefix";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Item.GetRollablePrefixes += Rollable;
        On.Terraria.Item.CanRollPrefix += CanRoll;
        On.Terraria.Item.RollAPrefix += Roll;
        On.Terraria.Item.TryGetPrefixStatMultipliersForItem += Stats;
        On.Terraria.Item.Prefix_int_boolRef += Applied;
        On.Terraria.Player.GrantPrefixBenefits += AccessoryBenefits;
        On.Terraria.Lang.InitializeLegacyLocalization += AfterLegacyLocalization;

        Log.Info("[prefix] 已挂（可滚列表 / 能否滚 / 加权随机 / 数值 / 应用 / 饰品 / 名字）");
        return true;
    }

    #region 可滚列表

    /// <summary>合并而非替换：原版列表是基底，mod 物品再补上原版分类表里的前缀。</summary>
    private Il2CppStructArray<int> Rollable(On.Terraria.Item.orig_GetRollablePrefixes orig, Item self)
    {
        var vanilla = orig(self);

        var categories = PrefixTable.CategoriesOf(self);
        var modItem = Of(self);

        var extra = new List<int>();
        foreach (var category in categories)
        {
            foreach (var prefix in PrefixTable.InCategory(category))
            {
                if (CanRollOn(prefix, self)) extra.Add(prefix.Type);
            }
        }

        var filterModItem = modItem is not null && PrefixTable.HasHook(modItem, HookId.AllowPrefix);
        var anyGlobal = GlobalItemRegistry.AnyWith(HookId.AllowPrefix);

        // 原版物品没有任何 mod 前缀、也没人过滤时，原样返回：这条路径每件物品都会被问到。
        if (!filterModItem && !anyGlobal && extra.Count == 0 && categories.Length == 0) return vanilla;

        var result = new List<int>();
        var seen = new HashSet<int>();

        void Add(int id)
        {
            if (!seen.Add(id)) return;
            if ((filterModItem || anyGlobal) && !Allowed(self, id)) return;
            result.Add(id);
        }

        if (vanilla is not null)
        {
            for (var i = 0; i < vanilla.Length; i++) Add(vanilla[i]);
        }

        // 原版只给原版类型填了列表：mod 物品要自己补上所属分类的原版前缀。
        if (modItem is not null)
        {
            foreach (var category in categories)
            {
                foreach (var id in PrefixTable.VanillaPrefixes(category)) Add(id);
            }
        }

        foreach (var id in extra) Add(id);

        // 没有任何可选项时把原版结果原样交回去（可能本来就是空数组）。
        if (result.Count == 0) return vanilla is null ? new Il2CppStructArray<int>(0) : vanilla;

        int[] merged = [.. result];
        return merged;
    }

    private static bool CanRollOn(ModPrefix prefix, Item item)
    {
        if (!prefix.HookEnabled(HookId.CanRoll)) return prefix.RollChance(item) > 0;

        try
        {
            return prefix.CanRoll(item);
        }
        catch (Exception e)
        {
            Safe.Report("prefix." + prefix.Name + ".CanRoll", e);
            return false;
        }
    }

    private static bool Allowed(Item item, int prefix)
    {
        var allowed = true;

        foreach (var global in GlobalItemRegistry.For(item, HookId.AllowPrefix))
        {
            try
            {
                allowed &= global.AllowPrefix(item, prefix);
            }
            catch (Exception e)
            {
                Safe.Report("prefix." + global.Name + ".AllowPrefix", e);
            }
        }

        var modItem = Of(item);
        if (modItem is null || !PrefixTable.HasHook(modItem, HookId.AllowPrefix)) return allowed;

        try
        {
            return allowed & modItem.AllowPrefix(item, prefix);
        }
        catch (Exception e)
        {
            Safe.Report("prefix." + modItem.Name + ".AllowPrefix", e);
            return allowed;
        }
    }

    #endregion

    #region 掷前缀

    /// <summary>游戏自己不认识 Custom 分类的 mod 前缀：这里替它答。</summary>
    private static bool CanRoll(On.Terraria.Item.orig_CanRollPrefix orig, Item self, int prefix)
    {
        var mod = PrefixTable.Of(prefix);
        if (mod is null || mod.Category != ModPrefix.CategoryCustom) return orig(self, prefix);

        return Allowed(self, prefix) && CanRollOn(mod, self);
    }

    /// <summary>强制指定优先；列表里只要出现过 mod 前缀就整轮自己掷（权重 = RollChance）。</summary>
    private bool Roll(On.Terraria.Item.orig_RollAPrefix orig, Item self, UnifiedRandom random, ref int rolledPrefix)
    {
        try
        {
            var forced = Choose(self, random);
            if (forced > 0 && self.CanRollPrefix(forced))
            {
                rolledPrefix = forced;
                return true;
            }

            var list = self.GetRollablePrefixes();
            if (list is null || list.Length == 0) return orig(self, random, ref rolledPrefix);

            var ids = new List<int>(list.Length);
            var weights = new List<double>(list.Length);
            var total = 0d;
            var anyMod = false;

            for (var i = 0; i < list.Length; i++)
            {
                var id = list[i];
                var weight = 1d;

                if (PrefixTable.Of(id) is ModPrefix prefix)
                {
                    anyMod = true;
                    if (!CanRollOn(prefix, self)) continue;

                    weight = RollChance(prefix, self);
                }

                if (!(weight > 0)) continue;

                ids.Add(id);
                weights.Add(weight);
                total += weight;
            }

            if (!anyMod) return orig(self, random, ref rolledPrefix);
            if (ids.Count == 0) return false;

            var roll = random.NextDouble() * total;
            var index = 0;
            while (index < ids.Count - 1 && roll >= weights[index]) roll -= weights[index++];

            rolledPrefix = ids[index];
            return true;
        }
        catch (Exception e)
        {
            Fail("roll", e);
            return orig(self, random, ref rolledPrefix);
        }
    }

    private static double RollChance(ModPrefix prefix, Item item)
    {
        if (!prefix.HookEnabled(HookId.RollChance)) return prefix.CanRoll(item) ? 1d : 0d;

        try
        {
            return prefix.RollChance(item);
        }
        catch (Exception e)
        {
            Safe.Report("prefix." + prefix.Name + ".RollChance", e);
            return 0d;
        }
    }

    /// <summary>作者点名的前缀：Global 先问，ModItem 后问，第一个正数生效。</summary>
    private static int Choose(Item item, UnifiedRandom random)
    {
        var modItem = Of(item);
        if (modItem is null && !GlobalItemRegistry.AnyWith(HookId.ChoosePrefix)) return -1;

        foreach (var global in GlobalItemRegistry.For(item, HookId.ChoosePrefix))
        {
            try
            {
                var picked = global.ChoosePrefix(item, random);
                if (picked > 0) return picked;
            }
            catch (Exception e)
            {
                Safe.Report("prefix." + global.Name + ".ChoosePrefix", e);
            }
        }

        if (modItem is null || !PrefixTable.HasHook(modItem, HookId.ChoosePrefix)) return -1;

        try
        {
            return modItem.ChoosePrefix(item, random);
        }
        catch (Exception e)
        {
            Safe.Report("prefix." + modItem.Name + ".ChoosePrefix", e);
            return -1;
        }
    }

    #endregion

    #region 数值

    /// <summary>mod 前缀不调原版：先把 out 复位成"无变化"，再交给作者写。</summary>
    private bool Stats(On.Terraria.Item.orig_TryGetPrefixStatMultipliersForItem orig, Item self, int rolledPrefix,
                       ref float dmg, ref float kb, ref float spd, ref float size, ref float shtspd, ref float mcst,
                       ref int crt, ref int tagdmg, ref int arpen, ref float value)
    {
        var prefix = PrefixTable.Of(rolledPrefix);
        if (prefix is null) return orig(self, rolledPrefix, ref dmg, ref kb, ref spd, ref size, ref shtspd, ref mcst,
            ref crt, ref tagdmg, ref arpen, ref value);

        dmg = kb = spd = size = shtspd = mcst = value = 1f;
        crt = tagdmg = arpen = 0;

        try
        {
            if (!prefix.AllStatChangesHaveEffectOn(self)) return false;

            var damage = 1f;
            var knockback = 1f;
            var useTime = 1f;
            var scale = 1f;
            var shootSpeed = 1f;
            var mana = 1f;
            var crit = 0;
            var tag = 0;
            var pierce = 0;

            prefix.SetStats(ref damage, ref knockback, ref useTime, ref scale, ref shootSpeed, ref mana, ref crit, ref tag,
                ref pierce);

            // 价格倍率必须与六个倍率一致，否则会出现"很弱的词条卖很贵"。
            var multiplier = damage * (2f - useTime) * (2f - mana) * scale * knockback * shootSpeed * (1f + crit * 0.02f);
            if (prefix.HookEnabled(HookId.ModifyValue)) prefix.ModifyValue(ref multiplier);

            dmg = damage;
            kb = knockback;
            spd = useTime;
            size = scale;
            shtspd = shootSpeed;
            mcst = mana;
            crt = crit;
            tagdmg = tag;
            arpen = pierce;
            value = multiplier;

            // 倍率被取整吃掉时这个前缀等于没效果：不该让它装上。
            return (damage == 1f || Math.Round(self.damage * damage) != self.damage) &&
                   (useTime == 1f || Math.Round(self.useAnimation * useTime) != self.useAnimation) &&
                   (mana == 1f || Math.Round(self.mana * mana) != self.mana) &&
                   (knockback == 1f || self.knockBack != 0f);
        }
        catch (Exception e)
        {
            Fail(prefix.Name, HookId.SetStats, e);
            return false;
        }
    }

    #endregion

    #region 应用

    private bool Applied(On.Terraria.Item.orig_Prefix_int_boolRef orig, Item self, int prefixWeWant, ref bool rolledPrefixIsTopTier)
    {
        var argument = prefixWeWant;

        try
        {
            // -1 / -2 是"随便给一个"：作者能不能否决、能不能强行要顶级，都在这两个上。
            if (prefixWeWant < 0 && prefixWeWant > -3)
            {
                var chance = PrefixChance(self, prefixWeWant, Main.rand);
                if (chance == false) return false;
                if (chance == true && prefixWeWant == -1) argument = -2;
            }
        }
        catch (Exception e)
        {
            Fail("chance", e);
        }

        var applied = orig(self, argument, ref rolledPrefixIsTopTier);
        if (!applied || prefixWeWant == -3) return applied;

        var prefix = PrefixTable.Of(self.prefix);
        if (prefix is not null && prefix.HookEnabled(HookId.Apply))
        {
            try
            {
                prefix.Apply(self);
            }
            catch (Exception e)
            {
                Fail(prefix.Name, HookId.Apply, e);
            }
        }

        var modItem = Of(self);
        if (modItem is not null && PrefixTable.HasHook(modItem, HookId.ApplyPrefix))
        {
            try
            {
                modItem.ApplyPrefix(self, self.prefix);
            }
            catch (Exception e)
            {
                Fail(modItem.Name, HookId.ApplyPrefix, e);
            }
        }

        foreach (var global in GlobalItemRegistry.For(self, HookId.ApplyPrefix))
        {
            try
            {
                global.ApplyPrefix(self, self.prefix);
            }
            catch (Exception e)
            {
                Fail(global.Name, HookId.ApplyPrefix, e);
            }
        }

        return applied;
    }

    /// <summary>null = 没人表态；false = 这次不给前缀；true = 要给（-1 时升级成 -2，即必出顶级）。</summary>
    private static bool? PrefixChance(Item item, int prefix, UnifiedRandom random)
    {
        bool? verdict = null;

        void Take(bool? answer)
        {
            if (answer.HasValue) verdict = answer.Value && (verdict ?? true);
        }

        foreach (var global in GlobalItemRegistry.For(item, HookId.PrefixChance))
        {
            try
            {
                Take(global.PrefixChance(item, prefix, random));
            }
            catch (Exception e)
            {
                Safe.Report("prefix." + global.Name + ".PrefixChance", e);
            }
        }

        var modItem = Of(item);
        if (modItem is null || !PrefixTable.HasHook(modItem, HookId.PrefixChance)) return verdict;

        try
        {
            Take(modItem.PrefixChance(item, prefix, random));
        }
        catch (Exception e)
        {
            Safe.Report("prefix." + modItem.Name + ".PrefixChance", e);
        }

        return verdict;
    }

    private void AccessoryBenefits(On.Terraria.Player.orig_GrantPrefixBenefits orig, Player self, Item item)
    {
        orig(self, item);

        var prefix = PrefixTable.Of(item.prefix);
        if (prefix is null || !prefix.HookEnabled(HookId.ApplyAccessoryEffects)) return;

        try
        {
            prefix.ApplyAccessoryEffects(self);
        }
        catch (Exception e)
        {
            Fail(prefix.Name, HookId.ApplyAccessoryEffects, e);
        }
    }

    #endregion

    private void AfterLegacyLocalization(On.Terraria.Lang.orig_InitializeLegacyLocalization orig)
    {
        orig();
        PrefixTable.WriteNames();
    }

    private static ModItem? Of(Item item) => ModContent.Find(ContentKind.Item, item.type) as ModItem;

    /// <summary>同一 (名字, 钩子) 只记一条：前缀判定在重铸面板上会被连续调用。</summary>
    private void Fail(string name, HookId hook, Exception exception)
    {
        if (!_reported.Add(HashCode.Combine(name.GetHashCode(), hook))) return;
        ContentErrors.Report($"[prefix] {hook} 异常：{Safe.Describe(exception)}");
    }

    /// <summary>管线自己这一层的失败（没有对应的作者钩子可归）。</summary>
    private void Fail(string key, Exception exception)
    {
        if (!_reported.Add(key.GetHashCode())) return;
        ContentErrors.Report($"[prefix] {key} 异常：{Safe.Describe(exception)}");
    }
}
