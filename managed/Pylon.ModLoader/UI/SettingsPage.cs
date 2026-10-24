using ImGuiApi = Pylon.ImGui.ImGui;

namespace Pylon.ModLoader.UI
{
    /// <summary>菜单 Tab 0「设置」：框架自己的界面设置。当前只有整体缩放。</summary>
    internal static class SettingsPage
    {
        internal static void Draw()
        {
            var scale = UiSettings.Scale;
            if (ImGuiApi.SliderFloat("UI 大小比例", ref scale, UiSettings.MinScale, UiSettings.MaxScale, "%.2f×"))
            {
                UiSettings.Scale = scale;
                ImGuiDriver.ApplyUiScale(UiSettings.Scale);
            }
            if (ImGuiApi.IsItemDeactivated()) UiSettings.Save();
        }
    }
}
