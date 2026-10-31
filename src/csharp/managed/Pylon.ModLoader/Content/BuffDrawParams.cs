using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader;

/// <summary>buff 条图标的绘制参数，一个 buff 一份；改字段就改了这次的绘制。</summary>
public sealed class BuffDrawParams
{
    /// <summary>本次画的那张图（默认是 Buff 表里的）。</summary>
    public Texture2D Texture = null!;

    /// <summary>图标左上角。</summary>
    public Vector2 Position;

    /// <summary>时间文字的左上角。</summary>
    public Vector2 TextPosition;

    /// <summary>从图上取哪一块。</summary>
    public Rectangle SourceRectangle;

    /// <summary>鼠标判定框（默认以图标为中心）。</summary>
    public Rectangle MouseRectangle;

    /// <summary>着色（默认白）。</summary>
    public Color DrawColor;

    // ⚠ Pylon 限制：本版不 patch SpriteBatch.Draw（真机上会串 SpriteEffects），改那几个字段不会传到这次绘制；
    // 要自己画就在 PostDraw 里用 spriteBatch.Draw（原版图标已画完）。
}
