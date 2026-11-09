using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 场景效果管线：每帧在作者效果之间选一个赢家（优先级 + 权重），赢了就写 `Main.newMusic`；
/// 顺便把 `SpecialVisuals` 每帧分派给每个效果；水与背景交给各自管线，全屏地图背景由本管线画。
/// </summary>
internal sealed class SceneEffectPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static SceneEffectPipeline Instance { get; } = new();

    public string Name => "sceneeffect";

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>这一帧的赢家（没有就是 null）；水样式等"赢家决定的表现"从这里取。</summary>
    internal static ModSceneEffect? Current { get; private set; }

    public bool Install()
    {
        On.Terraria.Main.UpdateAudio_DecideOnNewMusic += DecideMusic;
        On.Terraria.Main.UpdateAudio_DecideOnTOWMusic += TowMusic;
        On.Terraria.Player.Update += Update;
        On.GUIMap.DrawMapFullscreenBackground += DrawMapBackground;

        // 拍图与全屏地图的群系：赢家换了 mod 水或背景时也要跟着换，不然图上是原版的水色。
        CaptureBiomePipeline.Install();

        Log.Info("[sceneeffect] 已挂（选赢家 / 音乐 / 每帧回调 / 水与背景交给各自管线）");
        return true;
    }

    public void Refresh() => SceneEffectTable.Rebuild();

    /// <summary>安装期一次：普通场景效果的静态默认值（群落的归生物群系管线）。</summary>
    internal void Build()
    {
        foreach (var effect in ModContent.GetContent<ModSceneEffect>())
        {
            if (effect is ModBiome) continue;
            effect.SetStaticDefaults();
        }

        if (SceneEffectTable.Count > 0) Log.Info($"[sceneeffect] 安装：{SceneEffectTable.Count} 个场景效果");
    }

    // ===================== 音乐 =====================

    /// <summary>
    /// 压过原版音乐现在按档算：<see cref="VanillaSceneTier.Tier"/> 给出原版这一帧的场景档（1..9），
    /// 作者的 <c>Priority</c> 达到同一档才准覆盖。<see cref="VanillaMusicPriority"/> 保留作"无条件压过原版"的档位。
    /// </summary>
    public const int VanillaMusicPriority = 100;

    /// <summary>这一帧走的是不是"另一个世界"音乐；原版两条决策路径各发一次，我们只记个标记。</summary>
    private static bool _otherworld;

    private static void TowMusic(On.Terraria.Main.orig_UpdateAudio_DecideOnTOWMusic orig, Main self)
    {
        orig(self);
        _otherworld = true;
    }

    private static void DecideMusic(On.Terraria.Main.orig_UpdateAudio_DecideOnNewMusic orig, Main self)
    {
        _otherworld = false;
        orig(self);

        // orig 之后 Main.newMusic 就是"原版这一帧的决定"：非 0 说明原版有话说（事件 / 天气 / Boss）。
        var vanilla = Main.newMusic;

        if (Local() is not { } player) return;

        var winner = Winner(player);
        if (winner < 0) return;

        var effect = SceneEffectTable.At(winner);
        var music = effect.Music;
        if (music < 0) return;

        if (vanilla != 0 && vanilla != music)
        {
            // 原版有话说：作者至少要达到原版这一帧的档；100 以上无条件压过。
            if (effect.Priority >= VanillaMusicPriority) { }
            else
            {
                int tier;
                try
                {
                    tier = VanillaSceneTier.Tier(_otherworld);
                }
                catch (Exception e)
                {
                    ContentErrors.Report("[sceneeffect] 原版场景档计算异常：" + Safe.Describe(e));
                    tier = VanillaSceneTier.BossHigh;
                }

                if (effect.Priority < tier) return;
            }
        }

        Main.newMusic = music;
    }

    /// <summary>选赢家：优先级高者胜，同优先级比权重，全平手时先注册的赢（严格大于才替换）。</summary>
    private static int Winner(Player player)
    {
        var best = -1;
        var bestPriority = 0;
        var bestWeight = 0f;

        for (var index = 0; index < SceneEffectTable.Count; index++)
        {
            var effect = SceneEffectTable.At(index);
            if (effect is null) continue;

            bool active;
            try
            {
                active = effect.IsSceneEffectActive(player);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[sceneeffect] {effect.Name}.IsSceneEffectActive 异常：{Safe.Describe(e)}");
                active = false;
            }

            if (!active) continue;

            var priority = effect.Priority;
            var weight = Weight(effect, player);

            if (best >= 0 && priority < bestPriority) continue;
            if (best >= 0 && priority == bestPriority && weight <= bestWeight) continue;

            best = index;
            bestPriority = priority;
            bestWeight = weight;
        }

        return best;
    }

    /// <summary>
    /// 水/背景这一帧准不准换：赢家声明了视觉（水或表层背景）才需要比档；
    /// 档位与音乐同尺——≥ 100 无条件，否则要 ≥ 原版这一帧的场景档（<see cref="VanillaSceneTier.Tier"/>）。
    /// 视觉不分"另一个世界"那条路径，固定按普通世界算。
    /// </summary>
    internal static bool AllowsVisuals(ModSceneEffect effect)
    {
        bool hasVisuals;
        int priority;
        try
        {
            hasVisuals = effect.WaterStyle is not null || effect.SurfaceBackgroundStyle is not null;
            priority = effect.Priority;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[sceneeffect] {effect.Name}.WaterStyle/Priority 异常：{Safe.Describe(e)}");
            return false;
        }

        if (!hasVisuals) return true;
        if (priority >= VanillaMusicPriority) return true;

        int tier;
        try
        {
            tier = VanillaSceneTier.Tier(false);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[sceneeffect] 原版场景档计算异常：" + Safe.Describe(e));

            // 算不出来就放行：宁可让 mod 的背景生效，也不要因为一次异常让作者的效果永远不出现。
            return true;
        }

        return priority >= tier;
    }

    private static float Weight(ModSceneEffect effect, Player player)
    {
        try
        {
            return effect.GetWeight(player);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[sceneeffect] {effect.Name}.GetWeight 异常：{Safe.Describe(e)}");
            return 0f;
        }
    }

    private static Player? Local()
    {
        if (Main.gameMenu) return null;

        var who = Main.myPlayer;
        var players = Main.player;
        if (players is null || (uint)who >= (uint)players.Length) return null;

        var player = players[who];
        return player is null || !player.active ? null : player;
    }

    // ===================== 每帧回调 =====================

    // ===================== 全屏地图背景 =====================

    private static readonly Dictionary<string, Asset<Texture2D>?> MapTextures = new(StringComparer.Ordinal);

    /// <summary>原版把地图背景画完之后，赢家声明了 <c>MapBackground</c> 就盖一层自己的。</summary>
    private static void DrawMapBackground(On.GUIMap.orig_DrawMapFullscreenBackground orig, GUIMap self,
                                          Vector2 screenPosition, int screenWidth, int screenHeight)
    {
        orig(self, screenPosition, screenWidth, screenHeight);

        if (Current is not { } effect) return;

        string path;
        try
        {
            path = effect.MapBackground;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[sceneeffect] {effect.Name}.MapBackground 异常：{Safe.Describe(e)}");
            return;
        }

        if (string.IsNullOrEmpty(path)) return;

        var spriteBatch = Main.spriteBatch;
        if (spriteBatch is null) return;

        var texture = MapTexture(effect, path);
        if (texture is null) return;

        if (texture.Width <= 0 || texture.Height <= 0) return;

        var scale = Math.Max(screenWidth / (float)texture.Width, screenHeight / (float)texture.Height);

        var color = Color.White;
        try
        {
            effect.MapBackgroundColor(ref color);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[sceneeffect] {effect.Name}.MapBackgroundColor 异常：{Safe.Describe(e)}");
            color = Color.White;
        }

        var origin = new Il2CppSystem.Nullable<Vector2>(Vector2.Zero);
        var position = new Il2CppSystem.Nullable<Vector2>(new Vector2(screenPosition.X, screenPosition.Y));
        var scaleNullable = new Il2CppSystem.Nullable<Vector2>(new Vector2(scale, scale));
        var colorNullable = new Il2CppSystem.Nullable<Color>(color);

        spriteBatch.Draw(texture, position, null, null, origin, 0f, scaleNullable, colorNullable, SpriteEffects.None, 0f);
    }

    /// <summary>按贴图路径缓存资源；加载失败只记一次，之后当没有。</summary>
    private static Texture2D? MapTexture(ModSceneEffect effect, string path)
    {
        if (MapTextures.TryGetValue(path, out var cached)) return cached?.Value;

        Asset<Texture2D>? asset = null;
        try
        {
            asset = effect.Mod?.LoadAsset(path);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[sceneeffect] {effect.Name} 的地图背景加载失败（{path}）：{Safe.Describe(e)}");
        }

        MapTextures[path] = asset;

        try
        {
            return asset?.Value;
        }
        catch
        {
            return null;
        }
    }

    private void Update(On.Terraria.Player.orig_Update orig, Player self, int i)
    {
        orig(self, i);

        if (SceneEffectTable.Count == 0) return;
        if (self is null || Main.gameMenu || !self.active || self.whoAmI != Main.myPlayer) return;

        var winner = Winner(self);
        Current = winner >= 0 ? SceneEffectTable.At(winner) : null;

        // 水与背景：赢家要压得过原版这一帧的档位才准换（与音乐同尺）。压不过就不写，让原版的值留着。
        var allowed = Current is { } visual && AllowsVisuals(visual);
        WaterPipeline.Instance.Apply(Current, allowed);
        BackgroundPipeline.Instance.Apply(Current, allowed);

        for (var index = 0; index < SceneEffectTable.Count; index++)
        {
            var effect = SceneEffectTable.At(index);
            if (effect is null) continue;

            bool active;
            try
            {
                active = effect.IsSceneEffectActive(self);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[sceneeffect] {effect.Name}.IsSceneEffectActive 异常：{Safe.Describe(e)}");
                active = false;
            }

            try
            {
                effect.SpecialVisuals(self, active);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[sceneeffect] {effect.Name}.SpecialVisuals 异常：{Safe.Describe(e)}");
            }
        }
    }
}
