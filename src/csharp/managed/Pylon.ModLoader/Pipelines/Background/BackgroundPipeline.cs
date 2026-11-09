using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.Graphics.Effects;

namespace Pylon.ModLoader;

/// <summary>
/// 见 docs/pipeline/content-background.md。
/// </summary>
internal sealed class BackgroundPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static BackgroundPipeline Instance { get; } = new();

    public string Name => "background";

    private static IModLog Log => LoaderHost.Current.Log;

    private sealed class Fill
    {
        public int[] Slots = [];
        public Texture2D?[] Textures = [];
    }

    private static readonly Dictionary<int, Fill?> Fills = new();

    private static bool Step1Pending;
    private static float Step1Top;
    private static int Step1Push;
    private static float[] FadeBuffer = [];

    public bool Install()
    {
        On.Terraria.Main.GetPreferredBGStyleForPlayer += PreferredStyle;
        On.Terraria.Main.UpdateBGVisibility_BackLayer += BackLayer;
        On.Terraria.Main.UpdateBGVisibility_FrontLayer += FrontLayer;
        On.Terraria.Main.DrawSurfaceBG_BackMountainsStep1 += Step1Hook;
        On.Terraria.Main.DrawSurfaceBG_BackMountainsStep2 += Step2Hook;
        On.Terraria.Main.DrawSurfaceBG_GetFogPower += FogPower;

        On.Terraria.Main.DrawBackground_PickUndergroundBackgroundStyle += PickUnderground;
        On.Terraria.Main.get_backTextureValues += BackTextureValues;
        On.Terraria.Main.get_oldBackTextureValues += OldBackTextureValues;

        Log.Info("[background] 已挂（背景号固定 / 远中近三层 / 淡入淡出 / 地下 5 层）");
        return true;
    }

    public void Refresh()
    {
        BackgroundTable.Assign();
        BackgroundTable.AssignUnderground();
    }

    /// <summary>安装期一次：发号 + 静态默认值 + 背景贴图扩表。</summary>
    internal void Build()
    {
        // 先收各 Mod 的背景贴图（ResizeArrays 要用），再发号。
        var mods = LoaderHost.Current.Mods;
        for (var i = 0; i < mods.Count; i++) BackgroundTextureLoader.AutoloadBackgrounds(mods.Mods[i]);

        BackgroundTable.Assign();
        BackgroundTable.AssignUnderground();

        foreach (var style in BackgroundTable.All) style.SetStaticDefaults();
        foreach (var style in BackgroundTable.UndergroundStyles) style.SetStaticDefaults();
        foreach (var global in BackgroundTable.GlobalStyles) global.SetStaticDefaults();

        BackgroundTextureLoader.ResizeArrays();

        if (BackgroundTable.Count > 0) Log.Info($"[background] 安装：{BackgroundTable.Count} 个表层风格");
    }

    // ===================== 每帧：赢家写 bgStyle =====================

    /// <summary>这一帧的表层风格赢家（画淡入淡出时要用）。</summary>
    internal static ModSurfaceBackgroundStyle? CurrentSurface;

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

    // ===================== 背景号 =====================

    private static int PreferredStyle(On.Terraria.Main.orig_GetPreferredBGStyleForPlayer orig)
    {
        var vanilla = orig();
        var style = vanilla;

        try
        {
            style = ChooseSurface(vanilla);
            foreach (var global in BackgroundTable.GlobalStyles) global.ChooseSurfaceBackgroundStyle(ref style);
            if (style >= BackgroundTable.Base || Main.bgStyle >= BackgroundTable.Base) Ensure();
        }
        catch (Exception e)
        {
            ContentErrors.Report("[background] 选表层风格异常：" + Safe.Describe(e));
            return vanilla;
        }

        return style;
    }

    private static int ChooseSurface(int vanilla)
    {
        if (Main.gameMenu) return vanilla;
        return CurrentSurface is { } style ? style.Slot : vanilla;
    }

    private static void Ensure()
    {
        var total = BackgroundTable.TotalCount;

        var front = Main.bgAlphaFrontLayer;
        if (front is null || front.Length < total)
        {
            if (Tables.ArrayGrow.Grow(ref front, total)) Main.bgAlphaFrontLayer = front;
        }

        var far = Main.bgAlphaFarBackLayer;
        if (far is null || far.Length < total)
        {
            if (Tables.ArrayGrow.Grow(ref far, total)) Main.bgAlphaFarBackLayer = far;
        }
    }

    // ===================== 透明度 =====================

    private static void BackLayer(On.Terraria.Main.orig_UpdateBGVisibility_BackLayer orig, Main self,
        Il2CppSystem.Nullable<int> targetBiomeOverride, Il2CppSystem.Nullable<float> transitionAmountOverride)
    {
        // 不读 Il2CppSystem.Nullable.HasValue（真机上会 NRE）；override 参数原样交回原版。
        var style = CurrentSurface?.Slot ?? Main.bgStyle;
        if (BackgroundTable.Get(style) is null)
        {
            orig(self, targetBiomeOverride, transitionAmountOverride);
            return;
        }

        try
        {
            Ensure();

            var fades = Main.bgAlphaFarBackLayer;
            if (fades is null || style >= fades.Length)
            {
                orig(self, targetBiomeOverride, transitionAmountOverride);
                return;
            }

            // 不调 DrawBG_ModifyBGFarBackLayerAlpha（它吃的 Il2CppSystem.Nullable 在真机上会 NRE）；
            // 它做的事就是按过渡速度把这一层的远层透明度推上去、其余压下来。
            var farFades = Main.bgAlphaFarBackLayer!;
            var speed = Main.backgroundLayerTransitionSpeed;
            if (!(speed > 0f)) speed = 0.005f;
            for (var i = 0; i < farFades.Length; i++)
            {
                farFades[i] = i == style
                    ? Math.Min(1f, farFades[i] + speed)
                    : Math.Max(0f, farFades[i] - speed);
            }
        }
        catch (Exception e)
        {
            ContentErrors.Report("[background] 远层可见性异常：" + Safe.Describe(e));
            orig(self, targetBiomeOverride, transitionAmountOverride);
        }
    }

    private static void FrontLayer(On.Terraria.Main.orig_UpdateBGVisibility_FrontLayer orig, Main self,
        Il2CppSystem.Nullable<int> targetBiomeOverride, Il2CppSystem.Nullable<float> transitionAmountOverride)
    {
        orig(self, targetBiomeOverride, transitionAmountOverride);

        // 这一帧的赢家优先（Main.bgStyle 由 Player.Update 里的 Apply 写，可能晚于本钩子）。
        var style = CurrentSurface?.Slot ?? Main.bgStyle;
        if (BackgroundTable.Get(style) is null && BackgroundTable.GlobalStyles.Count == 0) return;

        try
        {
            Ensure();

            var fades = Main.bgAlphaFrontLayer;
            if (fades is null) return;

            var length = fades.Length;
            if (FadeBuffer.Length != length) FadeBuffer = new float[length];
            for (var i = 0; i < length; i++) FadeBuffer[i] = fades[i];

            BackgroundTable.ModifyFarFades(style, FadeBuffer, Main.backgroundLayerTransitionSpeed);

            for (var i = 0; i < length; i++) fades[i] = FadeBuffer[i];
        }
        catch (Exception e)
        {
            ContentErrors.Report("[background] ModifyFarFades 异常：" + Safe.Describe(e));
        }
    }

    // ===================== 远 / 中 / 近 =====================

    private static void Step1Hook(On.Terraria.Main.orig_DrawSurfaceBG_BackMountainsStep1 orig, Main self,
        float backgroundTopMagicNumber, float bgGlobalScaleMultiplier, int pushBGTopHack)
    {
        orig(self, backgroundTopMagicNumber, bgGlobalScaleMultiplier, pushBGTopHack);
        Step1Pending = true;
        Step1Top = backgroundTopMagicNumber;
        Step1Push = pushBGTopHack;
        DrawBack(self, false);
    }

    private static void Step2Hook(On.Terraria.Main.orig_DrawSurfaceBG_BackMountainsStep2 orig, Main self, int pushBGTopHack)
    {
        orig(self, pushBGTopHack);
        DrawBack(self, true);
    }

    private static float FogPower(On.Terraria.Main.orig_DrawSurfaceBG_GetFogPower orig)
    {
        if (Step1Pending)
        {
            Step1Pending = false;
            DrawClose(Main.instance);
        }

        return orig();
    }

    private static void Blit(Texture2D texture, float x, float y, int w, int h, Color color, float scale) =>
        Main.spriteBatch.Draw(texture, new Vector2(x, y), new Il2CppSystem.Nullable<Rectangle>(new Rectangle(0, 0, w, h)),
                              color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);

    private static void DrawBack(Main self, bool middle)
    {
        if (BackgroundTable.All.Count == 0) return;

        var fades = Main.bgAlphaFarBackLayer;
        foreach (var style in BackgroundTable.All)
        {
            var alpha = style.Slot < fades.Length ? fades[style.Slot] : 0f;
            if (!(alpha > 0f)) continue;

            try
            {
                var slot = middle ? style.ChooseMiddleTexture() : style.ChooseFarTexture();
                if (BackgroundTextureLoader.Texture(slot) is not { } texture) continue;

                var color = Main.ColorOfSurfaceBackgroundsBase * alpha;
                Main.ColorOfSurfaceBackgroundsModified = color;

                var w = Main.backgroundWidth[slot];
                var h = Main.backgroundHeight[slot];
                var loops = self.bgLoops;
                var startX = self.bgStartX;
                var step = Main.bgWidthScaled;
                float topY = self.bgTopY;
                var scale = Main.bgScale;

                for (var k = 0; k < loops; k++) Blit(texture, startX + step * k, topY, w, h, color, scale);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[background] {style.Name}.{(middle ? "ChooseMiddleTexture" : "ChooseFarTexture")} 异常：{Safe.Describe(e)}");
            }
        }
    }

    private static void DrawClose(Main self)
    {
        if (BackgroundTable.All.Count == 0) return;

        var fades = Main.bgAlphaFrontLayer;
        foreach (var style in BackgroundTable.All)
        {
            var alpha = style.Slot < fades.Length ? fades[style.Slot] : 0f;
            if (!(alpha > 0f)) continue;

            try
            {
                if (!style.PreDrawCloseBackground(Main.spriteBatch)) continue;

                float scale = 1.25f, a = 1800f, b = 1750f;
                double parallax = 0.37;
                var slot = style.ChooseCloseTexture(ref scale, ref parallax, ref a, ref b);
                if (BackgroundTextureLoader.Texture(slot) is not { } texture) continue;

                var bgScale = scale * 2f;
                var w = Main.backgroundWidth[slot];
                var h = Main.backgroundHeight[slot];
                var width = (int)(w * bgScale);
                if (width <= 0 || !(parallax > 0)) continue;

                SkyManager.Instance.DrawToDepth(Main.spriteBatch, (float)(1.0 / parallax));

                var screen = Main.screenPosition;
                if (screen.Y >= Main.worldSurface * 16.0 + 16.0) continue;

                var x = screen.X * parallax;
                var startX = (int)(-Math.IEEERemainder(x, width) - width / 2);
                var topY = (int)(Step1Top * a + b) + (int)self.scAdj + Step1Push;
                var loops = Main.screenWidth / width + 2;
                var color = Main.ColorOfSurfaceBackgroundsBase * alpha;
                Main.ColorOfSurfaceBackgroundsModified = color;

                for (var k = 0; k < loops; k++) Blit(texture, startX + width * k, topY, w, h, color, bgScale);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[background] {style.Name} 近层异常：{Safe.Describe(e)}");
            }
        }
    }

    // ===================== 地下 =====================

    private static int PickUnderground(On.Terraria.Main.orig_DrawBackground_PickUndergroundBackgroundStyle orig, double magmaLayer)
    {
        var vanilla = orig(magmaLayer);
        lock (Fills) Fills.Clear();

        var style = vanilla;
        try
        {
            if (!Main.gameMenu && CurrentUnderground() is { } underground && Main.BackgroundEnabled)
                style = underground.Slot;

            foreach (var global in BackgroundTable.GlobalStyles) global.ChooseUndergroundBackgroundStyle(ref style);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[background] 选地下风格异常：" + Safe.Describe(e));
            return vanilla;
        }

        return style;
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

    private static Il2CppReferenceArray<Texture2D> BackTextureValues(On.Terraria.Main.orig_get_backTextureValues orig, Main self)
    {
        var values = orig(self);
        Patch(self, false, values);
        return values;
    }

    private static Il2CppReferenceArray<Texture2D> OldBackTextureValues(On.Terraria.Main.orig_get_oldBackTextureValues orig, Main self)
    {
        var values = orig(self);
        Patch(self, true, values);
        return values;
    }

    private static Fill? FillOf(int style, Main self, bool old)
    {
        lock (Fills)
        {
            if (Fills.TryGetValue(style, out var known)) return known;
        }

        Fill? fill = null;
        var mod = BackgroundTable.OfUnderground(style) is not null;
        if (mod || BackgroundTable.GlobalStyles.Count > 0)
        {
            var slots = new[] { -1, -1, -1, -1, 6 };
            if (!mod)
            {
                var numbers = old ? self.oldBackTexture : self.backTexture;
                for (var k = 0; k < 5 && k < numbers.Length; k++) slots[k] = numbers[k];
            }

            BackgroundTable.FillUndergroundTextureArray(style, slots);

            var textures = new Texture2D?[5];
            var any = false;
            for (var k = 0; k < 5; k++)
            {
                textures[k] = BackgroundTextureLoader.Texture(slots[k]);
                any |= textures[k] is not null;
            }

            if (any) fill = new Fill { Slots = slots, Textures = textures };
        }

        lock (Fills) Fills[style] = fill;
        return fill;
    }

    private static void Patch(Main self, bool old, Il2CppReferenceArray<Texture2D> values)
    {
        if (values is null || values.Length < 5) return;

        var style = PickUndergroundStyle(self, old);
        if (style < BackgroundTable.UndergroundBase && BackgroundTable.GlobalStyles.Count == 0) return;

        try
        {
            if (FillOf(style, self, old) is not { } fill) return;

            var numbers = old ? self.oldBackTexture : self.backTexture;
            for (var k = 0; k < 5 && k < values.Length; k++)
            {
                if (fill.Textures[k] is not { } texture) continue;
                values[k] = texture;
                if (k < numbers.Length) numbers[k] = fill.Slots[k];
            }
        }
        catch (Exception e)
        {
            ContentErrors.Report("[background] 地下 FillTextureArray 异常：" + Safe.Describe(e));
        }
    }

    /// <summary>当前地下风格（原版值）——用游戏自己的字段推：backTexture 存在时就是它。</summary>
    private static int PickUndergroundStyle(Main self, bool old)
    {
        try
        {
            return old ? Main.oldUndergroundBackground : Main.undergroundBackground;
        }
        catch
        {
            return -1;
        }
    }
}