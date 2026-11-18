using Pylon.NaviteAPI.ImGui;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>
/// 菜单 Tab 0「设置」：框架自己的界面设置。
/// </summary>
internal static class SettingsPage
{
    internal static void Draw()
    {
        TerrariaUI.SeparatorText("界面设置");

        var scale = UiSettings.Scale;
        if (TerrariaUI.SliderFloat("UI 大小比例", ref scale, UiSettings.MinScale, UiSettings.MaxScale,
                "0.00", "×"))
        {
            UiSettings.Scale = scale;
        }
        if (ImGui.IsItemDeactivated()) UiSettings.Save();

        TerrariaUI.DimText("范围 0.75× – 1.5×，设置自动保存到 .pylon/ui.json");
    }
}
