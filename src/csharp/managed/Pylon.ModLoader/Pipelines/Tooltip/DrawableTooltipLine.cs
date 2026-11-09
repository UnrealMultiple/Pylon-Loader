using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria.GameContent;

namespace Pylon.ModLoader;

/// <summary>
/// 绘制期的一行：PreDrawTooltipLine 能改 Text / Color / Visible / Rotation / BaseScale / MaxWidth / Spread。
/// 位置是管线按行序号估出来的（游戏真正的排版不公开），改 X/Y 只影响作者自己画的叠加层。
/// </summary>
public class DrawableTooltipLine : TooltipLine
{
    public DrawableTooltipLine(TooltipLine parent, int index, int x, int y, Color color)
        : base(parent.Mod, parent.Name, parent.Text)
    {
        IsModifier = parent.IsModifier;
        IsModifierBad = parent.IsModifierBad;
        OneDropLogo = parent.OneDropLogo;
        OverrideColor = parent.OverrideColor;
        Role = parent.Role;

        Index = index;
        OriginalX = X = x;
        OriginalY = Y = y;
        Color = color;
        Font = FontAssets.MouseText.Value;
        BaseScale = Vector2.One;
        MaxWidth = -1;
        Spread = 2;
    }

    public readonly int Index;

    public readonly int OriginalX;

    public int X;

    public readonly int OriginalY;

    public int Y;

    public Color Color;

    public SpriteFont Font;

    public float Rotation;

    public Vector2 Origin;

    public Vector2 BaseScale;

    public float MaxWidth;

    public float Spread;
}
