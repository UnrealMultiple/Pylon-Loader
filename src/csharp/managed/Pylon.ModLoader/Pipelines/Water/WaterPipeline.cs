using Pylon.ModLoader.Tables;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using ReLogic.Content;
using Terraria;
using Terraria.GameContent;

namespace Pylon.ModLoader;

/// <summary>
/// 水样式管线：发号 + 扩表 + 主贴图，并在场景效果选出赢家之后写 <c>Main.waterStyle</c>。
/// 瀑布样式（发号 + 贴图 + 绘制补画）也在这里：瀑布的编号空间、贴图表与绘制入口都挂在
/// <c>WaterfallManager</c> 一处，拆出去反而要多一份跨文件读。
/// </summary>
internal sealed class WaterPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static WaterPipeline Instance { get; } = new();

    public string Name => "water";

    public IReadOnlyList<TableRequest> Tables =>
    [
        new(WaterTables.Style, () => WaterTable.Total),
        new(WaterTables.Waterfall, () => WaterfallTable.Total),
    ];


    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.WaterfallManager.Draw += DrawWaterfalls;
        On.Terraria.Player.GetHairColor += HairColor;
        On.Terraria.WaterfallManager.DrawWaterfall_int_int_int_float_Vector2_RectangleRef_ColorRef_SpriteEffects += TintWaterfall;
        On.Terraria.Dust.dustWater += DustWater;
        On.Terraria.Gore.NewGore += NewGore;
        On.Terraria.GameContent.Drawing.TileDrawing.EmitLiquidDrops += EmitLiquidDrops;
        On.Terraria.Rain.MakeRain += MakeRain;
        On.Terraria.Rain.NewRain += NewRain;
        On.Terraria.Rain.NewRainForced += NewRainForced;
        On.Terraria.Main.DrawRain += DrawRain;
        On.Terraria.Main.CalculateWaterStyle += ChooseWaterStyle;
        On.Terraria.Graphics.Light.LightingEngine.UpdateLightDecay += UpdateLightDecay;
        On.Terraria.Graphics.Light.LegacyLighting.UpdateLightDecay += UpdateLegacyLightDecay;

        Log.Info("[water] 已挂（编号 / 扩表 / 主贴图 / 瀑布贴图与绘制 / 溅水尘与水滴 / 雨 / 光穿透 / 场景赢家改 CalculateWaterStyle 返回值）");
        return true;
    }

    private static int ChooseWaterStyle(On.Terraria.Main.orig_CalculateWaterStyle orig, bool ignoreFountains)
    {
        var vanilla = orig(ignoreFountains);

        var effect = SceneEffectPipeline.Current;
        if (effect is null) return vanilla;

        try
        {
            if (!SceneEffectPipeline.AllowsVisuals(effect)) return vanilla;

            var style = effect.WaterStyle;
            if (style is null || !style.Registered) return vanilla;

            return style.Type;
        }
        catch (Exception e)
        {
            ContentErrors.Report("[water] 场景赢家的 WaterStyle 异常：" + Safe.Describe(e));
            return vanilla;
        }
    }

    [ThreadStatic]
    private static ModWaterStyle? _raining;

    private static void MakeRain(On.Terraria.Rain.orig_MakeRain orig)
    {
        var water = Current();
        if (water is null)
        {
            orig();
            return;
        }

        var saved = Main.waterStyle;
        var outer = _raining;
        Main.waterStyle = 0;
        _raining = water;
        try
        {
            orig();
        }
        finally
        {
            Main.waterStyle = saved;
            _raining = outer;
        }
    }

    private static byte RainVariant(ModWaterStyle water)
    {
        try
        {
            var variant = water.GetRainVariant();
            return variant is >= byte.MinValue and <= byte.MaxValue ? (byte)variant : (byte)0;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[water] {water.Name}.GetRainVariant 异常：{Safe.Describe(e)}");
            return 0;
        }
    }

    private static void DrawRain(On.Terraria.Main.orig_DrawRain orig, Main self)
    {
        var water = Current();
        ReLogic.Content.Asset<Texture2D>? own = null;
        if (water is not null)
        {
            try
            {
                var path = water.GetRainTexture();
                if (!string.IsNullOrEmpty(path) && ModContent.HasAsset(water.Mod, path))
                    own = water.Mod.LoadAsset(path);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[water] {water.Name}.GetRainTexture 异常：{Safe.Describe(e)}");
            }
        }

        if (own is null)
        {
            orig(self);
            return;
        }

        var vanilla = TextureAssets.Rain;
        TextureAssets.Rain = own;
        try
        {
            orig(self);
        }
        finally
        {
            TextureAssets.Rain = vanilla;
        }
    }

    private static Il2CppMicrosoft.Xna.Framework.Vector3? Decay()
    {
        var water = Current();
        if (water is null) return null;

        float r = 0.88f, g = 0.96f, b = 1.015f;
        try
        {
            water.LightColorMultiplier(ref r, ref g, ref b);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[water] {water.Name}.LightColorMultiplier 异常：{Safe.Describe(e)}");
            return null;
        }
        return new Il2CppMicrosoft.Xna.Framework.Vector3(r, g, b);
    }

    private static void UpdateLightDecay(On.Terraria.Graphics.Light.LightingEngine.orig_UpdateLightDecay orig,
                                        Terraria.Graphics.Light.LightingEngine self)
    {
        orig(self);

        if (Decay() is { } decay) self._workingLightMap.LightDecayThroughWater = decay;
    }

    private static void UpdateLegacyLightDecay(On.Terraria.Graphics.Light.LegacyLighting.orig_UpdateLightDecay orig,
                                               Terraria.Graphics.Light.LegacyLighting self)
    {
        orig(self);

        if (Decay() is { } decay) self._workingLightMap.LightDecayThroughWater = decay;
    }

    [ThreadStatic]
    private static bool _dripping;

    private static ModWaterStyle? Current()
    {
        if (Main.gameMenu || WaterTable.Count == 0) return null;
        return WaterTable.Of(Main.waterStyle);
    }

    private static int DustWater(On.Terraria.Dust.orig_dustWater orig)
    {
        var water = Current();
        if (water is null) return orig();

        try
        {
            return water.GetSplashDust();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[water] {water.Name}.GetSplashDust 异常：{Safe.Describe(e)}");
            return orig();
        }
    }

    private static int NewGore(On.Terraria.Gore.orig_NewGore orig, Il2CppMicrosoft.Xna.Framework.Vector2 position,
                              Il2CppMicrosoft.Xna.Framework.Vector2 velocity, int type, float scale)
    {
        if (!_dripping) return orig(position, velocity, type, scale);

        var water = Current();
        if (water is null || type != 705 + Main.waterStyle) return orig(position, velocity, type, scale);

        int drip;
        try
        {
            drip = water.GetDropletGore();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[water] {water.Name}.GetDropletGore 异常：{Safe.Describe(e)}");
            return orig(position, velocity, type, scale);
        }

        if (drip < Terraria.ID.GoreID.Count) return orig(position, velocity, drip, scale);

        var index = orig(position, velocity, Terraria.ID.GoreID.WaterDrip, scale);
        if ((uint)index < (uint)Main.gore.Length)
        {
            var gore = Main.gore[index];
            if (gore is not null) gore.type = drip;
        }
        return index;
    }

    private static void EmitLiquidDrops(On.Terraria.GameContent.Drawing.TileDrawing.orig_EmitLiquidDrops orig,
                                       Terraria.GameContent.Drawing.TileDrawing self, int j, int i, byte liquid,
                                       ushort typeCache)
    {
        var outer = _dripping;
        _dripping = true;
        try
        {
            orig(self, j, i, liquid, typeCache);
        }
        finally
        {
            _dripping = outer;
        }
    }

    private static int NewRain(On.Terraria.Rain.orig_NewRain orig, Il2CppMicrosoft.Xna.Framework.Vector2 position,
                              Il2CppMicrosoft.Xna.Framework.Vector2 velocity)
    {
        var index = orig(position, velocity);

        if (_raining is { } water && Hooks.Overrides(water.GetType(), typeof(ModWaterStyle), nameof(ModWaterStyle.GetRainVariant)))
            SetRainType(index, water);
        return index;
    }

    private static int NewRainForced(On.Terraria.Rain.orig_NewRainForced orig,
                                     Il2CppMicrosoft.Xna.Framework.Vector2 position,
                                     Il2CppMicrosoft.Xna.Framework.Vector2 velocity)
    {
        var index = orig(position, velocity);
        if (Current() is { } water) SetRainType(index, water);
        return index;
    }

    private static void SetRainType(int index, ModWaterStyle water)
    {
        var rain = Main.rain;
        if (rain is null || (uint)index >= (uint)rain.Length) return;

        var drop = rain[index];
        if (drop is not null) drop.type = RainVariant(water);
    }

    public void Refresh()
    {
        WaterTable.Assign();
        WaterfallTable.Assign();

        var falls = WaterfallTable.Total;
        if (falls > WaterfallTable.Base)
        {
            TableMaintenance.Ensure(WaterTables.Waterfall, falls).GrowAll(Log.Info);
        }

        SetupTextures();
    }

    internal void Build()
    {
        var total = WaterTable.Total;
        if (total > 0) TableMaintenance.Ensure(WaterTables.Style, total).GrowAll(Log.Info);

        GrowLiquidRenderer(total);

        var falls = WaterfallTable.Total;
        if (falls > WaterfallTable.Base)
            TableMaintenance.Ensure(WaterTables.Waterfall, falls).GrowAll(Log.Info);

        DrawWatersLimits.Build(Log, WaterTable.Base, WaterTable.Total);

        foreach (var style in ModContent.GetContent<ModWaterStyle>())
        {
            if (!style.Registered) continue;
            style.SetStaticDefaults();
        }

        foreach (var fall in ModContent.GetContent<ModWaterfallStyle>())
        {
            if (!fall.Registered) continue;
            fall.SetStaticDefaults();
        }

        if (WaterTable.Count > 0) Log.Info($"[water] 安装：{WaterTable.Count} 个水样式");
        if (WaterfallTable.Count > 0) Log.Info($"[water] 安装：{WaterfallTable.Count} 个瀑布样式");
    }

    private static void GrowLiquidRenderer(int total)
    {
        try
        {
            var table = Terraria.GameContent.Liquid.LiquidRenderer._liquidTextures;
            if (table is null) return;
            if (ArrayGrow.Grow(ref table, total))
                Terraria.GameContent.Liquid.LiquidRenderer._liquidTextures = table;
        }
        catch (Exception e)
        {
            Log.Warn("[water] LiquidRenderer._liquidTextures 扩容失败：" + Safe.Describe(e));
        }
    }

    internal void SetupTextures()
    {
        var written = 0;

        foreach (var water in ModContent.GetContent<ModWaterStyle>())
        {
            if (!water.Registered) continue;
            if (water.Type < 0) continue;

            try
            {
                var liquid = Terraria.GameContent.Liquid.LiquidRenderer._liquidTextures;
                var blocks = TextureAssets.Liquid;
                var slopes = TextureAssets.LiquidSlope;

                Asset<Texture2D>? main = water.Texture;
                var block = LoadOptional(water, water.BlockTexture) ?? main;
                var slope = LoadOptional(water, water.SlopeTexture) ?? block;

                if (liquid is not null && water.Type < liquid.Length && liquid.Length > 0)
                    liquid[water.Type] = main ?? liquid[0];
                if (blocks is not null && water.Type < blocks.Length && block is not null)
                    blocks[water.Type] = block;
                if (slopes is not null && water.Type < slopes.Length && slope is not null)
                    slopes[water.Type] = slope;

                written++;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[water] {water.Name} 的水贴图槽失败：{Safe.Describe(e)}");
            }
        }

        var falls = 0;
        foreach (var fall in ModContent.GetContent<ModWaterfallStyle>())
        {
            if (!fall.Registered) continue;
            try
            {
                if (WriteWaterfallTexture(fall.Type, fall.Texture)) falls++;
                else Log.Warn($"[water] {fall.Name} 的瀑布贴图没写进 waterfallTexture[{fall.Type}]（表还没建或越界）");
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[water] {fall.Name} 的瀑布贴图失败：{Safe.Describe(e)}");
            }
        }

        if (written > 0) Log.Info($"[water] 水贴图：写 {written}");
        if (falls > 0) Log.Info($"[water] 瀑布贴图：写 {falls}");
    }

    private static Asset<Texture2D>? LoadOptional(ModWaterStyle water, string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            return ModContent.HasAsset(water.Mod, path) ? water.Mod.LoadAsset(path) : null;
        }
        catch (Exception e)
        {
            Log.Warn($"[water] {water.Name} 的附加贴图 {path} 读取失败：{Safe.Describe(e)}");
            return null;
        }
    }

    private static bool WriteWaterfallTexture(int type, Asset<Texture2D>? asset)
    {
        if (asset is null) return false;

        var array = WaterfallManager.waterfallTexture;
        if (array is null || type < 0 || type >= array.Length) return false;

        array[type] = asset;
        return true;
    }

    private static void TintWaterfall(
        On.Terraria.WaterfallManager.orig_DrawWaterfall_int_int_int_float_Vector2_RectangleRef_ColorRef_SpriteEffects orig, WaterfallManager self, int waterfallType, int x,
        int y, float opacity,
        Il2CppMicrosoft.Xna.Framework.Vector2 position, ref Il2CppMicrosoft.Xna.Framework.Rectangle sourceRect,
        ref Il2CppMicrosoft.Xna.Framework.Graphics.Color color,
        Il2CppMicrosoft.Xna.Framework.Graphics.SpriteEffects effects)
    {
        if (WaterfallTable.Of(waterfallType) is { } fall)
        {
            var r = 1f;
            var g = 1f;
            var b = 1f;
            var a = 1f;

            try
            {
                fall.ColorMultiplier(ref r, ref g, ref b, ref a);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[water] " + fall.Name + ".ColorMultiplier 异常：" + Safe.Describe(e));
                r = g = b = a = 1f;
            }

            if (r != 1f || g != 1f || b != 1f || a != 1f)
            {
                color = new Il2CppMicrosoft.Xna.Framework.Graphics.Color(
                    Tint(color.R, r), Tint(color.G, g), Tint(color.B, b), Tint(color.A, a));
            }

            try
            {
                fall.AddLight(x, y);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[water] " + fall.Name + ".AddLight 异常：" + Safe.Describe(e));
            }
        }

        orig(self, waterfallType, x, y, opacity, position, ref sourceRect, ref color, effects);
    }

    private static byte Tint(byte channel, float multiplier)
    {
        var value = channel * multiplier;
        return value <= 0f ? (byte)0 : value >= 255f ? (byte)255 : (byte)value;
    }

    private static Il2CppMicrosoft.Xna.Framework.Graphics.Color HairColor(
        On.Terraria.Player.orig_GetHairColor orig, Player self, bool useLighting)
    {
        var color = orig(self, useLighting);

        if (WaterTable.Of(Main.waterStyle) is not { } water) return color;

        if (!InBiomeOf(self, water)) return color;

        try
        {
            return water.BiomeHairColor();
        }
        catch (Exception e)
        {
            ContentErrors.Report("[water] " + water.Name + ".BiomeHairColor 异常：" + Safe.Describe(e));
            return color;
        }
    }

    private static bool InBiomeOf(Player player, ModWaterStyle water)
    {
        foreach (var biome in ModContent.GetContent<ModBiome>())
        {
            if (!biome.Registered) continue;
            if (!ReferenceEquals(biome.WaterStyle, water)) continue;

            bool active;
            try
            {
                active = biome.IsActive(player);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[water] " + biome.Name + ".IsBiomeActive 异常：" + Safe.Describe(e));
                continue;
            }

            if (active) return true;
        }

        return false;
    }

    private static void DrawWaterfalls(On.Terraria.WaterfallManager.orig_Draw orig, WaterfallManager self,
                                      SpriteBatch spriteBatch)
    {
        orig(self, spriteBatch);

        if (WaterfallTable.Count == 0 || Main.gameMenu || Main.dedServ) return;

        var water = WaterTable.Of(Main.waterStyle);
        if (water is null) return;

        int style;
        try
        {
            style = water.ChooseWaterfallStyle();
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[water] {water.Name}.ChooseWaterfallStyle 异常：{Safe.Describe(e)}");
            return;
        }

        var fall = WaterfallTable.Of(style);
        if (fall is null) return;

        var alpha = StepAlpha(style);
        if (alpha <= 0f) return;

        try
        {
            self.DrawWaterfall(spriteBatch, style, alpha);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[water] 瀑布 {fall.Name} 绘制失败：{Safe.Describe(e)}");
        }
    }

    private static int _fallStyle = -1;
    private static float _fallAlpha;

    private const float FallFadeStep = 0.02f;

    private static float StepAlpha(int style)
    {
        if (style != _fallStyle)
        {
            _fallStyle = style;
            _fallAlpha = 0f;
        }

        if (_fallAlpha < 1f) _fallAlpha = Math.Min(1f, _fallAlpha + FallFadeStep);
        return _fallAlpha;
    }

    internal void Apply(ModSceneEffect? effect, bool allowed)
    {
        if (effect is null || !allowed) return;

        ModWaterStyle? style;
        try
        {
            style = effect.WaterStyle;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[water] {effect.Name}.WaterStyle 异常：{Safe.Describe(e)}");
            return;
        }

        if (style is null) return;

        if (!style.Registered)
        {
            ContentErrors.Report($"[water] {effect.Name} 的 WaterStyle 没注册：{style.Name}");
            return;
        }

        Main.waterStyle = style.Type;
    }
}

/// <summary>水样式与瀑布样式相关的原生表。</summary>
internal static class WaterTables
{
    private static readonly Type[] Types = [typeof(Terraria.GameContent.TextureAssets)];

    private static readonly Type[] FallTypes = [typeof(Terraria.WaterfallManager)];

    internal static readonly TableSet Style = new("水样式", () => Vanilla, Types);

    internal static readonly TableSet Waterfall = new("瀑布贴图", () => WaterfallVanilla, FallTypes);

    private static int _vanilla;

    internal static int Vanilla
    {
        get
        {
            if (_vanilla > 0) return _vanilla;
            var length = TextureSlots.Length(TextureTable.Liquid);
            if (length > 0) _vanilla = length;
            return _vanilla;
        }
    }

    private static int _waterfallVanilla;

    internal static int WaterfallVanilla
    {
        get
        {
            if (_waterfallVanilla > 0) return _waterfallVanilla;
            var array = Terraria.WaterfallManager.waterfallTexture;
            if (array is { Length: > 0 }) _waterfallVanilla = array.Length;
            return _waterfallVanilla;
        }
    }
}
