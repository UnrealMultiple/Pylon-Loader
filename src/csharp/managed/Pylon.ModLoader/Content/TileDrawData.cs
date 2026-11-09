using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader;

public sealed class TileDrawData
{
    /// <summary>这一格的帧 X。</summary>
    public int tileFrameX;

    /// <summary>这一格的帧 Y。</summary>
    public int tileFrameY;

    /// <summary>这一格的类型。</summary>
    public int typeCache;

    /// <summary>这一格的受光色。</summary>
    public Color tileLight;

    /// <summary>这一格本身。</summary>
    public Tile tileCache;
}