namespace Pylon.ModLoader;

/// <summary>装备贴图的部位（与游戏的 `TextureAssets` 表一一对应）。</summary>
public enum EquipType
{
    /// <summary>头盔。</summary>
    Head = 0,

    /// <summary>胸甲。</summary>
    Body = 1,

    /// <summary>护腿。</summary>
    Legs = 2,

    /// <summary>手部（画在前面）。</summary>
    HandOn = 3,

    /// <summary>手部（画在后面）。</summary>
    HandOff = 4,

    /// <summary>背部。</summary>
    Back = 5,

    /// <summary>胸前。</summary>
    Front = 6,

    /// <summary>鞋。</summary>
    Shoes = 7,

    /// <summary>腰。</summary>
    Waist = 8,

    /// <summary>翅膀。</summary>
    Wing = 9,

    /// <summary>盾。</summary>
    Shield = 10,

    /// <summary>项链。</summary>
    Neck = 11,

    /// <summary>面部。</summary>
    Face = 12,

    /// <summary>气球。</summary>
    Balloon = 13,

    /// <summary>胡子。</summary>
    Beard = 14,
}
