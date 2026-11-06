using Terraria;
using Terraria.GameContent;
using Pylon.ModLoader.Textures;

namespace Pylon.ModLoader;

/// <summary>就绪判据：只在游戏表"建好且还是原版长度"时才允许安装，否则会被游戏重建覆盖。</summary>
internal static class GameReadiness
{
    public static bool LooksReady()
    {
        try
        {
            if (!Main._isAsyncLoadComplete) return false;
            if (Main.ItemDropsDB == null || Main.BestiaryDB == null) return false;
            if (Recipe.numRecipes <= 0) return false;

            var textures = TextureSlots.Length(TextureTable.Item);
            if (textures != Terraria.ID.ItemID.Count) return false;

            var names = Lang._itemNameCache;
            if (names == null || names.Length < Terraria.ID.ItemID.Count) return false;

            return Terraria.ID.ContentSamples.ItemsByType != null;
        }
        catch
        {
            return false;
        }
    }
}