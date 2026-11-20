using System.Numerics;
using Pylon.NaviteAPI.ImGui;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>菜单 Tab 0「设置」：外观、主题、输入与交互、关于，对应 HTML 设置页。</summary>
internal static class SettingsPage
{
    private static readonly string[] TouchModes = ["跟随系统", "始终开启", "始终关闭"];

    private static bool _scaleDirty;
    private static bool _wallpaperDirty;

    internal static void Draw()
    {
        var scale = UiSettings.Scale;
        var wallpaper = UiSettings.Wallpaper;
        var touchMode = UiSettings.TouchMode;
        var animations = UiSettings.Animations;
        var showFps = UiSettings.ShowFps;

        ModernUI.PageHead("设置", "APPEARANCE", "界面缩放、壁纸透出与主题外观，设置自动保存到 .pylon/ui.json");

        ModernUI.BeginCard("外观", "DISPLAY");
        if (ModernUI.RowSlider("##scale", "UI 大小比例",
                $"范围 {UiSettings.MinScale:0.00}× – {UiSettings.MaxScale:0.00}×",
                ref scale, UiSettings.MinScale, UiSettings.MaxScale, "×0.00", ""))
        {
            UiSettings.Scale = scale;
            _scaleDirty = true;
        }
        if (ModernUI.RowSlider("##wallpaper", "壁纸透出", "面板不透明度随浓度变化",
                ref wallpaper, UiSettings.MinWallpaper, UiSettings.MaxWallpaper, "0", "%", 100f))
        {
            UiSettings.Wallpaper = wallpaper;
            _wallpaperDirty = true;
        }
        ModernUI.EndCard();

        ModernUI.BeginCard("主题", "THEMES", "点击卡片立即切换");
        ModernUI.ThemeGrid(UiTheme.PaletteCount, ModernUI.ThemeCard);
        ModernUI.EndCard();

        ModernUI.BeginCard("输入与交互", "INPUT");
        ModernUI.BeginRow();
        ModernUI.RowLabel("触屏模式");
        if (ModernUI.ComboField("##touchmode", TouchModes, ref touchMode))
        {
            UiSettings.TouchMode = touchMode;
            UiSettings.Save();
        }
        ModernUI.EndRow();

        ModernUI.BeginRow();
        ModernUI.RowLabel("界面动效");
        if (ModernUI.Toggle("##animations", animations))
        {
            UiSettings.Animations = !animations;
            UiSettings.Save();
        }
        ModernUI.EndRow();

        ModernUI.BeginRow();
        ModernUI.RowLabel("显示 FPS");
        if (ModernUI.Checkbox("覆盖层左上角显示帧率", ref showFps))
        {
            UiSettings.ShowFps = showFps;
            UiSettings.Save();
        }
        ModernUI.EndRow();
        ModernUI.EndCard();

        ModernUI.BeginCard("关于", "ABOUT");
        ModernUI.BeginRow();
        ModernUI.RowLabel("当前版本", "Pylon.ModLoader · GLES 后端");
        ModernUI.InlineText("v0.5.2 · 已是最新", UiTheme.Text2, 14f);
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + TerrariaTheme.Px(14f));
        if (ModernUI.GhostSmall("检查更新")) ModernUI.Toast("当前已是最新版本 v0.5.2");
        ModernUI.EndRow();
        ModernUI.EndCard();

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && (_scaleDirty || _wallpaperDirty))
        {
            UiSettings.Save();
            _scaleDirty = false;
            _wallpaperDirty = false;
        }
    }
}
