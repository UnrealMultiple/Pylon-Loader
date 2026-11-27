using Pylon.ModLoader.UI;
using Pylon.NaviteAPI.ImGui;

namespace Pylon.ModLoader.ImGuiHost;

public static class ModOverlay
{
    private sealed class Entry
    {
        public string Key = string.Empty;
        public Action<ImDrawListPtr> Callback = null!;
        public bool Background;
        public bool Failed;
    }

    private static readonly List<Entry> Entries = [];
    private static readonly List<Entry> Snapshot = [];
    private static int _version;
    private static int _snapshotVersion = -1;

    public static void Register(string key, Action<ImDrawListPtr> callback, bool background = true)
    {
        if (string.IsNullOrEmpty(key) || callback is null) return;

        for (var i = 0; i < Entries.Count; i++)
        {
            if (!string.Equals(Entries[i].Key, key, StringComparison.Ordinal)) continue;
            Entries[i].Callback = callback;
            Entries[i].Background = background;
            Entries[i].Failed = false;
            return;
        }

        Entries.Add(new Entry { Key = key, Callback = callback, Background = background });
        _version++;
    }

    public static void Unregister(string key)
    {
        if (string.IsNullOrEmpty(key)) return;
        for (var i = Entries.Count - 1; i >= 0; i--)
        {
            if (!string.Equals(Entries[i].Key, key, StringComparison.Ordinal)) continue;
            Entries.RemoveAt(i);
            _version++;
        }
    }

    internal static void Reset()
    {
        Entries.Clear();
        Snapshot.Clear();
        _version++;
        _snapshotVersion = -1;
    }

    internal static void DrawAll()
    {
        if (Entries.Count == 0) return;

        if (_snapshotVersion != _version)
        {
            _snapshotVersion = _version;
            Snapshot.Clear();
            Snapshot.AddRange(Entries);
        }

        for (var i = 0; i < Snapshot.Count; i++)
        {
            var entry = Snapshot[i];
            if (entry.Failed) continue;

            try
            {
                var list = entry.Background ? ImGui.GetBackgroundDrawList() : ImGui.GetForegroundDrawList();
                entry.Callback(list);
            }
            catch (Exception e)
            {
                entry.Failed = true;
                ModUI.Logger?.Warn($"[overlay] {entry.Key} 常驻绘制出错，已停用：" + Flatten(e));
            }
        }
    }

    private static string Flatten(Exception e)
    {
        var text = e.GetType().Name + ": " + e.Message;
        var inner = e.InnerException;
        for (var depth = 0; inner is not null && depth < 3; depth++, inner = inner.InnerException)
            text += " <- " + inner.GetType().Name + ": " + inner.Message;
        return text;
    }
}