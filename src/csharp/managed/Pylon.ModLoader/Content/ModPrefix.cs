using Terraria;

namespace Pylon.ModLoader;

/// <summary>自定义前缀（修饰语）：一个类 = 一个前缀，Type 就是 item.prefix 存的那个号。</summary>
public abstract class ModPrefix : ModType
{
    // 前缀分类的值照抄 tModLoader：决定哪些物品能拿这个前缀（游戏里没有 PrefixCategory 枚举）。
    /// <summary>近战（剑锤斧镐）：前缀会动体积。</summary>
    public const int CategoryMelee = 0;

    /// <summary>远程（枪弓）：前缀会动射速。</summary>
    public const int CategoryRanged = 1;

    /// <summary>魔法：前缀会动魔力消耗。</summary>
    public const int CategoryMagic = 2;

    /// <summary>召唤。</summary>
    public const int CategorySummon = 3;

    /// <summary>所有武器通用的那几个。</summary>
    public const int CategoryAnyWeapon = 4;

    /// <summary>饰品。</summary>
    public const int CategoryAccessory = 5;

    /// <summary>只有 CanRoll / 物品的 ChoosePrefix 放行时才出现（默认）。</summary>
    public const int CategoryCustom = 6;

    /// <summary>显示名（空 = 用 PrefixName.&lt;类名&gt; 的本地化键）。</summary>
    public virtual string DisplayName { get; set; } = "";

    /// <summary>Mod 名/类名；Mod 还没挂上时是 '?'。</summary>
    public virtual string FullName => (Mod != null ? Mod.Name : "?") + "/" + Name;

    /// <summary>哪些物品能拿这个前缀（默认 Custom：只在 CanRoll 或物品的 ChoosePrefix 放行的地方出现）。</summary>
    public virtual int Category => CategoryCustom;

    /// <summary>只放共享数值，不要碰游戏对象（游戏表已扩容，PrefixID.Sets 可直接写）。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>掷前缀时的权重；游戏自己的前缀权重都是 1。</summary>
    public virtual int RollChance(Item item) => 1;

    /// <summary>这个前缀能不能出现在这个物品上（默认按 RollChance &gt; 0）。</summary>
    public virtual bool CanRoll(Item item) => RollChance(item) > 0;

    /// <summary>数值：六个倍率（伤害/击退/攻速/体积/射速/魔力）+ 暴击/召唤伤害/护甲穿透三个加成；改 Ref 的 Value。</summary>
    public virtual void SetStats(ref float damageMult, ref float knockbackMult, ref float useTimeMult,
                                 ref float scaleMult, ref float shootSpeedMult, ref float manaMult,
                                 ref int critBonus, ref int tagDamage, ref int armorPenetration) { }

    /// <summary>false：这个前缀在这个物品上一个数值都不变，游戏就不让它装上。</summary>
    public virtual bool AllStatChangesHaveEffectOn(Item item) => true;

    /// <summary>SetStats 的数值之后：这个前缀还改物品的什么。</summary>
    public virtual void Apply(Item item) { }

    /// <summary>价格倍率（可写引用）；稀有度跟着它走。</summary>
    public virtual void ModifyValue(ref float valueMult) { }

    /// <summary>每帧，这个前缀的饰品装备着时。</summary>
    public virtual void ApplyAccessoryEffects(Player player) { }
}

// 未移植 SetStats 的单参数变体：JS 方便写法（{ damage, knockBack, ... }），C# 里没有对应物。
// 未移植 GetTooltipLines：TooltipLine 已有，但 ContentKind 还没有 Prefix，等前缀管线定形后接。
// 未移植 register / isModType / getModPrefix / getTypeByName：注册走自动发现，后三个读 PrefixLoader 注册表。
