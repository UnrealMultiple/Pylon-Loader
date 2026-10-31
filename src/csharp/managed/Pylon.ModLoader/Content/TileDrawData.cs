using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>画一格 tile 时要用的数据（对应参考里的 drawData）；改字段就改了这次的画法。</summary>
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
