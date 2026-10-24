using System.Text.Json;
using Pylon.ModLoader.UI;
using Terraria;

namespace Pylon.ModLoader.Effects;

/// <summary>设置 → 通用 里的"屏幕效果 开/关"一行；选择存进 <c>PylonLoader.screen.json</c>。</summary>
internal static class ScreenEffectsSetting
{
    private const int SettingId = 193;

    private static GUIOpenUIButtonSetting? _entry;
    private static bool _restored;
    private static bool _installed;

    private static string FilePath => Path.Combine(Main.SavePath ?? "", "PylonLoader.screen.json");

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        try
        {
            On.GUISettingsOverlay.SetCategory += SetCategoryPostfix;
            On.GUIOpenUIButtonSetting.GetOption += GetOptionPostfix;
            On.GUIOpenUIButtonSetting.GetButtonCaption += GetButtonCaptionPostfix;
            On.GUIOpenUIButtonSetting.OpenUI += OpenUI;
        }
        catch (Exception e)
        {
            _installed = false;
            ScreenReport.Report("ScreenEffectsSetting.Install", e);
        }
    }

    internal static void Restore()
    {
        if (_restored) return;
        _restored = true;
        string file;
        try { file = FilePath; }
        catch (Exception)
        {
            _restored = false;
            return;
        }
        if (!System.IO.File.Exists(file)) return;
        try
        {
            using JsonDocument json = JsonDocument.Parse(System.IO.File.ReadAllText(file));
            if (json.RootElement.TryGetProperty("Enabled", out JsonElement enabled) &&
                enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)
                ScreenEffects.Enabled = enabled.GetBoolean();
        }
        catch (Exception e)
        {
            ModUI.Logger?.Error($"ScreenEffects: {file} 已损坏（{e.Message}），效果保持开启");
        }
    }

    internal static void Set(bool enabled)
    {
        _restored = true;
        ScreenEffects.Enabled = enabled;
        try { System.IO.File.WriteAllText(FilePath, JsonSerializer.Serialize(new { Enabled = enabled })); }
        catch (Exception e) { ScreenReport.Report("ScreenEffectsSetting (save)", e); }
    }

    private static bool Ours(GUIOpenUIButtonSetting self) => _entry != null && self.Pointer == _entry.Pointer;

    private static void SetCategoryPostfix(On.GUISettingsOverlay.orig_SetCategory orig, GUISettingsOverlay self,
        SettingsOverlayOptionCategories_Layout.Category category)
    {
        orig(self, category);
        try { AddEntry(self); }
        catch (Exception e) { ScreenReport.Report("ScreenEffectsSetting (row)", e); }
    }

    private static void AddEntry(GUISettingsOverlay overlay)
    {
        if (!Main.gameMenu || overlay.Selected != SettingsOverlayOptionCategories_Layout.Category.General) return;
        var list = overlay._categorySettings;
        if (list == null) return;
        Restore();
        _entry ??= new GUIOpenUIButtonSetting((GUISettingID)SettingId)
        {
            _category = SettingsOverlayOptionCategories_Layout.Category.General,
        };
        if (!list.Contains(_entry)) list.Insert(0, _entry);
    }

    private static string GetOptionPostfix(On.GUIOpenUIButtonSetting.orig_GetOption orig, GUIOpenUIButtonSetting self) =>
        Ours(self) ? "屏幕效果:" : orig(self);

    private static string GetButtonCaptionPostfix(On.GUIOpenUIButtonSetting.orig_GetButtonCaption orig, GUIOpenUIButtonSetting self) =>
        Ours(self) ? (ScreenEffects.Enabled ? "开" : "关") : orig(self);

    private static void OpenUI(On.GUIOpenUIButtonSetting.orig_OpenUI orig, GUIOpenUIButtonSetting self)
    {
        if (!Ours(self))
        {
            orig(self);
            return;
        }
        Set(!ScreenEffects.Enabled);
    }
}