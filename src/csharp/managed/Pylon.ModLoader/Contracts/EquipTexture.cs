using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>一张已注册的穿戴贴图（盔甲/翅膀/饰品）；每个钩子默认转给主人 ModItem，子类只改自己要的。</summary>
public class EquipTexture
{
    public string Texture { get; internal set; } = "";

    public string Name { get; internal set; } = "";

    public int Type { get; internal set; }

    public int Slot { get; internal set; } = -1;

    public ModItem Item { get; internal set; } = null!;

    public virtual void FrameEffects(Player player, int type)
    {
        if (Item != null) Item.EquipFrameEffects(player, (EquipType)type);
    }

    public virtual bool IsVanitySet(int head, int body, int legs) =>
        Item != null && Item.IsVanitySet(head, body, legs);

    public virtual void PreUpdateVanitySet(Player player)
    {
        if (Item != null) Item.PreUpdateVanitySet(player);
    }

    public virtual void UpdateVanitySet(Player player)
    {
        if (Item != null) Item.UpdateVanitySet(player);
    }

    public virtual void DrawArmorColor(Player drawPlayer, float shadow, ref Color color, ref int glowMask, ref Color glowColor)
    {
        if (Item != null) Item.DrawArmorColor(drawPlayer, shadow, ref color, ref glowMask, ref glowColor);
    }

    public virtual void ArmorArmGlowMask(Player drawPlayer, float shadow, ref int glowMask, ref Color glowColor)
    {
        if (Item != null) Item.ArmorArmGlowMask(drawPlayer, shadow, ref glowMask, ref glowColor);
    }

    public virtual void ArmorSetShadows(Player player)
    {
        if (Item != null) Item.ArmorSetShadows(player);
    }

    public virtual void SetMatch(bool male, ref int equipSlot, ref bool robes)
    {
        if (Item != null) Item.SetMatch(male, ref equipSlot, ref robes);
    }

    public virtual void VerticalWingSpeeds(Player player, ref float ascentWhenFalling, ref float ascentWhenRising,
                                           ref float maxCanAscendMultiplier, ref float maxAscentMultiplier, ref float constantAscend)
    {
        if (Item != null) Item.VerticalWingSpeeds(player, ref ascentWhenFalling, ref ascentWhenRising,
                                                  ref maxCanAscendMultiplier, ref maxAscentMultiplier, ref constantAscend);
    }

    public virtual void HorizontalWingSpeeds(Player player, ref float speed, ref float acceleration)
    {
        if (Item != null) Item.HorizontalWingSpeeds(player, ref speed, ref acceleration);
    }

    public virtual bool WingUpdate(Player player, bool inUse) => Item != null && Item.WingUpdate(player, inUse);
}
