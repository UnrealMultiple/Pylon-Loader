using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 场景效果管线：每帧把激活的效果按"权重(0..1)+优先级"排序，各槽（水/表层背景/地下背景/音乐/拍图配色/地图背景）
/// 由排序里第一个声明它的效果占住；音乐的落点是 Main.newMusic（要过 BiomeLow 与原版档位两道门），水与背景的落点
/// 在各自己管线的原生消费钩子；SpecialVisuals 每帧派发给每个效果（激活与否都派）。Mod 曲槽的音乐不写 newMusic，
/// 由音乐管线经 MediaPlayer 播放并在播出后把该字段清 0，两个管线不在同一帧互相覆盖。
/// </summary>
internal sealed class SceneEffectPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static SceneEffectPipeline Instance { get; } = new();

    public string Name => "sceneeffect";

    private static IModLog Log => LoaderHost.Current.Log;

    internal static readonly SceneEffectInstance Scene = new();

    internal static ModSceneEffect? WaterEffect { get; private set; }
    internal static ModSceneEffect? SurfaceEffect { get; private set; }
    internal static ModSceneEffect? UndergroundEffect { get; private set; }
    internal static ModSceneEffect? MusicEffect { get; private set; }

    public bool Install()
    {
        On.Terraria.Main.UpdateAudio_DecideOnNewMusic += DecideMusic;
        On.Terraria.Main.UpdateAudio_DecideOnTOWMusic += TowMusic;
        On.Terraria.Player.Update += Update;
        On.GUIMap.DrawMapFullscreenBackground += DrawMapBackground;
        On.Terraria.Main.DrawMapFullscreenBackground += DrawMainMapBackground;

        CaptureBiomePipeline.Install();

        Log.Info("[sceneeffect] 已挂（排序与各槽赢家 / 音乐 / 每帧回调 / 水与背景交给各自管线）");
        return true;
    }

    public void Refresh() => SceneEffectTable.Rebuild();

    internal void Build()
    {
        foreach (var effect in ModContent.GetContent<ModSceneEffect>())
        {
            if (effect is ModBiome) continue;
            effect.SetStaticDefaults();
        }

        if (SceneEffectTable.Count > 0) Log.Info($"[sceneeffect] 安装：{SceneEffectTable.Count} 个场景效果");
    }

    private static readonly List<(float Weight, int Order, ModSceneEffect Effect)> Ranked = [];

    private static readonly Comparison<(float Weight, int Order, ModSceneEffect Effect)> ByWeight =
        (a, b) => b.Weight != a.Weight ? b.Weight.CompareTo(a.Weight) : a.Order.CompareTo(b.Order);

    private static void Update(On.Terraria.Player.orig_Update orig, Player self, int i)
    {
        orig(self, i);

        if (SceneEffectTable.Count == 0) return;
        if (self is null || Main.gameMenu || !self.active || self.whoAmI != Main.myPlayer) return;

        UpdateSceneEffect(self);
    }

    private static void UpdateSceneEffect(Player player)
    {
        var scene = new SceneEffectInstance();
        WaterEffect = SurfaceEffect = UndergroundEffect = MusicEffect = null;
        Ranked.Clear();

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

            try
            {
                effect.SpecialVisuals(player, active);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[sceneeffect] {effect.Name}.SpecialVisuals 异常：{Safe.Describe(e)}");
            }

            if (!active) continue;

            float weight;
            try
            {
                weight = Math.Clamp(effect.GetWeight(player), 0f, 1f) + effect.Priority;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[sceneeffect] {effect.Name}.GetWeight 异常：{Safe.Describe(e)}");
                continue;
            }

            Ranked.Add((weight, index, effect));
        }

        if (Ranked.Count > 0)
        {
            scene.AnyActive = true;
            Ranked.Sort(ByWeight);
            foreach (var (_, _, effect) in Ranked)
            {
                try
                {
                    Fill(scene, effect);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[sceneeffect] {effect.FullKey} (scene) 异常：{Safe.Describe(e)}");
                }
            }
        }

        Scene.AnyActive = scene.AnyActive;
        Scene.WaterStyle = scene.WaterStyle;
        Scene.UndergroundBackground = scene.UndergroundBackground;
        Scene.SurfaceBackground = scene.SurfaceBackground;
        Scene.Music = scene.Music;
        Scene.BiomeTorchItemType = scene.BiomeTorchItemType;
        Scene.BiomeCampfireItemType = scene.BiomeCampfireItemType;
        Scene.MapBackground = scene.MapBackground;
        Scene.MapBackgroundSceneEffect = scene.MapBackgroundSceneEffect;
        Scene.TileColorStyle = scene.TileColorStyle;
    }

    private static void Fill(SceneEffectInstance scene, ModSceneEffect effect)
    {
        var priority = effect.Priority;

        if (scene.WaterStyle.Value == -1 && effect.WaterStyle is { } water)
        {
            scene.WaterStyle = new SceneEffectInstance.PrioritizedPair { Value = water.Slot, Priority = priority };
            WaterEffect = effect;
        }

        if (scene.UndergroundBackground.Value == -1 && effect.UndergroundBackgroundStyle is { } underground)
        {
            scene.UndergroundBackground = new SceneEffectInstance.PrioritizedPair { Value = underground.Slot, Priority = priority };
            UndergroundEffect = effect;
        }

        if (scene.SurfaceBackground.Value == -1 && effect.SurfaceBackgroundStyle is { } surface)
        {
            scene.SurfaceBackground = new SceneEffectInstance.PrioritizedPair { Value = surface.Slot, Priority = priority };
            SurfaceEffect = effect;
        }

        if (scene.Music.Value == -1 && effect.Music != -1)
        {
            scene.Music = new SceneEffectInstance.PrioritizedPair { Value = effect.Music, Priority = priority };
            MusicEffect = effect;
        }

        if (effect is ModBiome biome)
        {
            if (scene.BiomeTorchItemType.Value == -1 && biome.BiomeTorchItemType != -1)
                scene.BiomeTorchItemType = new SceneEffectInstance.PrioritizedPair { Value = biome.BiomeTorchItemType, Priority = priority };
            if (scene.BiomeCampfireItemType.Value == -1 && biome.BiomeCampfireItemType != -1)
                scene.BiomeCampfireItemType = new SceneEffectInstance.PrioritizedPair { Value = biome.BiomeCampfireItemType, Priority = priority };
        }

        if (scene.TileColorStyle == Terraria.Graphics.Capture.CaptureBiome.TileColorStyle.Normal
            && effect.TileColorStyle != Terraria.Graphics.Capture.CaptureBiome.TileColorStyle.Normal)
            scene.TileColorStyle = effect.TileColorStyle;

        if (scene.MapBackground is null && effect.MapBackground is { Length: > 0 } map)
        {
            scene.MapBackground = map;
            scene.MapBackgroundSceneEffect = effect;
        }
    }

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
        Decide();
    }

    private static void Decide()
    {
        try
        {
            if (Local() is not { } player) return;

            if (Main.SceneMetrics.ActiveMusicBox >= 0) return;

            int music = -1;
            int priority = 0;
            FromNpcs(ref music, ref priority);

            var slot = Scene.Music;
            if (slot.Value > -1 && slot.Priority > priority)
            {
                music = slot.Value;
                priority = slot.Priority;
            }

            if (music < 0 || priority < VanillaSceneTier.BiomeLow) return;

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

            if (priority < tier) return;

            if (music >= MusicTable.VanillaCount) return;

            Main.newMusic = music;
        }
        catch (Exception e)
        {
            ContentErrors.Report("[sceneeffect] 音乐决策异常：" + Safe.Describe(e));
        }
    }

    private static void FromNpcs(ref int music, ref int priority)
    {
        var screen = Main.screenPosition;
        const float margin = 5000f;
        var x0 = screen.X - margin;
        var x1 = screen.X + Main.screenWidth + margin;
        var y0 = screen.Y - margin;
        var y1 = screen.Y + Main.screenHeight + margin;
        var npcs = Main.npc;

        for (var index = 0; index < npcs.Length; index++)
        {
            var npc = npcs[index];
            if (npc is null || !npc.active) continue;
            if (ModContent.Find(ContentKind.Npc, npc.type) is not ModNpc modNpc) continue;

            int value;
            try
            {
                value = modNpc.Music;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[sceneeffect] {modNpc.Name}.Music 异常：{Safe.Describe(e)}");
                continue;
            }

            if (value < 0) continue;

            var center = npc.Center;
            if (center.X < x0 || center.X > x1 || center.Y < y0 || center.Y > y1) continue;

            if (music < 0 || modNpc.SceneEffectPriority > priority)
            {
                music = value;
                priority = modNpc.SceneEffectPriority;
            }
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

    private static readonly Dictionary<string, Asset<Texture2D>?> MapTextures = new(StringComparer.Ordinal);

    private static void DrawMapBackground(On.GUIMap.orig_DrawMapFullscreenBackground orig, GUIMap self,
                                          Vector2 screenPosition, int screenWidth, int screenHeight)
    {
        if (!TryDrawMapBackground(screenPosition, screenWidth, screenHeight))
            orig(self, screenPosition, screenWidth, screenHeight);
    }

    private static void DrawMainMapBackground(On.Terraria.Main.orig_DrawMapFullscreenBackground orig,
                                              Vector2 screenPosition, int screenWidth, int screenHeight)
    {
        if (!TryDrawMapBackground(screenPosition, screenWidth, screenHeight))
            orig(screenPosition, screenWidth, screenHeight);
    }

    private static bool TryDrawMapBackground(Vector2 screenPosition, int screenWidth, int screenHeight)
    {
        if (Main.gameMenu || Scene.MapBackground is not { Length: > 0 } path) return false;

        var effect = Scene.MapBackgroundSceneEffect;
        if (effect is null) return false;

        try
        {
            var spriteBatch = Main.spriteBatch;
            if (spriteBatch is null) return false;
            var texture = MapTexture(effect, path);
            if (texture is null || texture.Width <= 0 || texture.Height <= 0) return false;

            var color = !effect.MapBackgroundFullbright && screenPosition.Y <= Main.worldSurface * 16.0
                ? Main.ColorOfTheSkies : Color.White;
            effect.MapBackgroundColor(ref color);
            spriteBatch.Draw(texture, new Rectangle(0, 0, screenWidth, screenHeight), color);
            return true;
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[sceneeffect] {effect.Name}.MapBackground 绘制异常:{Safe.Describe(e)}");
            return false;
        }
    }

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
}
