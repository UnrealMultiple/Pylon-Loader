using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using Terraria.Graphics.Light;

namespace CheatMenu.Features;

/// <summary>
/// 地图高亮。1:1 对应原版 <c>FullBrightTool</c>：
/// hook 两个光照引擎的 ProcessArea 后把 LightMap 的 _colors/_decayColors 整片刷成 1，
/// GetColor 恒定返回 1，Main.DrawBlack 在高亮时跳过。
/// 原版用原生偏移 +0x20/+0x30 直接写数组；这里改用代理字段（同一个数组，语义相同）。
/// </summary>
public sealed class FullBrightFeature : CheatFeature
{
    /// <summary>亮度系数：滑条统一设置 XYZ（1 = 原版全亮白），每次取用都读当前配置。</summary>
    private static Vector3 One
    {
        get
        {
            var v = Math.Clamp(CheatFeatureHost.Config?.FullBrightLevel ?? 1f, 0f, 1f);
            return new Vector3(v, v, v);
        }
    }

    public override string Id => "visual.fullbright";
    public override string Title => "地图高亮";
    public override CheatCategory Category => CheatCategory.Main;
    public override string SubPage => "主要";

    public override void Install()
    {
        On.Terraria.Graphics.Light.LegacyLighting.ProcessArea += LegacyProcess;
        On.Terraria.Graphics.Light.LightingEngine.ProcessArea += EngineProcess;
        On.Terraria.Graphics.Light.LegacyLighting.GetColor += LegacyGetColor;
        On.Terraria.Graphics.Light.LightingEngine.GetColor += EngineGetColor;
        On.Terraria.Main.DrawBlack += DrawBlack;
    }

    private static bool Enabled => CheatFeatureHost.Config is { FullBrightEnabled: true };

    private static void LegacyProcess(On.Terraria.Graphics.Light.LegacyLighting.orig_ProcessArea orig,
        LegacyLighting self, Rectangle area)
    {
        orig(self, area);
        if (!Enabled || self is null) return;
        Fill(self._activeLightMap);
        Fill(self._workingLightMap);
    }

    private static void EngineProcess(On.Terraria.Graphics.Light.LightingEngine.orig_ProcessArea orig,
        LightingEngine self, Rectangle area)
    {
        orig(self, area);
        if (!Enabled || self is null) return;
        Fill(self._activeLightMap);
        Fill(self._workingLightMap);
    }

    private static Vector3 LegacyGetColor(On.Terraria.Graphics.Light.LegacyLighting.orig_GetColor orig,
        LegacyLighting self, int x, int y) => Enabled ? One : orig(self, x, y);

    private static Vector3 EngineGetColor(On.Terraria.Graphics.Light.LightingEngine.orig_GetColor orig,
        LightingEngine self, int x, int y) => Enabled ? One : orig(self, x, y);

    private static void DrawBlack(On.Terraria.Main.orig_DrawBlack orig, Main self, bool force)
    {
        if (Enabled) return;
        orig(self, force);
    }

    private static void Fill(LightMap map)
    {
        if (map is null) return;
        FillArray(map._colors);
        FillArray(map._decayColors);
    }

    private static void FillArray(Il2CppStructArray<Vector3> colors)
    {
        if (colors is null) return;
        colors.AsSpan().Fill(One);
    }

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("地图高亮", "FULL BRIGHT");
        ModernUI.BeginRow();
        ModernUI.RowLabel("地图高亮", "光照图整片刷白，跳过黑暗渐变");
        if (ModernUI.Toggle("##fullbright", cfg.FullBrightEnabled))
        {
            cfg.FullBrightEnabled = !cfg.FullBrightEnabled;
            CheatFeatureHost.MarkDirty();
        }
        ModernUI.EndRow();

        var level = cfg.FullBrightLevel;
        if (ModernUI.RowSlider("##fullbrightlevel", "亮度", "0-100%，1 = 全亮白", ref level, 0f, 1f, "0", "%", 100f))
        {
            cfg.FullBrightLevel = Math.Clamp(level, 0f, 1f);
            CheatFeatureHost.MarkDirty();
        }
        ModernUI.EndCard();
    }
}