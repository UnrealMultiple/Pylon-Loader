// 层：框架 / 触屏按钮 —— 布局持久化。
//
// 游戏的控制文件永远不带 mod 动作（保存时变空槽、立刻又回来）：mod 按钮单独存
// PylonLoader.touchbuttons.json，按 'mod/Name' 和槽位。退出的 mod 的按钮留在槽里，mod 回来时补上。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Terraria;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

using Pylon.ModLoader.UI;

namespace Pylon.ModLoader
{
    /// <summary>触屏按钮的布局持久化。</summary>
    public static class TouchButtonLayouts
    {
        private const string FileName = "PylonLoader.touchbuttons.json";
        private static SavedData _saved;

        // 保存剥离栈：Save(pid)/Share(prefix) 会往里压，对应的 postfix 出栈
        private static readonly Stack<Action> UndoStack = new();

        private sealed class SavedData
        {
            public int version = 1;
            public Dictionary<string, List<RowData>> profiles = new(StringComparer.Ordinal);
        }

        private sealed class RowData
        {
            public string slot { get; set; }
            public int index { get; set; }
            public string action { get; set; }
            public List<string> group { get; set; }
        }

        /// <summary>把保存的位置套回当前布局（在布局加载完 / 内容就绪后）。</summary>
        public static void Apply(VirtualControlsStyles_Layout styles)
        {
            if (styles == null || TouchButtonLoader.Count == 0) return;

            var data = Saved();
            var unbound = TouchButtonLoader.UnboundValue();
            var profiles = Profiles(styles);
            foreach (var profile in profiles)
            {
                if (profile == null) continue;
                var id = ProfileId(profile);
                var rows = data.profiles.TryGetValue(id, out var list) && list != null && list.Count > 0 ? list : null;
                if (rows == null) continue;
                var controls = TouchButtonLoader.SafeRead(() => profile.Controls);
                if (controls == null) continue;

                var bySlot = new Dictionary<string, VirtualControlsBinding_Layout>(StringComparer.Ordinal);
                for (int i = 0; i < controls.Length; i++)
                {
                    var b = controls[i];
                    if (b != null) bySlot[SlotKey(b, i)] = b;
                }
                foreach (var row in rows)
                {
                    var binding = bySlot.TryGetValue(row.slot ?? "", out var found)
                        ? found
                        : (row.index < controls.Length ? controls[row.index] : null);
                    if (binding == null) continue;
                    var main = row.action != null ? TouchButtonLoader.ByKey(row.action) : null;
                    if (main != null && ActionOf(binding) == unbound) SetAction(binding, (VirtualControlsHardwareConfigurationMapping_Layout.ContolType)TouchButtonLoader.ActionOf(main));
                    var groups = TouchButtonLoader.SafeGroups(binding);
                    if (groups == null || row.group == null) continue;
                    for (int j = 0; j < row.group.Count && j < groups.Length; j++)
                    {
                        var key = row.group[j];
                        var entry = key != null ? TouchButtonLoader.ByKey(key) : null;
                        if (entry != null && (int)groups[j] == unbound) groups[j] = (VirtualControlsHardwareConfigurationMapping_Layout.ContolType)TouchButtonLoader.ActionOf(entry);
                    }
                }
            }
        }


        /// <summary>读 / 初始化保存文件。</summary>
        private static SavedData Saved()
        {
            if (_saved != null) return _saved;
            var file = FilePath();
            SavedData data = null;
            try
            {
                if (File.Exists(file))
                {
                    using var stream = File.OpenRead(file);
                    data = JsonSerializer.Deserialize<SavedData>(stream);
                }
            }
            catch (Exception e)
            {
                TouchButtonLoader.Log("TouchButtons: " + file + " 坏了（" + e.Message + "）；布局里先不放 mod 按钮");
            }
            _saved = data != null && data.profiles != null ? data : new SavedData();
            return _saved;
        }

        private static string FilePath()
        {
            try { return Path.Combine(Main.SavePath, FileName); }
            catch { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FileName); }
        }

        /// <summary>保存前抓取 mod 按钮的位置（写进 json）。</summary>
        public static void Capture(VirtualControlsStyles_Layout styles)
        {
            var unbound = TouchButtonLoader.UnboundValue();
            var oldProfiles = Saved().profiles;
            string KeyOf(int action)
            {
                var e = TouchButtonLoader.ByAction(action);
                return e != null ? e.Key : null;
            }
            // 游戏里是空、但藏着退出的 mod 的按钮：留着
            string Kept(int action, string previous)
            {
                if (action != unbound || previous == null || TouchButtonLoader.ByKey(previous) != null) return null;
                return previous;
            }

            var profiles = new Dictionary<string, List<RowData>>(StringComparer.Ordinal);
            var live = new HashSet<string>(StringComparer.Ordinal);
            foreach (var profile in Profiles(styles))
            {
                if (profile == null) continue;
                var id = ProfileId(profile);
                if (string.IsNullOrEmpty(id)) continue;
                live.Add(id);
                var before = new Dictionary<string, RowData>(StringComparer.Ordinal);
                if (oldProfiles.TryGetValue(id, out var oldRows))
                {
                    foreach (var r in oldRows ?? new List<RowData>()) if (r.slot != null) before[r.slot] = r;
                }
                var rows = new List<RowData>();
                var controls = TouchButtonLoader.SafeRead(() => profile.Controls);
                for (int i = 0; controls != null && i < controls.Length; i++)
                {
                    var binding = controls[i];
                    if (binding == null) continue;
                    var slot = SlotKey(binding, i);
                    before.TryGetValue(slot, out var prev);
                    var action = ActionOf(binding);
                    var main = TouchButtonLoader.IsModded(action) ? KeyOf(action) : Kept(action, prev?.action);
                    var group = new List<string>();
                    var any = main != null;
                    var groups = TouchButtonLoader.SafeGroups(binding);
                    for (int j = 0; groups != null && j < groups.Length; j++)
                    {
                        var g = (int)groups[j];
                        var key = TouchButtonLoader.IsModded(g) ? KeyOf(g) : Kept(g, prev != null && prev.group != null && j < prev.group.Count ? prev.group[j] : null);
                        group.Add(key);
                        if (key != null) any = true;
                    }
                    if (any) rows.Add(new RowData { slot = slot, index = i, action = main, group = group });
                }
                if (rows.Count > 0) profiles[id] = rows;
            }
            foreach (var id in oldProfiles.Keys)
            {
                if (!profiles.ContainsKey(id) && !live.Contains(id)) profiles[id] = oldProfiles[id];
            }

            var data = new SavedData { version = 1, profiles = profiles };
            _saved = data;
            try
            {
                var text = JsonSerializer.Serialize(data);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath()));
                File.WriteAllText(FilePath(), text);
            }
            catch (Exception e)
            {
                TouchButtonLoader.Log("TouchButtons: 写 " + FilePath() + " 失败（" + e.Message + "）");
            }
        }

        /// <summary>把 mod 动作从布局里剥掉（保存/分享码用），结束后恢复。</summary>
        public static void __BeginStrip(IList<VirtualControlsProfile_Layout> profiles)
        {
            if (TouchButtonLoader.Count == 0 || profiles == null || profiles.Count == 0) return;
            var unbound = TouchButtonLoader.UnboundValue();
            foreach (var profile in profiles)
            {
                if (profile == null) continue;
                var controls = TouchButtonLoader.SafeRead(() => profile.Controls);
                for (int i = 0; controls != null && i < controls.Length; i++)
                {
                    var binding = controls[i];
                    if (binding == null) continue;
                    var action = ActionOf(binding);
                    if (TouchButtonLoader.IsModded(action))
                    {
                        UndoStack.Push(() => SetAction(binding, (VirtualControlsHardwareConfigurationMapping_Layout.ContolType)action));
                        SetAction(binding, (VirtualControlsHardwareConfigurationMapping_Layout.ContolType)unbound);
                    }
                    var groups = TouchButtonLoader.SafeGroups(binding);
                    for (int j = 0; groups != null && j < groups.Length; j++)
                    {
                        var g = (int)groups[j];
                        if (!TouchButtonLoader.IsModded(g)) continue;
                        var jj = j;
                        UndoStack.Push(() => groups[jj] = (VirtualControlsHardwareConfigurationMapping_Layout.ContolType)g);
                        groups[jj] = (VirtualControlsHardwareConfigurationMapping_Layout.ContolType)unbound;
                    }
                }
            }
        }

        /// <summary>恢复剥离的 mod 动作（后进先出）。</summary>
        public static void __EndStrip()
        {
            while (UndoStack.Count > 0)
            {
                var undo = UndoStack.Pop();
                try { undo(); }
                catch (Exception e) { TouchButtonLoader.Log("TouchButtons: 恢复按钮失败（" + ModUI.Flatten(e) + "）"); }
            }
        }

        /// <summary>游戏的档案 + 当前活动的那个（可能不在列表里）。</summary>
        public static List<VirtualControlsProfile_Layout> Profiles(VirtualControlsStyles_Layout styles)
        {
            var outList = new List<VirtualControlsProfile_Layout>();
            var profiles = styles != null ? TouchButtonLoader.SafeRead(() => styles.Profiles) : null;
            for (int i = 0; profiles != null && i < profiles.Length; i++)
            {
                if (profiles[i] != null) outList.Add(profiles[i]);
            }
            var active = styles != null ? TouchButtonLoader.SafeRead(() => styles.ActiveProfile) : null;
            if (active != null)
            {
                var same = false;
                foreach (var p in outList) if (ReferenceEquals(p, active)) { same = true; break; }
                if (!same) outList.Add(active);
            }
            return outList;
        }

        private static string ProfileId(VirtualControlsProfile_Layout profile)
        {
            try { return profile.Id ?? ""; }
            catch { return ""; }
        }

        private static string SlotKey(VirtualControlsBinding_Layout binding, int i)
        {
            try
            {
                var slotId = TouchButtonHooks.Val(binding, "SlotId");
                if (slotId != null && !string.IsNullOrEmpty(slotId.ToString())) return slotId.ToString();
            }
            catch { }
            return "#" + i;
        }

        private static int ActionOf(VirtualControlsBinding_Layout binding)
        {
            try { return (int)binding.Action; }
            catch { return -1; }
        }

        private static void SetAction(VirtualControlsBinding_Layout binding, VirtualControlsHardwareConfigurationMapping_Layout.ContolType action)
        {
            try { binding.Action = action; }
            catch { TouchButtonHooks.SetVal(binding, "Action", action); }
        }
    }
}