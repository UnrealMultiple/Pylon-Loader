// 层：Mod 框架 / Tooltips —— tooltip 行名常量
// 行名同 tModLoader（'ItemName'、'Damage'、'Tooltip0'…），ModifyTooltips 按名字找行，不用裸文字；
// 描述行一个下标：Tooltip(0) → 'Tooltip0'。Price/SpecialPrice/ReforgePrice 只在手机出现。
namespace Pylon.ModLoader.Tooltips
{
    /// <summary>tooltip 行的名字（TooltipLine.Name）。</summary>
    public static class TooltipNames
    {
        public const string ItemName = "ItemName";
        public const string Favorite = "Favorite";
        public const string FavoriteDesc = "FavoriteDesc";
        public const string NoTransfer = "NoTransfer";
        public const string Shared = "Shared";
        public const string SharedFrom = "SharedFrom";
        public const string NoSocial = "NoSocial";
        public const string Damage = "Damage";
        public const string CritChance = "CritChance";
        public const string Speed = "Speed";
        public const string Knockback = "Knockback";
        public const string FishingPower = "FishingPower";
        public const string NeedsBait = "NeedsBait";
        public const string BaitPower = "BaitPower";
        public const string Equipable = "Equipable";
        public const string WandConsumes = "WandConsumes";
        public const string Quest = "Quest";
        public const string Vanity = "Vanity";
        public const string Defense = "Defense";
        public const string PickPower = "PickPower";
        public const string AxePower = "AxePower";
        public const string HammerPower = "HammerPower";
        public const string TileBoost = "TileBoost";
        public const string HealLife = "HealLife";
        public const string HealMana = "HealMana";
        public const string UseMana = "UseMana";
        public const string Placeable = "Placeable";
        public const string Ammo = "Ammo";
        public const string Consumable = "Consumable";
        public const string Material = "Material";
        public const string Wireable = "Wireable";
        public const string Container = "Container";
        public const string WireTrigger = "WireTrigger";

        /// <summary>描述行：Tooltip(0) → 'Tooltip0'。</summary>
        public static string Tooltip(int index) => "Tooltip" + index;

        public const string WizardHatDuringAnniversary = "WizardHatDuringAnniversary";
        public const string BurningBlock = "BurningBlock";
        public const string MechSummonDuringEverything = "MechSummonDuringEverything";
        public const string MechdusaSummonNotDuringEverything = "MechdusaSummonNotDuringEverything";
        public const string EtherianManaWarning = "EtherianManaWarning";
        public const string WellFedExpert = "WellFedExpert";
        public const string BuffTime = "BuffTime";
        public const string OneDropLogo = "OneDropLogo";
        public const string Expert = "Expert";
        public const string Master = "Master";
        public const string PrefixDamage = "PrefixDamage";
        public const string PrefixSpeed = "PrefixSpeed";
        public const string PrefixCritChance = "PrefixCritChance";
        public const string PrefixUseMana = "PrefixUseMana";
        public const string PrefixSize = "PrefixSize";
        public const string PrefixShootSpeed = "PrefixShootSpeed";
        public const string PrefixKnockback = "PrefixKnockback";
        public const string PrefixArmorPenetration = "PrefixArmorPenetration";
        public const string PrefixTagDamage = "PrefixTagDamage";
        public const string PrefixAccDefense = "PrefixAccDefense";
        public const string PrefixAccMaxMana = "PrefixAccMaxMana";
        public const string PrefixAccCritChance = "PrefixAccCritChance";
        public const string PrefixAccDamage = "PrefixAccDamage";
        public const string PrefixAccMoveSpeed = "PrefixAccMoveSpeed";
        public const string PrefixAccMeleeSpeed = "PrefixAccMeleeSpeed";
        public const string SetBonus = "SetBonus";
        public const string LoadoutShareHint = "LoadoutShareHint";
        public const string JourneyResearch = "JourneyResearch";
        public const string JourneyResearchByTeammate = "JourneyResearchByTeammate";
        public const string BestiaryNotes = "BestiaryNotes";
        public const string CraftingMaterials = "CraftingMaterials";
        public const string Price = "Price";
        public const string SpecialPrice = "SpecialPrice";
        public const string ReforgePrice = "ReforgePrice";
    }
}
