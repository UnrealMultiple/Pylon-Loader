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

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.WaterfallManager.Draw += DrawWaterfalls;
        On.Terraria.Player.GetHairColor += HairColor;
        On.Terraria.WaterfallManager.DrawWaterfall_1 += TintWaterfall;
        On.Terraria.Dust.dustWater += DustWater;
        On.Terraria.Gore.NewGore += NewGore;
        On.Terraria.GameContent.Drawing.TileDrawing.EmitLiquidDrops += EmitLiquidDrops;
        On.Terraria.Rain.MakeRain += MakeRain;
        On.Terraria.Main.DrawRain += DrawRain;
        On.Terraria.Graphics.Light.LightingEngine.UpdateLightDecay += UpdateLightDecay;
        On.Terraria.Graphics.Light.LegacyLighting.UpdateLightDecay += UpdateLegacyLightDecay;

        Log.Info("[water] 已挂（编号 / 扩表 / 主贴图 / 瀑布贴图与绘制 / 溅水尘与水滴 / 雨 / 光穿透 / 赢家写 waterStyle）；斜坡、染发色本版不接");
        return true;
    }

    // ===================== 雨 =====================

    // MakeRain 发雨滴时把 waterStyle 暂时换成 0：原版按 705+style 生成雨滴，mod 水样式
    // 的编号会让 705+style 落到 mod gore 段里（那是水滴不是雨滴）。
    // 记下"正在给 mod 水样式下雨"，NewRain 之后把雨滴 type 换成作者的 GetRainVariant()。
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
        Main.waterStyle = 0;
        _raining = water;
        try
        {
            orig();
        }
        finally
        {
            Main.waterStyle = saved;
            _raining = null;
        }
    }

    /// <summary>作者给的雨滴列号，出界回落到原版 0..2。</summary>
    private static byte RainVariant(ModWaterStyle water)
    {
        try
        {
            var variant = water.GetRainVariant();
            return variant is >= 0 and <= 2 ? (byte)variant : (byte)0;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[water] {water.Name}.GetRainVariant 异常：{Safe.Describe(e)}");
            return 0;
        }
    }

    /// <summary>
    /// 雨的贴图：作者的 png 加载成 Asset 换进 <c>TextureAssets.Rain</c>，原版画完再换回去。
    /// 雨滴列（type）由 <see cref="NewRain"/> 在生成时改。
    /// </summary>
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

    // ===================== 光穿透 =====================

    /// <summary>当前水样式是 mod 的就把水体对光的衰减换成作者的倍率（两套光照引擎都要写）。</summary>
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

    // ===================== 溅水尘与水滴 =====================

    // 标记"正在发水滴"：EmitLiquidDrops 期间 NewGore 里 705+waterStyle 那一粒才是水滴。
    // 水花尘走 Dust.dustWater，不需要这个标记。
    [ThreadStatic]
    private static bool _dripping;

    /// <summary>当前水样式是 mod 的就返回它；原版水（或菜单）返回 null。</summary>
    private static ModWaterStyle? Current()
    {
        if (Main.gameMenu || WaterTable.Count == 0) return null;
        return WaterTable.Of(Main.waterStyle);
    }

    /// <summary>落水溅起的水花尘：作者给什么尘就出什么尘。</summary>
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

    /// <summary>方块滴下的水珠：作者给了自定义 gore 编号就换成它（超表长时走原版编号并改实例）。</summary>
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

        // 超过原版表长（指到另一个 mod 的水滴 gore）：先按原版编号生成，再把实例的 type 改过去。
        // Gore.Update 按 type 找行为，改的是实例字段，之后每帧都按新编号走。
        var index = orig(position, velocity, Terraria.ID.GoreID.WaterDrip, scale);
        if ((uint)index < (uint)Main.gore.Length)
        {
            var gore = Main.gore[index];
            if (gore is not null) gore.type = drip;
        }
        return index;
    }

    /// <summary>只在原版发水滴的调用里放行标记；其它途径的 NewGore（爆炸、掉落物碎裂）不动。</summary>
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

    /// <summary>给 mod 水样式下雨时，新雨滴的贴图列换成作者的变体。</summary>
    private static int NewRain(On.Terraria.Rain.orig_NewRain orig, Il2CppMicrosoft.Xna.Framework.Vector2 position,
                              Il2CppMicrosoft.Xna.Framework.Vector2 velocity)
    {
        var index = orig(position, velocity);

        if (_raining is not { } water) return index;
        if ((uint)index >= (uint)Main.rain.Length) return index;

        var drop = Main.rain[index];
        if (drop is not null) drop.type = RainVariant(water);
        return index;
    }

    public void Refresh()
    {
        WaterTable.Assign();
        WaterfallTable.Assign();

        // 瀑布表在第一轮注册时可能还没建（Base 读到 0，没发号）；这一轮补发之后再补扩一次。
        // 同一份表只建一个扩表器：TableGrower 的构造函数会替换掉同种类的旧实例，重复 new 会把补扩名单清掉。
        var falls = WaterfallTable.Total;
        if (falls > WaterfallTable.Base)
        {
            // 目标长度变了（动态注册又加了瀑布样式）就换一份扩表器：容量是构造时定死的。
            if (_waterfalls is null || _waterfallsCapacity != falls)
            {
                _waterfalls = new TypeTables.TableGrower(TypeTables.TableKind.Waterfall, falls);
                _waterfallsCapacity = falls;
            }

            _waterfalls.NextBatch(falls, Log.Info);
        }

        WriteWaterfallTextures();
    }

    private TypeTables.TableGrower? _waterfalls;
    private int _waterfallsCapacity;

    /// <summary>安装期一次：扩表 + 静态默认值 + 写瀑布贴图。要在写主贴图（Surface 阶段）之前跑。</summary>
    internal void Build()
    {
        var total = WaterTable.Total;
        if (total > 0) new TypeTables.TableGrower(TypeTables.TableKind.WaterStyle, total).NextBatch(total, Log.Info);

        var falls = WaterfallTable.Total;
        if (falls > WaterfallTable.Base)
        {
            // 与 Refresh 共用同一个扩表器：种类相同，TableGrower 的构造函数本来就会替换旧实例，
            // 这里显式复用只是让"谁在盯这张表"只有一份。
            _waterfalls ??= new TypeTables.TableGrower(TypeTables.TableKind.Waterfall, falls);
            _waterfallsCapacity = falls;
            _waterfalls.NextBatch(falls, Log.Info);
        }

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

        WriteWaterfallTextures();
        WriteSlopeTextures();

        if (WaterTable.Count > 0) Log.Info($"[water] 安装：{WaterTable.Count} 个水样式");
        if (WaterfallTable.Count > 0) Log.Info($"[water] 安装：{WaterfallTable.Count} 个瀑布样式");
    }

    // ===================== 瀑布 =====================

    /// <summary>
    /// 瀑布贴图表是 <c>WaterfallManager.waterfallTexture</c>：interop 代理把私有静态字段生成为公开
    /// 属性，直接读写即可；表还没建（LoadContent 之前）就先不写，Refresh 每次重试。
    /// </summary>
    private static void WriteWaterfallTextures()
    {
        if (WaterfallTable.Count == 0) return;

        var written = 0;
        foreach (var fall in ModContent.GetContent<ModWaterfallStyle>())
        {
            if (!fall.Registered) continue;

            try
            {
                if (WriteWaterfallTexture(fall.Type, fall.Texture)) written++;
                else Log.Warn($"[water] {fall.Name} 的瀑布贴图没写进 waterfallTexture[{fall.Type}]（表还没建或越界，下次 Refresh 重试）");
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[water] {fall.Name} 的瀑布贴图失败：{Safe.Describe(e)}");
            }
        }

        if (written > 0) Log.Info($"[water] 瀑布贴图：写 {written}");
    }

    /// <summary>
    /// 斜坡水贴图：作者放了 <c>&lt;类名&gt;_Slope.png</c> 才写 <c>TextureAssets.LiquidSlope</c>（没有这个文件就保持空，
    /// 游戏画斜坡水时用主贴图那一格）。单独写是因为安装步骤每张表只写一次，斜坡是第二条贴图路径。
    /// </summary>
    private static void WriteSlopeTextures()
    {
        var written = 0;

        foreach (var water in ModContent.GetContent<ModWaterStyle>())
        {
            if (!water.Registered) continue;
            if (string.IsNullOrEmpty(water.TexturePath)) continue;

            try
            {
                var asset = water.Mod.LoadAsset(water.TexturePath + "_Slope");

                var table = TextureAssets.LiquidSlope;
                if (table is null || water.Type < 0 || water.Type >= table.Length) continue;

                table[water.Type] = asset;
                written++;
            }
            catch (Exception e)
            {
                // 没有 _Slope 文件是最常见的情况：记一条 warn 就够，斜坡退回主贴图那一格。
                Log.Warn("[water] " + water.Name + " 没有斜坡贴图（" + water.TexturePath + "_Slope）：" + Safe.Describe(e));
            }
        }

        if (written > 0) Log.Info("[water] 斜坡贴图：写 " + written);
    }

    private static bool WriteWaterfallTexture(int type, Asset<Texture2D>? asset)
    {
        if (asset is null) return false;

        // interop 代理把私有静态字段生成为公开属性：直接读写，不用走原生按名查找。
        var array = WaterfallManager.waterfallTexture;
        if (array is null || type < 0 || type >= array.Length) return false;

        array[type] = asset;
        return true;
    }

    /// <summary>
    /// 单个瀑布的绘制：mod 瀑布样式在这里要两样东西——颜色乘子（<c>ColorMultiplier</c>）与加光点（<c>AddLight</c>）。
    /// 这个重载带 <c>ref Color</c> 与格坐标，是唯一能"在画之前改颜色、并知道画在哪"的入口。
    /// 原版瀑布直接放行，一点额外开销都不加。
    /// </summary>
    private static void TintWaterfall(
        On.Terraria.WaterfallManager.orig_DrawWaterfall_1 orig, WaterfallManager self, int waterfallType, int x,
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

    /// <summary>
    /// 群落染发色：原版让"水的样式"决定发色（丛林水把头发染绿），所以 mod 水样式也照这条走——
    /// 当前 <c>Main.waterStyle</c> 是 mod 水样式就返回它的 <see cref="ModWaterStyle.BiomeHairColor"/>。
    /// </summary>
    private static Il2CppMicrosoft.Xna.Framework.Graphics.Color HairColor(
        On.Terraria.Player.orig_GetHairColor orig, Player self, bool useLighting)
    {
        var color = orig(self, useLighting);

        if (WaterTable.Of(Main.waterStyle) is not { } water) return color;

        // 只在"真的站在用这条水的群落里"时才染：tML 的默认值是丛林绿，不设这道闸的话
        // 任何 mod 水样式都会把玩家头发默认染绿。
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

    /// <summary>这个玩家现在站的群落里，有没有哪一个把这条水样式当自己的。</summary>
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

    /// <summary>
    /// 原版只画它认识的那 28 种瀑布；mod 水样式抢到 <c>Main.waterStyle</c> 之后由这里补画：
    /// 按当前水样式声明的瀑布编号画一次。透明度用本管线自己的淡入淡出（原版的 <c>Main.liquidAlpha</c>
    /// 只到原版水样式数，没有 mod 水样式的槽位）：切进这个瀑布时淡入、切走时淡出。
    /// </summary>
    private static void DrawWaterfalls(On.Terraria.WaterfallManager.orig_Draw orig, WaterfallManager self,
                                      SpriteBatch spriteBatch)
    {
        orig(self, spriteBatch);

        if (WaterfallTable.Count == 0 || Main.gameMenu || Main.dedServ) return;

        var water = WaterTable.Of(Main.waterStyle);
        if (water is null) return;   // 原版水：原版绘制已经画了对的瀑布

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
        if (fall is null) return;   // 0（森林）或其它原版编号：原版那次 Draw 已经画了

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

    /// <summary>mod 瀑布的淡入：当前编号的不透明度，切到别的编号就从头淡。</summary>
    private static int _fallStyle = -1;
    private static float _fallAlpha;

    /// <summary>每帧一步（约半秒淡到位）。只有当前样式的瀑布会被画，所以只做淡入，切走后直接归零。</summary>
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

    // ===================== 每帧 =====================

    /// <summary>
    /// 场景效果的赢家声明了水样式就写上；没声明、或这一帧的档位压不过原版（<paramref name="allowed"/> = false）
    /// 就一个字都不动——保持游戏自己算的值。
    /// </summary>
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