using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.GameContent;
using NullableRect = Il2CppSystem.Nullable<Il2CppMicrosoft.Xna.Framework.Rectangle>;

namespace Pylon.ModLoader.UI;

/// <summary>
/// 面板：半透明背板 + 标题 + 文本行（+ 图片）。不建 UIPanel/UIText，每帧直接往 Main.spriteBatch 上画：
/// 文字与背板同帧出现，字体没就绪就整块不画。
/// </summary>
public sealed class ModPanel
{
    private readonly List<Action<SpriteBatch>> _rows = [];
    private readonly string _title;
    private readonly float _width, _height, _left, _top;
    private float _nextRow = 26f;

    public ModPanel(string title, float width = 340f, float height = 200f, float left = 60f, float top = 120f)
    {
        _title = title;
        _width = width;
        _height = height;
        _left = left;
        _top = top;
        ModUI.Register(this);
    }

    public ModPanel AddText(string text, float scale = 0.8f)
    {
        var y = _nextRow;
        _nextRow += 22f * scale + 8f;
        _rows.Add(batch => DrawText(batch, text, y, new Color(235, 235, 235), scale));
        return this;
    }

    public ModPanel AddImage(Asset<Texture2D>? texture, float width, float height)
    {
        var y = _nextRow;
        _nextRow += height + 8f;
        _rows.Add(batch =>
        {
            if (texture == null) return;
            var src = texture.Value;
            if (src == null) return;
            batch.Draw(src, new Rectangle((int)(_left + 12), (int)(_top + y), (int)width, (int)height),
                new NullableRect(), Color.White);
        });
        return this;
    }

    /// <summary>每帧直绘。字体没就绪就整块不画——面板和文字一起出现。</summary>
    internal void Draw(SpriteBatch? batch)
    {
        var font = TryFont();
        if (font == null || batch == null) return;

        // 背板：半透明深色（MagicPixel 铺一块）+ 顶边；资源没就绪时跳过背板，文字照画。
        try
        {
            var pixel = TextureAssets.MagicPixel.Value;
            if (pixel != null)
            {
                batch.Draw(pixel, new Rectangle((int)_left, (int)_top, (int)_width, (int)_height),
                    new NullableRect(), new Color(15, 15, 30, 215));
                batch.Draw(pixel, new Rectangle((int)_left, (int)_top, (int)_width, 2),
                    new NullableRect(), new Color(110, 110, 150, 210));
            }
        }
        catch (Exception) { }

        DrawText(batch, _title, 0f, Color.White, 0.9f);
        foreach (var row in _rows) row(batch);
    }

    private void DrawText(SpriteBatch batch, string text, float y, Color color, float scale)
    {
        var font = TryFont();
        if (font == null) return;
        batch.DrawString(font, text, new Vector2(_left + 12, _top + y), color, 0f, Vector2.Zero, scale,
            SpriteEffects.None, 0f);
    }

    private static SpriteFont? TryFont()
    {
        try { return FontAssets.MouseText.Value; }
        catch (Exception) { return null; }
    }
}
