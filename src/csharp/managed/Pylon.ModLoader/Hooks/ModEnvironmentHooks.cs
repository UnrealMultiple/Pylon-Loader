using Terraria;

namespace Pylon.ModLoader;

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
        foreach (var mod in LoaderHost.Current.Hooks.Channel(HookId.ModifySunLightColor))
        {
            try
            {
                mod.ModifySunLightColor(ref tile, ref background);
            }
            catch (Exception e)
            {
                Safe.Report(mod.Name + ".ModifySunLightColor", e);
            }
        }
        Main.tileColor = tile;
        Main.ColorOfTheSkies = background;
    }

    private static void Brightness(On.Terraria.Lighting.orig_UpdateGlobalBrightness orig)
    {
        orig();
        if (Main.gameMenu) return;

        var scale = 1f;
        foreach (var mod in LoaderHost.Current.Hooks.Channel(HookId.ModifyLightingBrightness))
        {
            try
            {
                mod.ModifyLightingBrightness(ref scale);
            }
            catch (Exception e)
            {
                Safe.Report(mod.Name + ".ModifyLightingBrightness", e);
            }
        }
        // 非有限或负倍率不能写入全局光照，否则会污染后续所有像素计算。
        if (float.IsFinite(scale) && scale >= 0f && scale != 1f)
            Lighting.GlobalBrightness *= scale;
    }

    private static void LanguageChanged(
        On.Terraria.Localization.LanguageManager.orig_SetLanguage_GameCulture orig,
        Terraria.Localization.LanguageManager self, Terraria.Localization.GameCulture culture)
    {
        orig(self, culture);
        // 启动期语言初始化可能早于内容安装，此时作者还不能安全读取内容表。
        if (LoaderHost.Current.Phase != LoaderPhase.Ready) return;
        LocalizationsLoaded();
    }

    internal static void LocalizationsLoaded()
    {
        foreach (var mod in LoaderHost.Current.Hooks.Channel(HookId.OnLocalizationsLoaded))
        {
            try
            {
                mod.OnLocalizationsLoaded();
            }
            catch (Exception e)
            {
                Safe.Report(mod.Name + ".OnLocalizationsLoaded", e);
            }
        }
    }

    private static void BeforeProjectiles(On.Terraria.Main.orig_PreUpdateAllProjectiles orig, Main self)
    {
        foreach (var mod in LoaderHost.Current.Hooks.Channel(HookId.PreUpdateProjectiles))
        {
            try
            {
                mod.PreUpdateProjectiles();
            }
            catch (Exception e)
            {
                Safe.Report(mod.Name + ".PreUpdateProjectiles", e);
            }
        }
        orig(self);
    }
}
