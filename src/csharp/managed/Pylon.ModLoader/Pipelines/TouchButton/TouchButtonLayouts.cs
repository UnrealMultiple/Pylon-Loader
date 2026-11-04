using System.IO;
using System.Text.Json.Nodes;
using Terraria;
using ContolType = VirtualControlsHardwareConfigurationMapping_Layout.ContolType;

namespace Pylon.ModLoader;

/// <summary>
/// 触屏按钮的布局：玩家的按钮位置由游戏保存，但游戏不认识 mod 动作。
/// 所以另存一份"哪个槽位放哪个按钮"，并在游戏加载布局之后把它套回去。
/// </summary>
public static class TouchButtonLayouts
{
    private sealed class Row
    {
        public string Slot = "";
        public int Index;
        public string? Action;
        public List<string?> Group = [];
    }

    private static Dictionary<string, List<Row>>? _saved;
    private static List<Action> _restore = [];

    private static string FilePath => Path.Combine(Main.SavePath ?? "", "PylonLoader.touchbuttons.json");

    /// <summary>把保存的位置套回当前布局（游戏加载完布局之后调）。</summary>
    public static void Apply(VirtualControlsStyles_Layout? styles)
    {
        if (styles is null || TouchButtonLoader.Count == 0) return;

        var data = Saved();
        var unbound = TouchButtonLoader.UnboundValue;

        foreach (var profile in Profiles(styles))
        {
            if (!data.TryGetValue(profile.Id ?? "", out var rows) || rows.Count == 0) continue;

            var controls = profile.Controls;
            if (controls is null) continue;

            var bySlot = new Dictionary<string, VirtualControlsBinding_Layout>(StringComparer.Ordinal);
            for (var i = 0; i < controls.Length; i++)
            {
                var binding = controls[i];
                if (binding is not null) bySlot[SlotKey(binding, i)] = binding;
            }

            foreach (var row in rows)
            {
                if (!bySlot.TryGetValue(row.Slot, out var binding))
                {
                    if (row.Index < 0 || row.Index >= controls.Length) continue;

                    binding = controls[row.Index];
                }

                if (binding is null) continue;

                if (TouchButtonLoader.ByKey(row.Action) is { } main && (int)binding.Action == unbound)
                {
                    binding.Action = (ContolType)TouchButtonLoader.ActionOf(main);
                }

                var groups = binding.GroupActions;
                if (groups is null) continue;

                for (var j = 0; j < row.Group.Count && j < groups.Length; j++)
                {
                    if (TouchButtonLoader.ByKey(row.Group[j]) is { } entry && (int)groups[j] == unbound)
                    {
                        groups[j] = (ContolType)TouchButtonLoader.ActionOf(entry);
                    }
                }
            }
        }
    }

    /// <summary>把当前布局里 mod 按钮的位置记下来（游戏保存布局之前调）。</summary>
    public static void Capture(VirtualControlsStyles_Layout? styles)
    {
        if (styles is null || TouchButtonLoader.Count == 0) return;

        var data = Saved();

        foreach (var profile in Profiles(styles))
        {
            var controls = profile.Controls;
            if (controls is null) continue;

            var rows = new List<Row>();

            for (var i = 0; i < controls.Length; i++)
            {
                var binding = controls[i];
                if (binding is null) continue;

                var row = new Row { Slot = SlotKey(binding, i), Index = i };

                if (TouchButtonLoader.ByAction((int)binding.Action) is { } main) row.Action = main.Key;

                var groups = binding.GroupActions;
                if (groups is not null)
                {
                    for (var j = 0; j < groups.Length; j++)
                    {
                        row.Group.Add(TouchButtonLoader.ByAction((int)groups[j])?.Key);
                    }
                }

                if (row.Action is null && row.Group.TrueForAll(k => k is null)) continue;

                rows.Add(row);
            }

            if (rows.Count > 0) data[profile.Id ?? ""] = rows;
            else data.Remove(profile.Id ?? "");
        }

        Write(data);
    }

    internal static List<VirtualControlsProfile_Layout> Profiles(VirtualControlsStyles_Layout? styles)
    {
        var result = new List<VirtualControlsProfile_Layout>();
        if (styles is null) return result;

        var all = styles.Profiles;
        for (var i = 0; all is not null && i < all.Length; i++)
        {
            var profile = all[i];
            if (profile is not null && !result.Contains(profile)) result.Add(profile);
        }

        return result;
    }

    /// <summary>游戏保存/导出分享码时先把 mod 动作临时换成"未绑定"：否则别的设备读不懂。</summary>
    internal static void BeginStrip(IReadOnlyList<VirtualControlsProfile_Layout> profiles)
    {
        _restore = [];

        if (TouchButtonLoader.Count == 0) return;

        var unbound = TouchButtonLoader.UnboundValue;

        foreach (var profile in profiles)
        {
            var controls = profile.Controls;
            if (controls is null) continue;

            for (var i = 0; i < controls.Length; i++)
            {
                var binding = controls[i];
                if (binding is null) continue;

                if (TouchButtonLoader.IsModded((int)binding.Action))
                {
                    var old = binding.Action;
                    _restore.Add(() => binding.Action = old);
                    binding.Action = (ContolType)unbound;
                }

                var groups = binding.GroupActions;
                if (groups is null) continue;

                for (var j = 0; j < groups.Length; j++)
                {
                    if (!TouchButtonLoader.IsModded((int)groups[j])) continue;

                    var index = j;
                    var old = groups[j];
                    _restore.Add(() => groups[index] = old);
                    groups[j] = (ContolType)unbound;
                }
            }
        }
    }

    internal static void EndStrip()
    {
        for (var i = _restore.Count - 1; i >= 0; i--)
        {
            try
            {
                _restore[i]();
            }
            catch (Exception e)
            {
                Safe.Report("touchbutton.unstrip", e);
            }
        }

        _restore = [];
    }

    private static Dictionary<string, List<Row>> Saved()
    {
        if (_saved is not null) return _saved;

        _saved = new Dictionary<string, List<Row>>(StringComparer.Ordinal);

        try
        {
            if (!File.Exists(FilePath)) return _saved;
            if (JsonNode.Parse(File.ReadAllText(FilePath))?["profiles"] is not JsonObject profiles) return _saved;

            foreach (var pair in profiles)
            {
                if (pair.Value is not JsonArray array) continue;

                var rows = new List<Row>();

                foreach (var node in array)
                {
                    if (node is not JsonObject row) continue;

                    var parsed = new Row
                    {
                        Slot = row["slot"]?.GetValue<string>() ?? "",
                        Index = row["index"]?.GetValue<int>() ?? 0,
                        Action = row["action"]?.GetValue<string>(),
                    };

                    if (row["group"] is JsonArray group)
                    {
                        foreach (var item in group) parsed.Group.Add(item?.GetValue<string>());
                    }

                    rows.Add(parsed);
                }

                _saved[pair.Key] = rows;
            }
        }
        catch (Exception e)
        {
            Safe.Report("touchbutton.layouts.read", e);
        }

        return _saved;
    }

    private static void Write(Dictionary<string, List<Row>> data)
    {
        try
        {
            var profiles = new JsonObject();

            foreach (var pair in data)
            {
                var rows = new JsonArray();

                foreach (var row in pair.Value)
                {
                    var group = new JsonArray();
                    foreach (var key in row.Group) group.Add(key);

                    rows.Add(new JsonObject
                    {
                        ["slot"] = row.Slot,
                        ["index"] = row.Index,
                        ["action"] = row.Action,
                        ["group"] = group,
                    });
                }

                profiles[pair.Key] = rows;
            }

            var root = new JsonObject { ["version"] = 1, ["profiles"] = profiles };
            File.WriteAllText(FilePath, root.ToJsonString());
        }
        catch (Exception e)
        {
            Safe.Report("touchbutton.layouts.write", e);
        }
    }

    private static string SlotKey(VirtualControlsBinding_Layout binding, int index) =>
        binding.SlotId is { Length: > 0 } id ? id : "#" + index;
}
