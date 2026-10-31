using System;
using System.Collections.Generic;
using System.Globalization;
using Terraria;
using GameTexture2D = Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D;
using ReLogic.Content;
using Pylon.ModLoader.Textures;

using Pylon.ModLoader.UI;

namespace Pylon.ModLoader;

/// <summary>一个触屏按钮的注册记录。</summary>
/// <summary>触屏按钮的注册表与状态机。</summary>
public sealed class TouchButtonLoader : ILoaderModule
{
    public TouchButtonLoader(ModLogger logger) => _logger = logger;

    public string Name => "touchbutton";

    public int Order => 850;

    private static readonly List<TouchButtonEntry> Entries = new();
    private static readonly Dictionary<string, TouchButtonEntry> ByKeyMap = new(StringComparer.Ordinal);
    private static ModLogger? _logger;
    private static IdsData? _ids;
    private static long _tick = -1;

    // 第一个 mod 的 QuickActionControl 号（离游戏的 35 远远的）
    private const int QuickBase = 100;

    private sealed class IdsData
    {
        public int Unbound;
        public int FirstAction;
        public int QuickUnbound;
        public int FirstQuick;
        public int FirstControl;
    }

    /// <summary>RegisterButton。</summary>
    public static ModTouchButton RegisterButton(Mod mod, string name, TouchButtonOptions? options = null)
    {
        if (mod == null) throw new ArgumentException("RegisterButton: 第一个参数必须是你的 Mod（this.Mod）");
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("RegisterButton: 按钮得有个名字");
        name = name.Trim();
        var key = mod.Name + "/" + name;
        if (!ByKeyMap.TryGetValue(key, out var entry))
        {
            entry = new TouchButtonEntry(key, Entries.Count);
            Entries.Add(entry);
            ByKeyMap[key] = entry;
        }
        entry.Mod = mod;
        entry.Name = name;
        entry.TextKey = "Mods." + mod.Name + ".TouchButtons." + name + ".DisplayName";
        entry.Texts = LangLocalized("TouchButtons", name + ".DisplayName") ?? LangLocalized("Keybinds", name + ".DisplayName");
        entry.LabelFunc = options?.Label;
        entry.IconFunc = options?.Icon;
        entry.VisibleFunc = options?.Visible;
        entry.EnabledFunc = options?.Enabled;
        entry.OnPress = options?.OnPress;
        entry.TextureResolved = false;
        entry.TextureValue = null;

        if (entry.Button == null)
        {
            entry.Button = new ModTouchButton(entry, name);
        }

        // 注册在内容就绪之后（PostSetupContent）：已经可以套用保存的布局
        if (TouchButtonHooks.Applied) TouchButtonHooks.ApplySaved();
        return entry.Button;
    }

    // ---- 编号 ----

    private static IdsData Ids
    {
        get
        {
            if (_ids != null) return _ids;
            var ids = new IdsData
            {
                Unbound = Read(() => (int)VirtualControlsHardwareConfigurationMapping_Layout.ContolType.Count, 28)
            };
            ids.FirstAction = ids.Unbound + 1;
            ids.QuickUnbound = Read(() => (int)QuickActionButton_Layout.QuickActionControl.Unbound, 35);
            ids.FirstQuick = QuickBase;
            ids.FirstControl = Read(() => (int)GUIVirtualInputController.ControlType.Count, 33);
            _ids = ids;
            return ids;
        }
    }

    private static int Read(Func<int> fn, int fallback)
    {
        try
        {
            var value = fn();
            if (IsInteger(value)) return value;
        }
        catch { }
        return fallback;
    }

    // 给 Hooks / Layouts 用的内部访问器
    internal static int UnboundValue() => Ids.Unbound;
    internal static int QuickUnboundValue() => Ids.QuickUnbound;

    private static bool IsInteger(int value) => value != int.MinValue;

    public static int Count => Entries.Count;

    /// <summary>全部条目（注册顺序）。</summary>
    public static IReadOnlyList<TouchButtonEntry> All => Entries;

    // 编号换算
    public static int ActionOf(TouchButtonEntry entry) => Ids.FirstAction + entry.Index;
    public static int QuickOf(TouchButtonEntry entry) => Ids.FirstQuick + entry.Index;
    public static int ControlOf(TouchButtonEntry entry) => Ids.FirstControl + entry.Index;
    public static int LastAction => Ids.FirstAction + Count - 1;

    public static TouchButtonEntry? ByKey(string key) =>
        key != null && ByKeyMap.TryGetValue(key, out var entry) ? entry : null;

    public static TouchButtonEntry ByAction(int action)
    {
        var i = action - Ids.FirstAction;
        return i >= 0 && i < Count ? Entries[i] : null;
    }

    public static TouchButtonEntry ByQuick(int quick)
    {
        var i = quick - Ids.FirstQuick;
        return i >= 0 && i < Count ? Entries[i] : null;
    }

    /// <summary>这个动作是不是 mod 的（本 boot 的或退出的 mod 留下的）。</summary>
    public static bool IsModded(int action) => action >= Ids.FirstAction;

    // ---- 文本 / 图标 / 状态 ----

    public static string Label(TouchButtonEntry entry)
    {
        string own = null;
        try
        {
            if (entry.LabelFunc != null)
            {
                var value = entry.LabelFunc();
                if (value != null) own = value;
            }
        }
        catch (Exception e) { Log("TouchButtonLoader: label 出错：" + ModUI.Flatten(e)); }
        if (!string.IsNullOrEmpty(own)) return own;
        if (entry.Texts != null)
        {
            foreach (var key in entry.Texts)
            {
                var text = PickLocalized(key);
                if (!string.IsNullOrEmpty(text)) return text;
            }
        }
        // 驼峰拆词：RandomBuff → "Random Buff"
        return System.Text.RegularExpressions.Regex.Replace(entry.Name, "([A-Z])", " $1").Trim();
    }

    /// <summary>图标的 Texture2D（解析一次并缓存；没有就给 null）。</summary>
    public static GameTexture2D Texture(TouchButtonEntry entry)
    {
        TouchButtonLoader.Log("[touch-trace] Texture enter resolved=" + entry.TextureResolved);
        if (entry.TextureResolved) return entry.TextureValue;
        entry.TextureResolved = true;
        try
        {
            entry.TextureValue = Resolve(entry) ?? null;
        }
        catch (Exception e)
        {
            Log("TouchButtonLoader: 图标出错：" + ModUI.Flatten(e));
            entry.TextureValue = null;
            TouchButtonLoader.Log("[touch-trace] Texture exception");
        }
        TouchButtonLoader.Log("[touch-trace] Texture done value=" + (entry.TextureValue != null));
        return entry.TextureValue;
    }

    public static bool Visible(TouchButtonEntry entry)
    {
        if (entry.VisibleFunc == null) return true;
        try { return entry.VisibleFunc() != false; }
        catch (Exception e) { Log("TouchButtonLoader: visible 出错：" + ModUI.Flatten(e)); return true; }
    }

    public static bool Enabled(TouchButtonEntry entry)
    {
        if (entry.EnabledFunc == null) return true;
        try { return entry.EnabledFunc() != false; }
        catch (Exception e) { Log("TouchButtonLoader: enabled 出错：" + ModUI.Flatten(e)); return true; }
    }

    /// <summary>本帧状态（第一次读时按 Main.GameUpdateCount 算一次）。</summary>
    public static void Tick()
    {
        var now = Main.GameUpdateCount;
        if (now == _tick) return;
        _tick = now;

        var controller = Controller();
        var pressed = controller != null ? SafeRead(() => controller.Pressed) : null;
        var active = !Main.gameMenu;
        foreach (var entry in Entries)
        {
            var control = ControlOf(entry);
            var held = pressed != null && control < pressed.Length && SafeIndex(pressed, control);
            var current = active && (entry.Latch || held) && Enabled(entry);
            entry.Latch = false;
            entry.Old = entry.Current;
            entry.Current = current;
            entry.JustPressed = current && !entry.Old;
            entry.JustReleased = !current && entry.Old;
        }
    }

    /// <summary>一次触摸（比一帧还短也能保证 JustPressed，并触发 onPress）。</summary>
    public static void Press(TouchButtonEntry entry)
    {
        if (Main.gameMenu || !Enabled(entry)) return;
        entry.Latch = true;
        if (entry.OnPress == null) return;
        Player player = null;
        try { player = Main.player[Main.myPlayer]; } catch { }
        try { entry.OnPress(player); }
        catch (Exception e) { Log("TouchButtonLoader: onPress 出错：" + ModUI.Flatten(e)); }
    }

    /// <summary>当前活动的触屏控制器。</summary>
    public static GUIVirtualInputController Controller()
    {
        try { return GUIInstance.Active.GUIVirtualInputController; }
        catch { return null; }
    }

    /// <summary>当前的布局样式实例。</summary>
    public static VirtualControlsStyles_Layout Styles()
    {
        try { return VirtualControlsStyles_Layout.Instance; }
        catch { return null; }
    }

    /// <summary>这个按钮在不在当前布局里（含小组）。</summary>
    public static bool IsBound(TouchButtonEntry entry)
    {
        var action = ActionOf(entry);
        var profile = SafeRead(() => Styles() != null ? Styles().ActiveProfile : null);
        var controls = profile != null ? SafeRead(() => profile.Controls) : null;
        for (int i = 0; controls != null && i < controls.Length; i++)
        {
            var binding = controls[i];
            if (binding == null) continue;
            if (SafeAction(binding) == action) return true;
            var groups = SafeGroups(binding);
            for (int j = 0; groups != null && j < groups.Length; j++)
            {
                if (SafeGroup(groups, j) == action) return true;
            }
        }
        return false;
    }

    /// <summary>装触屏按钮的 hook（幂等由 Hooks.Once 与加载阶段保证）。</summary>
    public bool Install()
    {
        TouchButtonHooks.Install();
        return true;
    }

    internal static void Log(string message) => _logger?.Warn(message);

    // ---- 本地化（Lang.Localized / Lang.Pick 的薄包装）----

    private static string[] LangLocalized(string section, string key)
    {
        try
        {
            foreach (var prefix in new[] { "TouchButtons", "Keybinds" })
            {
                _ = prefix;
            }
            var candidate = "TouchButtons." + key;
            if (Terraria.Localization.Language.Exists(candidate)) return new[] { candidate };
            candidate = "Keybinds." + key;
            if (Terraria.Localization.Language.Exists(candidate)) return new[] { candidate };
        }
        catch { }
        return null;
    }

    private static string PickLocalized(string key)
    {
        try
        {
            if (!Terraria.Localization.Language.Exists(key)) return null;
            var value = Terraria.Localization.Language.GetTextValue(key);
            return string.IsNullOrEmpty(value) || value == key ? null : value;
        }
        catch { return null; }
    }

    // ---- 图标解析 ----

    private static GameTexture2D Resolve(TouchButtonEntry entry)
    {
        var icon = entry.IconFunc != null ? entry.IconFunc() : null;
        if (icon == null)
        {
            // Assets/TouchButtons/<Name>.png，再没有就用包 icon.png
            var file = entry.Mod != null ? FindModTexture(entry.Mod, "Assets/TouchButtons/" + entry.Name) : null;
            if (file != null) return entry.Mod.LoadTexture(file);
            var packIcon = !string.IsNullOrEmpty(entry.Mod?.Directory)
                ? System.IO.Path.Combine(entry.Mod.Directory, "icon.png")
                : null;
            if (packIcon != null && System.IO.File.Exists(packIcon)) return entry.Mod.LoadTexture("icon.png");
            // 兜底：框架内置图标；注意**不能返回 null**（游戏的绘制代码拿 null 纹理去 Draw 会空指针崩）。
            try { return Pylon.ModLoader.UI.ModConfigMenu.BuiltinIcon(); }
            catch { return null; }
        }
        return icon;
    }

    private static string FindModTexture(Mod mod, string relative)
    {
        // 项目里贴图按相对路径解析（PylonTexture.Resolve）；这里只做存在性检查
        try
        {
            var full = PylonTexture.Resolve(mod, relative);
            return System.IO.File.Exists(full) ? relative : null;
        }
        catch { return null; }
    }

    // ---- 读游戏侧 field/数组的安全助手 ----

    internal static T SafeRead<T>(Func<T> fn)
    {
        try { return fn(); }
        catch { return default; }
    }

    internal static bool SafeIndex(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool> array, int i)
    {
        try { return array[i]; }
        catch { return false; }
    }

    internal static int SafeAction(VirtualControlsBinding_Layout binding)
    {
        try { return (int)binding.Action; }
        catch { return -1; }
    }

    internal static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<VirtualControlsHardwareConfigurationMapping_Layout.ContolType> SafeGroups(VirtualControlsBinding_Layout binding)
    {
        try { return binding.GroupActions; }
        catch { return null; }
    }

    internal static int SafeGroup(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<VirtualControlsHardwareConfigurationMapping_Layout.ContolType> groups, int j)
    {
        try { return (int)groups[j]; }
        catch { return -1; }
    }
}
