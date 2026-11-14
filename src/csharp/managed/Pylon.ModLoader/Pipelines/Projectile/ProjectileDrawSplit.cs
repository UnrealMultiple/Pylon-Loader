using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader;

/// <summary>
/// 弹幕绘制的"附加物 / 精灵"拆分，给 <see cref="ModProjectile.PreDrawExtras"/> 与 <see cref="ModProjectile.PreDraw"/>
/// 的返回值真实语义。手机版把附加物（鱼线、钩爪链、链锤铁链、悠悠球绳）画在 <c>Main.DrawProjDirect</c> 内部，
/// 没有单独的绘制入口，所以挂住 <c>SpriteBatch.Draw</c> 的四个重载。
/// 在拆分窗口内按贴图分流：遇到弹幕自己的贴图之前由 extras 决定画不画，遇到（含）之后由 sprite 决定。
/// 绘制只发生在游戏线程，状态按线程隔离。
/// </summary>
internal static class ProjectileDrawSplit
{
    private const int Depth = 8;

    [ThreadStatic]
    private static Split[]? _stack;

    [ThreadStatic]
    private static int _depth;

    internal static void Install()
    {
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_Texture2D_Vector2_Nullable_Rectangle_Color_float_Vector2_float_SpriteEffects_float += BatchFloat;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_Texture2D_Vector2_Nullable_Rectangle_Color_float_Vector2_Vector2_SpriteEffects_float += BatchVector;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_Texture2D_Vector2Ref_RectangleRef_ColorRef_float_Vector2_float_SpriteEffects += BatchRefFloat;
        On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.Draw_Texture2D_Vector2Ref_RectangleRef_ColorRef_float_Vector2_Vector2_SpriteEffects += BatchRefVector;
        On.Terraria.DataStructures.DrawData.Draw_SpriteBatch += DrawDataSpriteBatch;
    }

    internal static bool Begin(IntPtr own, bool extras, bool sprite)
    {
        if (_depth >= Depth) return false;
        var stack = _stack ??= new Split[Depth];
        stack[_depth++] = new Split { Own = own, Extras = extras, Sprite = sprite };
        return true;
    }

    internal static void End()
    {
        if (_depth > 0) _depth--;
    }

    private static bool Pass(Texture2D? texture)
    {
        try
        {
            var stack = _stack;
            if (_depth <= 0 || stack is null || texture is null) return true;

            ref var split = ref stack[_depth - 1];
            if (!split.Reached && texture.Pointer == split.Own) split.Reached = true;
            return split.Reached ? split.Sprite : split.Extras;
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"projectile draw split: {e}");
            return true;
        }
    }

    private static void BatchFloat(On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_Texture2D_Vector2_Nullable_Rectangle_Color_float_Vector2_float_SpriteEffects_float orig,
        SpriteBatch self, Texture2D texture, Vector2 position, Il2CppSystem.Nullable<Rectangle> sourceRectangle,
        Color color, float rotation, Vector2 origin, float scale, SpriteEffects effects, float layerDepth)
    {
        if (!Pass(texture)) return;
        orig(self, texture, position, sourceRectangle, color, rotation, origin, scale, effects, layerDepth);
    }

    private static void BatchVector(On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_Texture2D_Vector2_Nullable_Rectangle_Color_float_Vector2_Vector2_SpriteEffects_float orig,
        SpriteBatch self, Texture2D texture, Vector2 position, Il2CppSystem.Nullable<Rectangle> sourceRectangle,
        Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth)
    {
        if (!Pass(texture)) return;
        orig(self, texture, position, sourceRectangle, color, rotation, origin, scale, effects, layerDepth);
    }

    private static void BatchRefFloat(On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_Texture2D_Vector2Ref_RectangleRef_ColorRef_float_Vector2_float_SpriteEffects orig,
        SpriteBatch self, Texture2D texture, ref Vector2 position, ref Rectangle sourceRectangle, ref Color color,
        float rotation, Vector2 origin, float scale, SpriteEffects effects)
    {
        if (!Pass(texture)) return;
        orig(self, texture, ref position, ref sourceRectangle, ref color, rotation, origin, scale, effects);
    }

    private static void BatchRefVector(On.Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch.orig_Draw_Texture2D_Vector2Ref_RectangleRef_ColorRef_float_Vector2_Vector2_SpriteEffects orig,
        SpriteBatch self, Texture2D texture, ref Vector2 position, ref Rectangle sourceRectangle, ref Color color,
        float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects)
    {
        if (!Pass(texture)) return;
        orig(self, texture, ref position, ref sourceRectangle, ref color, rotation, origin, scale, effects);
    }

    private static void DrawDataSpriteBatch(On.Terraria.DataStructures.DrawData.orig_Draw_SpriteBatch orig,
        Terraria.DataStructures.DrawData data, SpriteBatch spriteBatch)
    {
        if (!Pass(data)) return;
        orig(data, spriteBatch);
    }

    private static bool Pass(Terraria.DataStructures.DrawData? data)
    {
        try
        {
            if (_depth <= 0) return true;
            return data is not null && data.texture is not null && Pass(data.texture);
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"projectile DrawData split: {e}");
            return true;
        }
    }

    private struct Split
    {
        public IntPtr Own;
        public bool Extras;
        public bool Sprite;
        public bool Reached;
    }
}
