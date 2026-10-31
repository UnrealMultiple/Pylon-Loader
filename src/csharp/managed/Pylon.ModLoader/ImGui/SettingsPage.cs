using ImGuiApi = Pylon.ImGui.ImGui;

namespace Pylon.ModLoader.UI;

/// <summary>菜单 Tab 0「设置」：框架自己的界面设置。</summary>
internal static class SettingsPage
{
    internal static void Draw()
    {
        TerrariaUI.SeparatorText("界面设置");

        var scale = UiSettings.Scale;
        if (TerrariaUI.SliderFloat("UI 大小比例", ref scale, UiSettings.MinScale, UiSettings.MaxScale,
                "0.00", "×"))
        {
            // 只改设置，不在帧中改样式：缩放由下一帧开场 ApplyScale 统一应用，
            // 否则同帧混用两套度量会花屏闪烁。
            UiSettings.Scale = scale;
        }
        if (ImGuiApi.IsItemDeactivated()) UiSettings.Save();

        TerrariaUI.DimText("范围 0.75× – 1.5×，设置自动保存到 .pylon/ui.json");
    }
}
