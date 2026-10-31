using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.GameContent;

namespace Pylon.ModLoader.Textures;

/// <summary>贴图表槽位的读写。</summary>
public static class TextureSlots
{
    /// <summary>写槽位；表没建或越界返回 false。</summary>
    public static bool TryWrite(TextureTable table, int type, Asset<Texture2D>? asset)
    {
        if (asset is null) return false;
        var array = Table(table);
        if (array is null || type < 0 || type >= array.Length) return false;
        array[type] = asset;
        return true;
    }

    /// <summary>表长（-1 = 表还没建）；用来判断"该扩表了"。</summary>
    public static int Length(TextureTable table) => Table(table)?.Length ?? -1;

    private static Il2CppReferenceArray<Asset<Texture2D>>? Table(TextureTable table)
    {
        try
        {
            return table switch
            {
                TextureTable.Item => TextureAssets.Item,
                TextureTable.ItemFlame => TextureAssets.ItemFlame,
                TextureTable.Projectile => TextureAssets.Projectile,
                TextureTable.Tile => TextureAssets.Tile,
                TextureTable.Wall => TextureAssets.Wall,
                TextureTable.Npc => TextureAssets.Npc,
                TextureTable.NpcHead => TextureAssets.NpcHead,
                TextureTable.NpcHeadBoss => TextureAssets.NpcHeadBoss,
                TextureTable.Buff => TextureAssets.Buff,
                TextureTable.Gore => TextureAssets.Gore,
                TextureTable.Extra => TextureAssets.Extra,
                _ => null,
            };
        }
        catch
        {
            return null;   // 表还没建时这些静态属性可能抛 NRE
        }
    }
}
