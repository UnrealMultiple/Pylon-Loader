using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.GameContent;

namespace Pylon.ModLoader.Textures
{
    /// <summary>游戏里按类型索引的贴图表。</summary>
    public enum TextureTable
    {
        Item, ItemFlame, Projectile, Tile, Wall, Npc, NpcHead, NpcHeadBoss, Buff, Gore, Extra,
    }

    /// <summary>贴图表槽位的读写</summary>
    public static class TextureSlots
    {
        /// <summary>写槽位；表没建或越界返回 false</summary>
        public static bool TryWrite(TextureTable table, int type, Asset<Texture2D> asset)
        {
            if (asset == null) return false;
            var array = Table(table);
            if (array == null || type < 0 || type >= array.Length) return false;
            array[type] = asset;
            return true;
        }

        /// <summary>表长（-1 = 表还没建）；用来判断"该扩表了"。</summary>
        public static int Length(TextureTable table)
        {
            var array = Table(table);
            return array == null ? -1 : array.Length;
        }

        /// <summary>编译期类型的静态属性</summary>
        private static Il2CppReferenceArray<Asset<Texture2D>> Table(TextureTable table)
        {
            try
            {
                switch (table)
                {
                    case TextureTable.Item: return TextureAssets.Item;
                    case TextureTable.ItemFlame: return TextureAssets.ItemFlame;
                    case TextureTable.Projectile: return TextureAssets.Projectile;
                    case TextureTable.Tile: return TextureAssets.Tile;
                    case TextureTable.Wall: return TextureAssets.Wall;
                    case TextureTable.Npc: return TextureAssets.Npc;
                    case TextureTable.NpcHead: return TextureAssets.NpcHead;
                    case TextureTable.NpcHeadBoss: return TextureAssets.NpcHeadBoss;
                    case TextureTable.Buff: return TextureAssets.Buff;
                    case TextureTable.Gore: return TextureAssets.Gore;
                    case TextureTable.Extra: return TextureAssets.Extra;
                    default: return null;
                }
            }
            catch
            {
                return null;   // 表还没建时属性可能抛 NRE
            }
        }
    }
}
