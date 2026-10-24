using Pylon.ModLoader.Logging;
using Pylon.ModLoader.UI;
using Pylon.NaviteAPI.ImGui;
using System.Numerics;

namespace Pylon.ModLoader.ImGuiHost;

/// <summary>运行日志页：环形缓冲订阅 ModLogger.Captured，等宽终端卡 + 清空按钮。</summary>
internal static class LogPage
{
    private struct Entry
    {
        public string Time;
        public string Tag;
        public string Level;
        public string Src;
        public string Message;
    }

    private const int MaxLines = 200;

    private static readonly object Gate = new();
    private static readonly List<Entry> Lines = [];
    private static bool _hooked;

    internal static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        ModLogger.Captured += OnCaptured;
    }

    internal static void Unhook()
    {
        if (!_hooked) return;
        _hooked = false;
        ModLogger.Captured -= OnCaptured;
        lock (Gate) Lines.Clear();
    }

    internal static void Draw()
    {
        ModernUI.PageHead("运行日志", "RUNTIME LOG", "ImGuiHost 与各 Mod 的实时输出");
        ModernUI.BeginCard("控制台", "CONSOLE", out var clear, "清空");
        if (clear)
        {
            lock (Gate) Lines.Clear();
            ModUI.Logger?.Info("日志已清空");
        }

        var frameSize = new Vector2(ModernUI.CardAvailX, TerrariaTheme.Px(340f));
        var framePos = ImGui.GetCursorScreenPos();
        var frameDl = ImGui.GetWindowDrawList();
        frameDl.AddRectFilled(framePos, framePos + frameSize, TerrariaTheme.U32(UiTheme.Field), TerrariaTheme.Px(12f));
        frameDl.AddRect(framePos, framePos + frameSize, TerrariaTheme.U32(UiTheme.FieldStroke),
            TerrariaTheme.Px(12f), 0, TerrariaTheme.Px(1f));

        UiScroll.Begin("##term", frameSize, new Vector2(TerrariaTheme.Px(14f), TerrariaTheme.Px(12f)));
        int count;
        lock (Gate) count = Lines.Count;
        for (var i = 0; i < count; i++)
        {
            Entry entry;
            lock (Gate) entry = Lines[i];
            ModernUI.TermLine(entry.Time, entry.Tag, entry.Level, entry.Src, entry.Message);
            ImGui.Dummy(new Vector2(0f, TerrariaTheme.Px(6f)));
        }
        if (count == 0) ImGui.Dummy(new Vector2(0f, TerrariaTheme.Px(12f)));
        UiScroll.End();

        ImGui.Dummy(new Vector2(0f, TerrariaTheme.Px(4f)));
        ModernUI.EndCard();
    }

    private static void OnCaptured(string level, string source, string message)
    {
        var entry = new Entry
        {
            Time = DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            Level = level,
            Tag = "[" + level.ToUpperInvariant() + "]",
            Src = source,
            Message = message,
        };
        lock (Gate)
        {
            Lines.Add(entry);
            if (Lines.Count > MaxLines) Lines.RemoveRange(0, Lines.Count - MaxLines);
        }
    }
}
