using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria.GameContent.UI;

namespace Pylon.ModLoader;

/// <summary>
/// 作用于所有表情气泡（游戏的与 mod 的）的横切类：一个类一个实例，钩子自己按气泡挑。
/// 与 <see cref="GlobalBuff"/> 同一套形状——注册表按覆写的方法建位集，没覆写的不进分派。
/// </summary>
public abstract class GlobalEmoteBubble : ModType
{
    /// <summary>气泡建出来时一次（游戏的与 mod 的表情都会走到）。</summary>
    public virtual void OnSpawn(EmoteBubble bubble) { }

    /// <summary>每帧；false = 不再走游戏那一套帧推进（与 <see cref="ModEmoteBubble.UpdateFrame"/> 同义）。</summary>
    public virtual bool UpdateFrame(EmoteBubble bubble) => true;

    /// <summary>这个气泡的绘制帧；null = 不问（作者不接管）。</summary>
    public virtual Rectangle? GetFrame(EmoteBubble bubble) => null;

    /// <summary>画这个气泡之前；false = 这一帧不画（只对 mod 表情生效，见文档）。</summary>
    public virtual bool PreDraw(EmoteBubble bubble, SpriteBatch spriteBatch, Texture2D texture, Vector2 position,
                                Rectangle frame, Vector2 origin, SpriteEffects spriteEffects) => true;

    /// <summary>画完之后（只对 mod 表情生效，见文档）。</summary>
    public virtual void PostDraw(EmoteBubble bubble, SpriteBatch spriteBatch, Texture2D texture, Vector2 position,
                                 Rectangle frame, Vector2 origin, SpriteEffects spriteEffects) { }

    /// <summary>表情菜单里的取帧（游戏的与 mod 的表情都会问）；null = 不问。</summary>
    public virtual Rectangle? GetFrameInEmoteMenu(int emoteType, int frame, int frameCounter) => null;

    /// <summary>菜单里每帧的帧计数（本版没有对应入口，保留声明；见文档"这一版没接"）。</summary>
    public virtual bool UpdateFrameInEmoteMenu(int emoteType, ref int frameCounter) => true;
}
