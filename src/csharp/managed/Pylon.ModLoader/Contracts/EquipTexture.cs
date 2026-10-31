using Terraria;

namespace Pylon.ModLoader;

/// <summary>一张已注册的穿戴贴图（盔甲/翅膀/饰品）；每个钩子默认转给主人 ModItem，子类只改自己要的。</summary>
public class EquipTexture
{
    /// <summary>贴图文件；注册时由管线填（相对 Mod 目录的路径）。</summary>
    public string Texture { get; internal set; } = "";

    /// <summary>注册名（GetEquipSlot 查它）。</summary>
    public string Name { get; internal set; } = "";

    /// <summary>部位（EquipType：Head/Body/…）；注册时由管线填。</summary>
    public int Type { get; internal set; }

    /// <summary>穿在身上的槽位（注册时分配）。</summary>
    public int Slot { get; internal set; } = -1;

    /// <summary>主人 ModItem（注册时的模板），或 null。</summary>
    public ModItem Item { get; internal set; } = null!;

    /// <summary>穿着这张贴图时每帧；默认转给主人的 EquipFrameEffects。</summary>
    public virtual void FrameEffects(Player player, int type)
    {
        if (Item != null) Item.EquipFrameEffects(player, type);
    }

    /// <summary>时装套判定；head/body/legs 是画出来的槽位不是物品；没主人就是 false。</summary>
    public virtual bool IsVanitySet(int head, int body, int legs) =>
        Item != null && Item.IsVanitySet(head, body, legs);

    /// <summary>时装套生效时、FrameEffects 之前；默认转给主人。</summary>
    public virtual void PreUpdateVanitySet(Player player)
    {
        if (Item != null) Item.PreUpdateVanitySet(player);
    }

    /// <summary>时装套生效时、FrameEffects 之后；默认转给主人。</summary>
    public virtual void UpdateVanitySet(Player player)
    {
        if (Item != null) Item.UpdateVanitySet(player);
    }

    /// <summary>这套的影子；默认转给主人。</summary>
    public virtual void ArmorSetShadows(Player player)
    {
        if (Item != null) Item.ArmorSetShadows(player);
    }

    /// <summary>这件画到哪个槽（equipSlot/robes 都是可写引用）；默认转给主人。</summary>
    public virtual void SetMatch(bool male, ref int equipSlot, ref bool robes)
    {
        if (Item != null) Item.SetMatch(male, ref equipSlot, ref robes);
    }

    /// <summary>空中这对翅膀的五个可写引用（没穿着的物品，item 传 null）；默认转给主人。</summary>
    public virtual void VerticalWingSpeeds(Player player, ref float ascentWhenFalling, ref float ascentWhenRising,
                                           ref float maxCanAscendMultiplier, ref float maxAscentMultiplier, ref float constantAscend)
    {
        if (Item != null) Item.VerticalWingSpeeds(null!, player, ref ascentWhenFalling, ref ascentWhenRising,
                                                  ref maxCanAscendMultiplier, ref maxAscentMultiplier, ref constantAscend);
    }

    /// <summary>空中平飞的加速度和速度（两个可写引用）；默认转给主人。</summary>
    public virtual void HorizontalWingSpeeds(Player player, ref float speed, ref float acceleration)
    {
        if (Item != null) Item.HorizontalWingSpeeds(null!, player, ref speed, ref acceleration);
    }

    /// <summary>true：翅膀由 mod 自己动画（游戏的 WingFrame 不跑）；没主人就是 false。</summary>
    public virtual bool WingUpdate(Player player, bool inUse) => Item != null && Item.WingUpdate(player, inUse);
}
