using Pylon.ModLoader.UI;
using Pylon.NaviteAPI.ImGui;
using UnityEngine;

namespace Pylon.ModLoader.ImGuiHost;
/// <summary>
/// 软键盘（TouchScreenKeyboard）的唯一入口。输入框的外观与点击都在 <see cref="TerrariaUI.TextInput"/> 里，
/// </summary>
internal static class ImGuiInputText
{
    private static TouchScreenKeyboard? _keyboard;
    private static uint _editing;
    private static bool _drawn;

    internal static void HandleClick(uint id, bool clicked, string text)
    {
        _drawn = true;
        if (clicked)
        {
            Open(id, text);
            return;
        }
        if (_editing != id && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            Close();
    }

    internal static string? Pull(uint id)
    {
        if (!IsEditing(id)) return null;
        try { return _keyboard!.text; }
        catch (Exception) { return null; }
    }

    internal static bool IsEditing(uint id) => id == _editing && IsKeyboardActive();

    internal static void EndFrame()
    {
        if (_drawn) { _drawn = false; return; }
        if (_editing != 0) Close();
    }

    private static void Open(uint id, string text)
    {
        Close();
        try
        {
            _keyboard = TouchScreenKeyboard.Open(text, TouchScreenKeyboardType.Default,
                false, false, false, false, string.Empty, 0);
            _editing = _keyboard == null ? 0 : id;
        }
        catch (Exception e)
        {
            _keyboard = null;
            _editing = 0;
            ModUI.Logger?.Warn("打开软键盘失败：" + e.Message);
        }
    }

    private static void Close()
    {
        if (_keyboard != null)
        {
            try { _keyboard.active = false; } catch (Exception) { }
        }
        _keyboard = null;
        _editing = 0;
    }

    private static bool IsKeyboardActive()
    {
        try { return _keyboard is { active: true }; }
        catch (Exception) { return false; }
    }
}
