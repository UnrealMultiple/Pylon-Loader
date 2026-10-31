using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader;

/// <summary>血条这次绘制的参数；PreDraw/PostDraw 里改字段就改了这次的绘制。</summary>
public sealed class BossBarDrawParams
{
    /// <summary>这次画的图标贴图。</summary>
    public Texture2D IconTexture = null!;

    /// <summary>从图标上取哪一块。</summary>
    public Rectangle IconFrame;

    /// <summary>生命。</summary>
    public float Life;

    /// <summary>最大生命。</summary>
    public float LifeMax;

    /// <summary>护盾当前值（和 ShieldMax 都是 0 就不画盾）。</summary>
    public float Shield;

    /// <summary>护盾上限。</summary>
    public float ShieldMax;

    /// <summary>条身中心（默认屏幕中下）。</summary>
    public Vector2 BarCenter;

    /// <summary>条身着色（默认白）。</summary>
    public Color BarColor;

    /// <summary>图标着色（默认白）。</summary>
    public Color IconColor;

    /// <summary>图标缩放（默认 1）。</summary>
    public float IconScale;

    /// <summary>显不显血条上的生命文字（默认 true）。</summary>
    public bool ShowText;

    /// <summary>生命文字相对默认位置的偏移。</summary>
    public Vector2 TextOffset;

    /// <summary>条身贴图（默认游戏的 UI_BossBar）。</summary>
    public Texture2D BarTexture = null!;
}
