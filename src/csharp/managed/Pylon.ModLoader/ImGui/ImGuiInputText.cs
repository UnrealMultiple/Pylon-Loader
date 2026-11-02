using Pylon.ImGui;
using UnityEngine;
using ImGuiApi = Pylon.ImGui.ImGui;

namespace Pylon.ModLoader.UI;

/// <summary>
/// 软键盘（TouchScreenKeyboard）的唯一入口。输入框的外观与点击都在 <see cref="TerrariaUI.TextInput"/> 里，
/// 这里只管两件事：谁的键盘、键盘里现在这一串文本。
///
/// 为什么不用 igInputText 收字：安卓上没有硬件键盘，ImGui 自己收不到字符，只能借系统输入法；
/// 而 ReadOnly 的 InputText 被点中后 ActiveId 不会自己放开，内部还留着光标与横向滚动位置，
/// 外部整串回写文本时它按旧光标算，删字后文字会飘到框中间——两件事都得反着跟它斗。
/// 所以输入框干脆自己画：点一下弹键盘，每帧把键盘里的整串文本镜像进 value，收键盘就结束。
/// （旧实现逐字补 igAddInputCharactersUTF8 / 退格，中文下还会崩。）
///
/// 开键盘只认"用户点了这个框"；收键盘只认"点了别处"或"这个框这一帧没画出来"。
/// 绝不能拿"控件是否 active"当"想输入"的信号，否则用户收起键盘后会被立刻再判成想输入，
/// 键盘弹起→收起→弹起，停不下来。
/// </summary>
internal static class ImGuiInputText
{
    private static TouchScreenKeyboard? _keyboard;
    private static uint _editing;   // 正在编辑的输入框 id；0 = 没有
    private static bool _drawn;     // 本帧画过输入框

    /// <summary>输入框的点击处理：点了它 → 弹键盘（种子是当前文本）；点了别处 → 收起键盘。</summary>
    internal static void HandleClick(uint id, bool clicked, string text)
    {
        _drawn = true;
        if (clicked)
        {
            Open(id, text);
            return;
        }
        if (_editing != id && ImGuiApi.IsMouseClicked(ImGuiMouseButton.Left))
            Close();
    }

    /// <summary>键盘里这一串文本；这个框没在编辑（或用户已收起键盘）返回 null。</summary>
    internal static string? Pull(uint id)
    {
        if (!IsEditing(id)) return null;
        try { return _keyboard!.text; }
        catch (Exception) { return null; }
    }

    /// <summary>这个框正在编辑（键盘确实弹着，用来画高亮）。</summary>
    internal static bool IsEditing(uint id) => id == _editing && IsKeyboardActive();

    /// <summary>一帧画完：这一帧没有输入框被画出来（换页、关窗口、绘制出错），收起键盘。</summary>
    internal static void EndFrame()
    {
        if (_drawn) { _drawn = false; return; }
        if (_editing != 0) Close();
    }

    private static void Open(uint id, string text)
    {
        Close();                        // 同一个框再点一下也走这里：先收掉上一次
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
