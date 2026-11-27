using System;
using System.Reflection;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 设置。1:1 对应原版 <c>SettingsTool</c>（设置 Tab）：
/// 整体 UI 缩放（0.70~3.00，立即生效）。
/// 原版的「防录屏」「显示控制台窗口」「启用脚本」是原生 C++ 层能力，Pylon 无对应接口，不迁移。
/// </summary>
public sealed class SettingsFeature : CheatFeature
{
    private static PropertyInfo _pylonScale;

    public override string Id => "settings.page";
    public override string Title => "设置";
    public override CheatCategory Category => CheatCategory.Settings;
    public override string SubPage => "主要";

    public override void DrawMenu()
    {
        ModernUI.BeginCard("界面", "INTERFACE");

        var scale = GetPylonScale();
        if (scale > 0f)
        {
            if (ModernUI.RowSlider("##uiscale", "整体UI缩放", "0.70-3.00", ref scale, 0.70f, 3.00f, "×0.00"))
            {
                SetPylonScale(Math.Clamp(scale, 0.70f, 3.00f));
            }
            ModernUI.DimText("缩放 Pylon 界面的字体与控件尺寸（0.70x ~ 3.00x）");
        }
        else
        {
            ModernUI.DimText("UI 缩放由 Pylon 悬浮球 → 设置页调整");
        }

        ModernUI.EndCard();

        ModernUI.BeginCard("配置", "CONFIG");
        ModernUI.DimText("作弊开关自动保存到本 Mod 的配置文件");
        ModernUI.EndCard();
    }

    /// <summary>Pylon 的 UiSettings.Scale 是 internal；通过反射读写（失败返回 0 表示不可用）。</summary>
    private static float GetPylonScale()
    {
        try
        {
            _pylonScale ??= typeof(ModernUI).Assembly.GetType("Pylon.ModLoader.ImGuiHost.UiSettings")
                ?.GetProperty("Scale", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (_pylonScale is null) return 0f;
            return Convert.ToSingle(_pylonScale.GetValue(null));
        }
        catch
        {
            return 0f;
        }
    }

    private static void SetPylonScale(float value)
    {
        try
        {
            _pylonScale?.SetValue(null, value);
            _pylonScale?.DeclaringType?.GetMethod("Save", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                ?.Invoke(null, null);
        }
        catch
        {
            // 设置页能力缺失就静默跳过
        }
    }
}
