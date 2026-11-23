using Pylon.ModLoader.Content;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.Localization;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 原版设置里的「菜单主题」行：玩家在 设置 → 通用 点一下就循环到下一个可用主题（含常驻的原版项，选择即落盘）。
/// 行本体是原版的开 UI 按钮设置项，在原版装完某一页设置列表之后插进去，只在主菜单的通用页插；
/// 行标题、当前主题名（按按钮宽度截断）与点击行为分别接管自这一行自己的 GetOption / GetButtonCaption / OpenUI，
/// 其余同名设置行照走原逻辑，所以不用改任何原版数据表。
/// </summary>
internal static class MenuSettingsRow
{
    private const float CaptionPadding = 16;
    private const int PortugueseCulture = 8;

    private static GUIOpenUIButtonSetting? _entry;

    internal static void Install()
    {
        On.GUISettingsOverlay.SetCategory += SetCategory;
        On.GUIOpenUIButtonSetting.GetOption += GetOption;
        On.GUIOpenUIButtonSetting.GetButtonCaption += GetButtonCaption;
        On.GUIOpenUIButtonSetting.OpenUI += OpenUI;
    }

    private static void SetCategory(On.GUISettingsOverlay.orig_SetCategory orig, GUISettingsOverlay self,
        SettingsOverlayOptionCategories_Layout.Category category)
    {
        orig(self, category);
        try { AddEntry(self); }
        catch (Exception e)
        {
            ContentErrors.Line("[menu] 设置行插入失败：" + Safe.Dump(e));
        }
    }

    private static void AddEntry(GUISettingsOverlay overlay)
    {
        if (!Main.gameMenu || overlay.Selected != SettingsOverlayOptionCategories_Layout.Category.General || MenuTable.Count == 0) return;

        var list = overlay._categorySettings;
        if (list is null) return;

        var entry = _entry;
        if (entry is null)
        {
            entry = new GUIOpenUIButtonSetting(GUISettingID.None);
            entry._category = SettingsOverlayOptionCategories_Layout.Category.General;
            _entry = entry;
        }

        if (list.IndexOf(entry) >= 0) return;

        var at = 0;
        for (var k = 0; k < list.Count; k++)
        {
            var item = list[k];
            if (item is null || item.Pointer == entry.Pointer) continue;
            if (item.TryCast<GUIOpenUIButtonSetting>() is not { } button || button._id != GUISettingID.None) continue;
            at = k + 1;
            break;
        }

        list.Insert(at, entry);
    }

    private static string GetOption(On.GUIOpenUIButtonSetting.orig_GetOption orig, GUIOpenUIButtonSetting self)
    {
        if (self is not null && _entry is not null && self.Pointer == _entry.Pointer)
        {
            try { return Label(); }
            catch (Exception e)
            {
                ContentErrors.Line("[menu] 设置行标题异常：" + Safe.Dump(e));
            }
        }
        return orig(self);
    }

    private static string GetButtonCaption(On.GUIOpenUIButtonSetting.orig_GetButtonCaption orig, GUIOpenUIButtonSetting self)
    {
        if (self is not null && _entry is not null && self.Pointer == _entry.Pointer)
        {
            try { return Caption(_entry); }
            catch (Exception e)
            {
                ContentErrors.Line("[menu] 设置行文案异常：" + Safe.Dump(e));
            }
        }
        return orig(self);
    }

    private static void OpenUI(On.GUIOpenUIButtonSetting.orig_OpenUI orig, GUIOpenUIButtonSetting self)
    {
        if (self is not null && _entry is not null && self.Pointer == _entry.Pointer)
        {
            try { MenuPipeline.Next(); }
            catch (Exception e)
            {
                ContentErrors.Line("[menu] 设置行切换主题异常：" + Safe.Dump(e));
            }
            return;
        }
        orig(self);
    }

    private static string Label()
    {
        try
        {
            return LanguageManager.Instance.ActiveCulture.LegacyId == PortugueseCulture ? "Tema do Menu:" : "Menu Theme:";
        }
        catch (Exception)
        {
            return "Menu Theme:";
        }
    }

    private static string Caption(GUIOpenUIButtonSetting entry)
    {
        var name = CurrentName();
        try
        {
            var caption = entry.GetLayout().Caption;
            var label = caption.Label;
            var font = label.GetFont();
            var width = caption.overloadedSize.X - CaptionPadding;
            if (font is null || width <= 0) return name;
            float Measure(string text) => font.MeasureString(text).X * label.Scale;
            if (Measure(name) <= width) return name;
            var cut = name;
            while (cut.Length > 1 && Measure(cut + "...") > width) cut = cut[..^1];
            return cut + "...";
        }
        catch (Exception e)
        {
            ContentErrors.Line("[menu] 设置行文案测宽异常：" + Safe.Dump(e));
            return name;
        }
    }

    private static string CurrentName()
    {
        if (MenuPipeline.Current is not { } menu) return "Terraria";
        try { return menu.DisplayName; }
        catch (Exception e)
        {
            ContentErrors.Line($"[menu] {menu.Name}.DisplayName 异常：{Safe.Dump(e)}");
            return menu.Name;
        }
    }
}
