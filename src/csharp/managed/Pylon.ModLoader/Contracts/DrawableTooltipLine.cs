using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria.GameContent;

namespace Pylon.ModLoader
{
    /// <summary>tooltip 画线时的那一行：PreDrawTooltipLine 能改 X/Y、Font、Rotation、Origin、BaseScale、Spread；文字和行在 ModifyTooltips 改。</summary>
    public class DrawableTooltipLine : TooltipLine
    {
        /// <summary>从父行复制显示属性（super 带上 Mod/Name/Text），再带上这次绘制的参数；管线在画每行前 new 出来。</summary>
        public DrawableTooltipLine(TooltipLine parent, int index, float x, float y, Color color)
            : base(parent.Mod, parent.Name, parent.Text)
        {
            OneDropLogo = parent.OneDropLogo;
            IsModifier = parent.IsModifier;
            IsModifierBad = parent.IsModifierBad;
            OverrideColor = parent.OverrideColor;

            Index = index;
            OriginalX = X = x;
            OriginalY = Y = y;
            Color = color;
            Font = FontAssets.MouseText.Value;
            Rotation = 0;
            Origin = Vector2.Zero;
            BaseScale = Vector2.One;
            MaxWidth = -1;
            Spread = 2;
        }

        /// <summary>这行在 tooltip 里的编号（从 0 数）。</summary>
        public int Index;

        /// <summary>画之前那帧的 X；X 和 OriginalX 初值相同（改 X 就移这行）。</summary>
        public float OriginalX;

        /// <summary>这行画在哪（PreDrawTooltipLine 改它移动这行）。</summary>
        public float X;

        /// <summary>画之前那帧的 Y。</summary>
        public float OriginalY;

        /// <summary>这行画在哪。</summary>
        public float Y;

        /// <summary>最终颜色（已带 tooltip 的透明度）。</summary>
        public Color Color;

        /// <summary>画这行文字用的字体（默认 MouseText）。</summary>
        public SpriteFont Font;

        /// <summary>旋转（默认 0）。</summary>
        public float Rotation;

        /// <summary>文字的原点（默认左上角）。</summary>
        public Vector2 Origin;

        /// <summary>缩放（默认 1；已包含手机 tooltip 的缩放）。</summary>
        public Vector2 BaseScale;

        /// <summary>文本最大宽度（默认 -1）。</summary>
        public float MaxWidth;

        /// <summary>四道阴影的间距（默认 2）。</summary>
        public float Spread;

        // OneDropLogo/IsModifier/IsModifierBad/OverrideColor 是 TooltipLine 的成员，构造时从 parent 复制；声明在 TooltipLine.cs。
    }
}