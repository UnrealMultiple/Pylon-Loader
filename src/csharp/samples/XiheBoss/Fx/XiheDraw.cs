// SpriteBatch 的适配：手机版 XNA 的 Draw 重载只收 Il2CppSystem.Nullable<Rectangle> 的源矩形，
// 直接传 Rectangle 会选到 Nullable<Vector2> 的怪重载（编译不过）。这里统一包一层。
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
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

        internal static void SpriteFull(SpriteBatch sb, Texture2D tex, Vector2 pos, Color color,
            float rotation, float scale, SpriteEffects effects = SpriteEffects.None)
        {
            Sprite(sb, tex, pos, new Rectangle(0, 0, tex.Width, tex.Height), color, rotation,
                Utils.Size(tex) / 2f, scale, effects);
        }    }
}


