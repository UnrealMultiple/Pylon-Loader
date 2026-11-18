using Pylon.ModLoader.Content;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Hooks;
/// <summary>在原版环境值计算之后分发修改，避免作者写入被同帧原版计算覆盖。</summary>
internal static class ModEnvironmentHooks
{
    private static bool _installed;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Terraria.Main.ApplyColorOfTheSkiesToTiles += SkyColors;
        On.Terraria.Lighting.UpdateGlobalBrightness += Brightness;
        On.Terraria.Main.PreUpdateAllProjectiles += BeforeProjectiles;
        On.Terraria.Localization.LanguageManager.SetLanguage_GameCulture += LanguageChanged;
    }

    private static void SkyColors(On.Terraria.Main.orig_ApplyColorOfTheSkiesToTiles orig)
    {
        orig();
        if (Main.gameMenu) return;

        var tile = Main.tileColor;
        var background = Main.ColorOfTheSkies;
        SystemLoader.ModifySunLightColor(ref tile, ref background);
        Main.tileColor = tile;
        Main.ColorOfTheSkies = background;
    }

    private static void Brightness(On.Terraria.Lighting.orig_UpdateGlobalBrightness orig)
    {
        orig();
        if (Main.gameMenu) return;

        var scale = 1f;
        SystemLoader.ModifyLightingBrightness(ref scale);
        if (float.IsFinite(scale) && scale >= 0f && scale != 1f)
            Lighting.GlobalBrightness *= scale;
    }

    private static void LanguageChanged(
        On.Terraria.Localization.LanguageManager.orig_SetLanguage_GameCulture orig,
        Terraria.Localization.LanguageManager self, Terraria.Localization.GameCulture culture)
    {
        orig(self, culture);
        if (LoaderHost.Current.Phase != LoaderPhase.Ready) return;
        LocalizationsLoaded();
    }

    internal static void LocalizationsLoaded() => SystemLoader.OnLocalizationsLoaded();

    private static void BeforeProjectiles(On.Terraria.Main.orig_PreUpdateAllProjectiles orig, Main self)
    {
        SystemLoader.PreUpdateProjectiles();
        orig(self);
    }
}
