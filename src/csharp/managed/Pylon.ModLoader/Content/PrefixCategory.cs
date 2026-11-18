namespace Pylon.ModLoader.Content;
/// <summary>前缀分类：决定哪些物品能拿这个前缀（Custom 只在 CanRoll / 物品的 ChoosePrefix 放行时出现）。</summary>
public enum PrefixCategory
{
    AnyWeapon,
    Melee,
    Ranged,
    Magic,
    Summon,
    Accessory,
    Custom,
}
