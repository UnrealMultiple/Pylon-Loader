using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Pylon.ModLoader.Core;
using NullableRect = Il2CppSystem.Nullable<Il2CppMicrosoft.Xna.Framework.Rectangle>;

namespace XiheBoss
{
    internal static class XiheDraw
    {
        internal static void Sprite(SpriteBatch sb, Texture2D tex, Vector2 pos, Rectangle frame, Color color,
            float rotation, Vector2 origin, float scale, SpriteEffects effects = SpriteEffects.None)
        {
            sb.Draw(tex, pos, new NullableRect(frame), color, rotation, origin, scale, effects, 0f);
        }

        internal static void Sprite(SpriteBatch sb, Texture2D tex, Vector2 pos, Rectangle frame, Color color,
            float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects = SpriteEffects.None)
        {
            sb.Draw(tex, pos, new NullableRect(frame), color, rotation, origin, scale, effects, 0f);
        }

        internal static void SpriteFull(SpriteBatch sb, Texture2D tex, Vector2 pos, Color color,
            float rotation, float scale, SpriteEffects effects = SpriteEffects.None)
        {
            Sprite(sb, tex, pos, new Rectangle(0, 0, tex.Width, tex.Height), color, rotation,
                Utils.Size(tex) / 2f, scale, effects);
        }    }
}
