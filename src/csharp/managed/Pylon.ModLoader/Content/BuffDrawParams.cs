using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader;

/// <summary>增益图标参数；图标绘制字段在原版对应绘制窗口应用。</summary>
public sealed class BuffDrawParams
{
    public Texture2D Texture = null!;
    /// <summary>图标位置；修改值作为相对原版这一笔绘制的位置差。</summary>
    public Vector2 Position;
    /// <summary>时间文字锚点；修改值作为附近时间文本的位置差。</summary>
    public Vector2 TextPosition;
    public Rectangle SourceRectangle;
    /// <summary>原始判定框快照；修改此字段不会回写原版输入判定。</summary>
    public Rectangle MouseRectangle;
    public Color DrawColor;
}
