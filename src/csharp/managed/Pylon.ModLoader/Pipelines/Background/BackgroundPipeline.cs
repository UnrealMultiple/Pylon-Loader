using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader;

/// <summary>
/// 背景管线：场景效果赢家声明了表层背景风格就写 <c>Main.bgStyle</c>，并在 <c>Main.DrawBackground</c>
/// 里接管 mod 槽位的绘制（用作者自己的贴图、只画一层）。见 docs/pipeline/content-background.md。
/// </summary>
internal sealed class BackgroundPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static BackgroundPipeline Instance { get; } = new();

    public string Name => "background";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Main.DrawBackground += DrawBackground;
        On.Terraria.Main.DrawSurfaceBG += DrawSurfaceBG;
        On.Terraria.Main.DrawSurfaceBG_BackMountainsStep1 += BackMountainsStep1;
        On.Terraria.Main.DrawBackground_PickUndergroundBackgroundStyle += PickUnderground;
        On.Terraria.Main.get_backTextureValues += BackTextureValues;
        On.Terraria.Main.get_oldBackTextureValues += OldBackTextureValues;

        Log.Info("[background] 已挂（mod 槽位接管绘制 / 赢家写 bgStyle）；三层视差与地下背景本版不接");
        return true;
    }

    public void Refresh() => BackgroundTable.Assign();

    /// <summary>安装期一次：发号 + 静态默认值。</summary>
    internal void Build()
    {
        BackgroundTable.Assign();
        BackgroundTable.AssignUnderground();

        foreach (var style in BackgroundTable.All) style.SetStaticDefaults();
        foreach (var style in BackgroundTable.UndergroundStyles) style.SetStaticDefaults();

        if (BackgroundTable.Count > 0) Log.Info($"[background] 安装：{BackgroundTable.Count} 个表层风格");
    }

    // ===================== 每帧 =====================

    /// <summary>
    /// 赢家声明了表层背景风格就写上；没声明、或这一帧的档位压不过原版（<paramref name="allowed"/> = false）
    /// 就一个字都不动——保持游戏自己算的值，同时把淡入淡出的来源清掉（否则会一直改上一位的风格）。
    /// </summary>
    internal void Apply(ModSceneEffect? effect, bool allowed)
    {
        CurrentSurface = null;
        if (effect is null || !allowed) return;

        ModSurfaceBackgroundStyle? style;
        try
        {
            style = effect.SurfaceBackgroundStyle;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[background] {effect.Name}.SurfaceBackgroundStyle 异常：{Safe.Describe(e)}");
            return;
        }

        if (style is null) return;

        if (!style.Registered)
        {
            ContentErrors.Report($"[background] {effect.Name} 的 SurfaceBackgroundStyle 没注册：{style.Name}");
            return;
        }

        Main.bgStyle = style.Slot;
        CurrentSurface = style;
    }

    /// <summary>这一帧的表层风格赢家（画淡入淡出时要用）。</summary>
    private static ModSurfaceBackgroundStyle? CurrentSurface;

    // ===================== 淡入淡出 =====================

    /// <summary>淡入淡出的过渡速度：游戏那个字段是私有静态，没有属性，用原版的量级。</summary>
    private const float TransitionSpeed = 0.005f;

    /// <summary>原版画表层背景之前，按赢家改一遍各层透明度：改的是 <c>Main.bgAlphaFrontLayer</c> 这个静态字段。</summary>
    private static void DrawSurfaceBG(On.Terraria.Main.orig_DrawSurfaceBG orig, Main self)
    {
        ApplyFarFades();
        orig(self);
    }

    private static void ApplyFarFades()
    {
        if (CurrentSurface is not { } style) return;
        if (BackgroundTable.Count == 0) return;

        var fades = Main.bgAlphaFrontLayer;
        if (fades is null || fades.Length == 0) return;

        var length = fades.Length;
        if (FarFadeBuffer is null || FarFadeBuffer.Length != length) FarFadeBuffer = new float[length];

        for (var index = 0; index < length; index++) FarFadeBuffer[index] = fades[index];

        try
        {
            style.ModifyFarFades(FarFadeBuffer, TransitionSpeed);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[background] {style.Name}.ModifyFarFades 异常：{Safe.Describe(e)}");
            return;
        }

        for (var index = 0; index < length; index++) fades[index] = FarFadeBuffer[index];
    }

    private static float[]? FarFadeBuffer;

    // ===================== 地下背景 =====================

    /// <summary>赢家声明了地下风格就换号；没声明就保持原版。</summary>
    private static int PickUnderground(On.Terraria.Main.orig_DrawBackground_PickUndergroundBackgroundStyle orig, double magmaLayer)
    {
        var vanilla = orig(magmaLayer);
        return CurrentUnderground() is { } style ? style.Slot : vanilla;
    }

    private static ModUndergroundBackgroundStyle? CurrentUnderground()
    {
        if (SceneEffectPipeline.Current is not { } effect) return null;

        ModUndergroundBackgroundStyle? style;
        try
        {
            style = effect.UndergroundBackgroundStyle;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[background] {effect.Name}.UndergroundBackgroundStyle 异常：{Safe.Describe(e)}");
            return null;
        }

        return style is not null && style.Slot >= 0 ? style : null;
    }

    private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Texture2D> BackTextureValues(
        On.Terraria.Main.orig_get_backTextureValues orig, Main self)
    {
        var values = orig(self);
        PatchUnderground(values, CurrentUnderground());
        return values;
    }

    private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Texture2D> OldBackTextureValues(
        On.Terraria.Main.orig_get_oldBackTextureValues orig, Main self)
    {
        var values = orig(self);
        PatchUnderground(values, CurrentUnderground());
        return values;
    }

    /// <summary>5 层贴图换成作者的：先看 FillTextureArray 给的编号，落不到就整层用作者自己的图。</summary>
    private static void PatchUnderground(
        Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Texture2D>? values,
        ModUndergroundBackgroundStyle? style)
    {
        if (values is null || style is null) return;

        var slots = new int[5];
        for (var index = 0; index < slots.Length; index++) slots[index] = -1;

        try
        {
            style.FillTextureArray(slots);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[background] {style.Name}.FillTextureArray 异常：{Safe.Describe(e)}");
        }

        Texture2D? own;
        try
        {
            own = style.Texture?.Value;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[background] {style.Name}.Texture 异常：{Safe.Describe(e)}");
            own = null;
        }

        var table = TextureAssets.Background;
        var count = Math.Min(values.Length, 5);

        for (var index = 0; index < count; index++)
        {
            Texture2D? texture = null;

            var id = slots[index];
            if (id >= 0 && table is not null && id < table.Length)
            {
                try
                {
                    texture = table[id]?.Value;
                }
                catch
                {
                    texture = null;
                }
            }

            texture ??= own;
            if (texture is not null) values[index] = texture;
        }
    }

    // ===================== 绘制 =====================

    private static void DrawBackground(On.Terraria.Main.orig_DrawBackground orig, Main self)
    {
        if (BackgroundTable.Count == 0)
        {
            orig(self);
            return;
        }

        var slot = Main.bgStyle;
        if (BackgroundTable.Of(slot) is not { } style)
        {
            orig(self);
            return;
        }

        Draw(style);
    }

    /// <summary>一层：作者自己的贴图，按 scale 缩放、按 parallax 横向平铺。</summary>
    private static void Draw(ModSurfaceBackgroundStyle style)
    {
        var spriteBatch = Main.spriteBatch;
        if (spriteBatch is null) return;

        DrawVanillaLayer(style, spriteBatch, false, 0.12f);
        DrawVanillaLayer(style, spriteBatch, true, 0.22f);
        DrawClose(style, spriteBatch);
    }

    /// <summary>近层：a/b 是"山顶到屏幕"的两段系数，作者可写引用。</summary>
    private static void DrawClose(ModSurfaceBackgroundStyle style, SpriteBatch spriteBatch)
    {
        Texture2D? texture;
        try
        {
            texture = style.Texture?.Value;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[background] {style.Name}.Texture 异常：{Safe.Describe(e)}");
            return;
        }

        if (texture is null) return;

        float scale = 1.25f, a = 1800f, b = 1750f, parallax = 0.37f;

        try
        {
            style.ChooseCloseTexture(ref scale, ref parallax, ref a, ref b);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[background] {style.Name}.ChooseCloseTexture 异常：{Safe.Describe(e)}");
            return;
        }

        if (scale <= 0f || parallax <= 0f) return;

        var bgScale = scale * 2f;
        var w = texture.Width;
        var h = texture.Height;
        var width = (int)(w * bgScale);
        if (width <= 0) return;

        var screen = Main.screenPosition;
        if (screen.Y >= Main.worldSurface * 16.0 + 16.0) return;

        var x = screen.X * parallax;
        var startX = (int)(-x % width - width / 2);

        // a/b 在这里生效：山顶 magic number × a + b，再加上游戏自己的两个修正量 scAdj 与 push。
        // 注意是整数截断后再加，和原版行为一致。
        var topY = (int)(Step1Top * a + b) + (int)Main.instance.scAdj + Step1Push;

        var loops = Main.screenWidth / width + 2;

        var origin = new Il2CppSystem.Nullable<Vector2>(Vector2.Zero);
        var scaleNullable = new Il2CppSystem.Nullable<Vector2>(new Vector2(bgScale, bgScale));
        var colorNullable = new Il2CppSystem.Nullable<Color>(Color.White);

        for (var k = 0; k < loops; k++)
        {
            var position = new Il2CppSystem.Nullable<Vector2>(new Vector2(startX + width * k, topY));
            spriteBatch.Draw(texture, position, null, null, origin, 0f, scaleNullable, colorNullable, SpriteEffects.None, 0f);
        }

        try
        {
            style.PreDrawCloseBackground(spriteBatch);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[background] {style.Name}.PreDrawCloseBackground 异常：{Safe.Describe(e)}");
        }
    }

    /// <summary>山顶的 magic number，从 <c>DrawSurfaceBG_BackMountainsStep1</c> 的参数里存下来。</summary>
    private static float Step1Top;

    /// <summary>Step1 的 push 值。</summary>
    private static int Step1Push;

    private static void BackMountainsStep1(On.Terraria.Main.orig_DrawSurfaceBG_BackMountainsStep1 orig, Main self,
        float backgroundTopMagicNumber, float bgGlobalScaleMultiplier, int pushBGTopHack)
    {
        orig(self, backgroundTopMagicNumber, bgGlobalScaleMultiplier, pushBGTopHack);
        Step1Top = backgroundTopMagicNumber;
        Step1Push = pushBGTopHack;
    }

    /// <summary>远层 / 中层：作者返回游戏背景表的编号，越界或空资源就跳过。</summary>
    private static void DrawVanillaLayer(ModSurfaceBackgroundStyle style, SpriteBatch spriteBatch, bool middle, float parallax)
    {
        int id;
        try
        {
            id = middle ? style.ChooseMiddleTexture() : style.ChooseFarTexture();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[background] {style.Name}.Choose{(middle ? "Middle" : "Far")}Texture 异常：{Safe.Describe(e)}");
            return;
        }

        if (id < 0) return;

        var table = TextureAssets.Background;
        if (table is null || id >= table.Length) return;

        var texture = table[id]?.Value;
        if (texture is null) return;

        DrawTiled(spriteBatch, texture, 1.25f, parallax, true);
    }

    /// <summary>横向平铺：视差决定水平偏移，<paramref name="centered"/> = 垂直居中。</summary>
    private static void DrawTiled(SpriteBatch spriteBatch, Texture2D texture, float scale, float parallax, bool centered)
    {
        if (scale <= 0f) scale = 1f;
        if (parallax <= 0f) parallax = 0.1f;

        var width = texture.Width * scale;
        if (width <= 0f) return;

        var offset = -(Main.screenPosition.X * parallax) % width;
        var y = centered ? (Main.screenHeight - texture.Height * scale) * 0.5f : 0f;

        var origin = new Il2CppSystem.Nullable<Vector2>(Vector2.Zero);
        var scaleNullable = new Il2CppSystem.Nullable<Vector2>(new Vector2(scale, scale));
        var color = new Il2CppSystem.Nullable<Color>(Color.White);

        for (var x = offset - width; x < Main.screenWidth; x += width)
        {
            var position = new Il2CppSystem.Nullable<Vector2>(new Vector2(x, y));
            spriteBatch.Draw(texture, position, null, null, origin, 0f, scaleNullable, color, SpriteEffects.None, 0f);
        }
    }
}