using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader;

/// <summary>
/// 资源条窗口内的 SpriteBatch.Draw 捕获：把游戏这一笔的真实贴图与变换交给覆盖层，
/// 而不是按样式猜。只在 <see cref="Active"/> 期间工作，其余绘制直接直通。
/// </summary>
internal static class SpriteBatchCapture
{
    internal sealed class DrawInfo
    {
        public Texture2D Texture = null!;
        public Vector2 Position;
        public Rectangle? Source;
        public Color Color;
        public float Rotation;
        public Vector2 Origin;
        public Vector2 Scale;
        public SpriteEffects Effects;
    }

    internal static bool Active;

    /// <summary>返回 false = 这一笔不画。null = 不需要接管。</summary>
    internal static Func<DrawInfo, bool>? Pre;

    internal static Action<DrawInfo>? Post;

    internal static void Install()
    {
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_2 += Draw_2;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_3 += Draw_3;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_4 += Draw_4;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_5 += Draw_5;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_6 += Draw_6;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_7 += Draw_7;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_11 += Draw_11;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_12 += Draw_12;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_14 += Draw_14;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_16 += Draw_16;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_18 += Draw_18;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_20 += Draw_20;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_21 += Draw_21;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_Fast += Draw_Fast;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_22 += Draw_22;
    }
    private static void Draw_Fast(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_Fast orig,
        SpriteBatch self, Texture2D texture, ref Vector2 position, ref Rectangle source, ref Color color, SpriteEffects effects)
    {
        if (!Active) { orig(self, texture, ref position, ref source, ref color, effects); return; }

        var info = new DrawInfo { Texture = texture, Position = position, Source = source, Color = color, Scale = Vector2.One, Effects = effects };
        var go = Pre?.Invoke(info) ?? true;
        if (go) orig(self, texture, ref position, ref source, ref color, effects);
        Post?.Invoke(info);
    }

    private static Rectangle? Rect(Il2CppSystem.Nullable<Rectangle> source)
    {
        try { return source.HasValue ? source.Value : null; }
        catch { return null; }
    }

    private static void Draw_2(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_2 orig,
        SpriteBatch self, Texture2D texture, ref Vector2 position, ref Rectangle source, ref Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects)
    {
        if (!Active) { orig(self, texture, ref position, ref source, ref color, rotation, origin, scale, effects); return; }

        var info = new DrawInfo { Texture=texture, Position=position, Source=source, Color=color, Rotation=rotation, Origin=origin, Scale=new Vector2(scale,scale), Effects=effects };
        var go = Pre?.Invoke(info) ?? true;
        if (go) orig(self, texture, ref position, ref source, ref color, rotation, origin, scale, effects);
        Post?.Invoke(info);
    }

    private static void Draw_3(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_3 orig,
        SpriteBatch self, Texture2D texture, ref Vector2 position, ref Rectangle source, ref Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects)
    {
        if (!Active) { orig(self, texture, ref position, ref source, ref color, rotation, origin, scale, effects); return; }

        var info = new DrawInfo { Texture=texture, Position=position, Source=source, Color=color, Rotation=rotation, Origin=origin, Scale=scale, Effects=effects };
        var go = Pre?.Invoke(info) ?? true;
        if (go) orig(self, texture, ref position, ref source, ref color, rotation, origin, scale, effects);
        Post?.Invoke(info);
    }

    private static void Draw_4(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_4 orig,
        SpriteBatch self, Texture2D texture, ref Vector2 position, ref Rectangle source, ref Color color, float rotation, Vector2 origin, float scale)
    {
        if (!Active) { orig(self, texture, ref position, ref source, ref color, rotation, origin, scale); return; }

        var info = new DrawInfo { Texture=texture, Position=position, Source=source, Color=color, Rotation=rotation, Origin=origin, Scale=new Vector2(scale,scale) };
        var go = Pre?.Invoke(info) ?? true;
        if (go) orig(self, texture, ref position, ref source, ref color, rotation, origin, scale);
        Post?.Invoke(info);
    }

    private static void Draw_5(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_5 orig,
        SpriteBatch self, Texture2D texture, ref Vector2 position, ref Color color, float rotation, Vector2 origin, float scale)
    {
        if (!Active) { orig(self, texture, ref position, ref color, rotation, origin, scale); return; }

        var info = new DrawInfo { Texture=texture, Position=position, Color=color, Rotation=rotation, Origin=origin, Scale=new Vector2(scale,scale) };
        var go = Pre?.Invoke(info) ?? true;
        if (go) orig(self, texture, ref position, ref color, rotation, origin, scale);
        Post?.Invoke(info);
    }

    private static void Draw_6(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_6 orig,
        SpriteBatch self, Texture2D texture, Vector2 position, Il2CppSystem.Nullable<Rectangle> source, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float depth)
    {
        if (!Active) { orig(self, texture, position, source, color, rotation, origin, scale, effects, depth); return; }

        var info = new DrawInfo { Texture=texture, Position=position, Source=Rect(source), Color=color, Rotation=rotation, Origin=origin, Scale=scale, Effects=effects };
        var go = Pre?.Invoke(info) ?? true;
        if (go) orig(self, texture, position, source, color, rotation, origin, scale, effects, depth);
        Post?.Invoke(info);
    }

    private static void Draw_7(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_7 orig,
        SpriteBatch self, Texture2D texture, Vector2 position, ref Rectangle source, Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float depth)
    {
        if (!Active) { orig(self, texture, position, ref source, color, rotation, origin, scale, effects, depth); return; }

        var info = new DrawInfo { Texture=texture, Position=position, Source=source, Color=color, Rotation=rotation, Origin=origin, Scale=scale, Effects=effects };
        var go = Pre?.Invoke(info) ?? true;
        if (go) orig(self, texture, position, ref source, color, rotation, origin, scale, effects, depth);
        Post?.Invoke(info);
    }

    private static void Draw_11(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_11 orig,
        SpriteBatch self, Texture2D texture, Vector2 position, Il2CppSystem.Nullable<Rectangle> source, Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float depth)
    {
        if (!Active) { orig(self, texture, position, source, color, rotation, origin, scale, effects, depth); return; }

        var info = new DrawInfo { Texture=texture, Position=position, Source=Rect(source), Color=color, Rotation=rotation, Origin=origin, Scale=new Vector2(scale,scale), Effects=effects };
        var go = Pre?.Invoke(info) ?? true;
        if (go) orig(self, texture, position, source, color, rotation, origin, scale, effects, depth);
        Post?.Invoke(info);
    }

    private static void Draw_12(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_12 orig,
        SpriteBatch self, Texture2D texture, ref Vector2 position, ref Rectangle source, ref Color color)
    {
        if (!Active) { orig(self, texture, ref position, ref source, ref color); return; }

        var info = new DrawInfo { Texture=texture, Position=position, Source=source, Color=color, Scale=Vector2.One };
        var go = Pre?.Invoke(info) ?? true;
        if (go) orig(self, texture, ref position, ref source, ref color);
        Post?.Invoke(info);
    }

    private static void Draw_14(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_14 orig,
        SpriteBatch self, Texture2D texture, Vector2 position, ref Rectangle source, Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float depth)
    {
        if (!Active) { orig(self, texture, position, ref source, color, rotation, origin, scale, effects, depth); return; }

        var info = new DrawInfo { Texture=texture, Position=position, Source=source, Color=color, Rotation=rotation, Origin=origin, Scale=new Vector2(scale,scale), Effects=effects };
        var go = Pre?.Invoke(info) ?? true;
        if (go) orig(self, texture, position, ref source, color, rotation, origin, scale, effects, depth);
        Post?.Invoke(info);
    }

    private static void Draw_16(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_16 orig,
        SpriteBatch self, Texture2D texture, Rectangle destination, Il2CppSystem.Nullable<Rectangle> source, Color color, float rotation, Vector2 origin, SpriteEffects effects, float depth)
    {
        if (!Active) { orig(self, texture, destination, source, color, rotation, origin, effects, depth); return; }

        var info = new DrawInfo { Texture=texture, Position=new Vector2(destination.X, destination.Y), Source=Rect(source), Color=color, Rotation=rotation, Origin=origin, Scale=Vector2.One, Effects=effects };
        var go = Pre?.Invoke(info) ?? true;
        if (go) orig(self, texture, destination, source, color, rotation, origin, effects, depth);
        Post?.Invoke(info);
    }

    private static void Draw_18(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_18 orig,
        SpriteBatch self, Texture2D texture, Vector2 position, Il2CppSystem.Nullable<Rectangle> source, Color color)
    {
        if (!Active) { orig(self, texture, position, source, color); return; }

        var info = new DrawInfo { Texture=texture, Position=position, Source=Rect(source), Color=color, Scale=Vector2.One };
        var go = Pre?.Invoke(info) ?? true;
        if (go) orig(self, texture, position, source, color);
        Post?.Invoke(info);
    }

    private static void Draw_20(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_20 orig,
        SpriteBatch self, Texture2D texture, Rectangle destination, Il2CppSystem.Nullable<Rectangle> source, Color color)
    {
        if (!Active) { orig(self, texture, destination, source, color); return; }

        var info = new DrawInfo { Texture=texture, Position=new Vector2(destination.X, destination.Y), Source=Rect(source), Color=color, Scale=Vector2.One };
        var go = Pre?.Invoke(info) ?? true;
        if (go) orig(self, texture, destination, source, color);
        Post?.Invoke(info);
    }

    private static void Draw_21(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_21 orig,
        SpriteBatch self, Texture2D texture, Vector2 position, Color color)
    {
        if (!Active) { orig(self, texture, position, color); return; }

        var info = new DrawInfo { Texture=texture, Position=position, Color=color, Scale=Vector2.One };
        var go = Pre?.Invoke(info) ?? true;
        if (go) orig(self, texture, position, color);
        Post?.Invoke(info);
    }

    private static void Draw_22(
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_22 orig,
        SpriteBatch self, Texture2D texture, Rectangle destination, Color color)
    {
        if (!Active) { orig(self, texture, destination, color); return; }

        var info = new DrawInfo { Texture=texture, Position=new Vector2(destination.X, destination.Y), Color=color, Scale=Vector2.One };
        var go = Pre?.Invoke(info) ?? true;
        if (go) orig(self, texture, destination, color);
        Post?.Invoke(info);
    }
}