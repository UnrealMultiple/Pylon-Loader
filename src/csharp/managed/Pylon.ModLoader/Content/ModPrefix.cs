using Pylon.ModLoader.Pipelines;
using Terraria;

namespace Pylon.ModLoader.Content;
/// <summary>自定义前缀（修饰语）：一个类 = 一个前缀，Type 就是 item.prefix 存的那个号；Category 决定哪些物品能拿它。</summary>
public abstract class ModPrefix : ModType
{
    public virtual string DisplayName { get; set; } = "";

    public virtual string LocalizationCategory => "Prefixes";

    public virtual PrefixCategory Category => PrefixCategory.Custom;

    public override void SetStaticDefaults() { }

    public virtual float RollChance(Item item) => 1f;

    public virtual bool CanRoll(Item item) => true;

    public virtual void SetStats(ref float damageMult, ref float knockbackMult, ref float useTimeMult,
                                 ref float scaleMult, ref float shootSpeedMult, ref float manaMult,
                                 ref int critBonus)
    { }

    public virtual bool AllStatChangesHaveEffectOn(Item item) => true;

    public virtual void Apply(Item item) { }

    public virtual void ModifyValue(ref float valueMult) { }

    public virtual void ApplyAccessoryEffects(Player player) { }

    public virtual IEnumerable<TooltipLine>? GetTooltipLines(Item item) => null;
}
