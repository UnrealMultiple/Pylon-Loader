using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader;

/// <summary>只在单个增益图标的原版绘制窗口内修改真实绘制参数，不接管布局或输入。</summary>
internal static class BuffIconCapture
{
    [ThreadStatic] private static Scope? _current;
    private static bool _installed;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_Texture2D_Vector2_Nullable_Rectangle_Color_float_Vector2_float_SpriteEffects_float += Draw;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_Texture2D_Vector2Ref_RectangleRef_ColorRef_float_Vector2_Vector2_SpriteEffects += DrawScaled;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_Texture2D_Vector2_Nullable_Rectangle_Color_float_Vector2_Vector2_SpriteEffects_float += DrawScaledNullable;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.DrawString_SpriteFont_string_Vector2_Color_float_Vector2_float_SpriteEffects_float += DrawString;
    }

    internal sealed class Scope : IDisposable
    {
        private readonly Scope? _outer;
        internal readonly Texture2D OriginalTexture;
        internal readonly Vector2 PositionDelta, TextDelta;
        internal readonly BuffDrawParams Parameters;
        internal readonly bool DrawIcon, SourceChanged, ColorChanged;
        internal readonly Vector2 TextAnchor;
        internal bool IconSeen;
        private bool _disposed;

        internal Scope(BuffDrawParams before, BuffDrawParams after, bool draw)
        {
            _outer = _current;
            OriginalTexture = before.Texture;
            PositionDelta = after.Position - before.Position;
            TextDelta = after.TextPosition - before.TextPosition;
            TextAnchor = before.TextPosition;
            Parameters = after;
            DrawIcon = draw;
            SourceChanged = before.SourceRectangle != after.SourceRectangle;
            ColorChanged = before.DrawColor != after.DrawColor;
            _current = this;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _current = _outer;
        }
    }

    private static void Draw(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_Texture2D_Vector2_Nullable_Rectangle_Color_float_Vector2_float_SpriteEffects_float orig,
        SpriteBatch self, Texture2D texture, Vector2 position, Il2CppSystem.Nullable<Rectangle> source, Color color,
        float rotation, Vector2 origin, float scale, SpriteEffects effects, float depth)
    {
        var scope = _current;
        if (scope is null || texture is null || texture.Pointer != scope.OriginalTexture.Pointer)
        {
            orig(self, texture, position, source, color, rotation, origin, scale, effects, depth);
            return;
        }

        scope.IconSeen = true;
        // 不跳过 ItemDraw：它还负责交互与时间文字。否决仅取消对应贴图的绘制。
        if (!scope.DrawIcon) return;
        var parameters = scope.Parameters;
        var selected = parameters.Texture;
        if (selected is null) selected = texture;
        if (scope.SourceChanged) source = new Il2CppSystem.Nullable<Rectangle>(parameters.SourceRectangle);
        if (scope.ColorChanged) color = parameters.DrawColor;
        orig(self, selected, position + scope.PositionDelta, source, color, rotation, origin, scale, effects, depth);
    }

    private static void DrawScaled(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_Texture2D_Vector2Ref_RectangleRef_ColorRef_float_Vector2_Vector2_SpriteEffects orig,
        SpriteBatch self, Texture2D texture, ref Vector2 position, ref Rectangle source, ref Color color,
        float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects)
    {
        var scope = _current;
        if (scope is null || texture is null || texture.Pointer != scope.OriginalTexture.Pointer)
        {
            orig(self, texture, ref position, ref source, ref color, rotation, origin, scale, effects);
            return;
        }

        scope.IconSeen = true;
        if (!scope.DrawIcon) return;
        var parameters = scope.Parameters;
        var selected = parameters.Texture ?? texture;
        if (scope.SourceChanged) source = parameters.SourceRectangle;
        if (scope.ColorChanged) color = parameters.DrawColor;
        position += scope.PositionDelta;
        orig(self, selected, ref position, ref source, ref color, rotation, origin, scale, effects);
    }

    private static void DrawScaledNullable(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_Texture2D_Vector2_Nullable_Rectangle_Color_float_Vector2_Vector2_SpriteEffects_float orig,
        SpriteBatch self, Texture2D texture, Vector2 position, Il2CppSystem.Nullable<Rectangle> source, Color color,
        float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float depth)
    {
        var scope = _current;
        if (scope is null || texture is null || texture.Pointer != scope.OriginalTexture.Pointer)
        {
            orig(self, texture, position, source, color, rotation, origin, scale, effects, depth);
            return;
        }

        scope.IconSeen = true;
        if (!scope.DrawIcon) return;
        var parameters = scope.Parameters;
        var selected = parameters.Texture ?? texture;
        if (scope.SourceChanged) source = new Il2CppSystem.Nullable<Rectangle>(parameters.SourceRectangle);
        if (scope.ColorChanged) color = parameters.DrawColor;
        orig(self, selected, position + scope.PositionDelta, source, color, rotation, origin, scale, effects, depth);
    }

    private static void DrawString(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_DrawString_SpriteFont_string_Vector2_Color_float_Vector2_float_SpriteEffects_float orig,
        SpriteBatch self, SpriteFont font, string text, Vector2 position, Color color, float rotation, Vector2 origin,
        float scale, SpriteEffects effects, float depth)
    {
        var scope = _current;
        // 单格可能夹带其它 UI 文字；仅修改图标之后、位于时间锚点附近的非空文本。
        if (scope is not null && scope.IconSeen && !string.IsNullOrEmpty(text) &&
            MathF.Abs(position.X - scope.TextAnchor.X) <= 64 && MathF.Abs(position.Y - scope.TextAnchor.Y) <= 64)
            position += scope.TextDelta;
        orig(self, font, text, position, color, rotation, origin, scale, effects, depth);
    }

}
