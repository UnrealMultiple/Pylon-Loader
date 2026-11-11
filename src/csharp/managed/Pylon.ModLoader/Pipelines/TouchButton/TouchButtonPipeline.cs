using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using ContolType = VirtualControlsHardwareConfigurationMapping_Layout.ContolType;

namespace Pylon.ModLoader;

/// <summary>
/// 触屏按钮管线：注册发号、状态机、游戏侧接入（控制器数组、触点、编辑器、快捷栏、布局保存）。
/// 按钮状态在 TouchButtonTable（密集 struct 数组），布局在 TouchButtonLayouts。
/// </summary>
internal sealed class TouchButtonPipeline : ILoaderModule
{
    /// <summary>游戏预留给"未绑定"的编号，我们所有动作都排在它之后。</summary>
    private static readonly int Unbound = (int)VirtualControlsHardwareConfigurationMapping_Layout.ContolType.Count;

    private static readonly int QuickUnbound = (int)QuickActionButton_Layout.QuickActionControl.Unbound;

    private static readonly int FirstControl = (int)GUIVirtualInputController.ControlType.Count;

    /// <summary>快捷栏那套的独立编号空间。</summary>
    private const int QuickBase = 100;

    public static TouchButtonPipeline Instance { get; } = new();

    private readonly TouchButtonTable _table = new();
    private readonly HashSet<int> _reported = [];

    private ModLogger? _log;
    private bool _pending;
    private int _ensureAge;

    public string Name => "touchbutton";

    private static IModLog Log => LoaderHost.Current.Log;

    internal bool Applied { get; private set; }

    internal int Count => _table.Count;

    internal int UnboundValue => Unbound;

    internal int QuickUnboundValue => QuickUnbound;

    public bool Install()
    {
        Log.Info("[touchbutton] 模块就位（游戏 UI 就绪后挂钩）");
        return true;
    }

    #region 注册与编号

    internal ModTouchButton RegisterButton(Mod mod, string name, TouchButtonOptions? options)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("按钮需要一个名字", nameof(name));

        var key = mod.Name + "/" + name.Trim();
        if (_table.ByKey(key) is { } known) return known.Button!;

        var index = _table.Count;
        var entry = new TouchButtonEntry(key, index)
        {
            Mod = mod,
            Name = name.Trim(),
            TextKey = $"Mods.{mod.Name}.TouchButtons.{name.Trim()}.DisplayName",
            Texts = [$"Mods.{mod.Name}.TouchButtons.{name.Trim()}.DisplayName", $"Mods.{mod.Name}.Keybinds.{name.Trim()}.DisplayName"],
        };

        if (options is not null)
        {
            entry.IconFunc = options.Icon;
            entry.LabelFunc = options.Label;
            entry.VisibleFunc = options.Visible;
            entry.EnabledFunc = options.Enabled;
            entry.OnPress = options.OnPress;
        }

        var button = new ModTouchButton(entry, entry.Name);
        entry.Button = button;
        _table.Add(entry);

        Log.Info($"[touchbutton] {key} → 动作 {ActionOf(entry)} / 控件 {ControlOf(entry)}");
        return button;
    }

    internal int ActionOf(TouchButtonEntry entry) => Unbound + 1 + entry.Index;

    internal int QuickOf(TouchButtonEntry entry) => QuickBase + entry.Index;

    internal int ControlOf(TouchButtonEntry entry) => FirstControl + entry.Index;

    internal int LastAction => Unbound + _table.Count;

    internal TouchButtonEntry? ByKey(string? key) => _table.ByKey(key);

    internal TouchButtonEntry? ByAction(int action)
    {
        var index = action - (Unbound + 1);
        return index >= 0 ? _table.ByIndex(index) : null;
    }

    internal TouchButtonEntry? ByQuick(int quick) => quick >= QuickBase ? _table.ByIndex(quick - QuickBase) : null;

    internal bool IsModded(int action) => action > Unbound;

    #endregion

    #region 状态

    /// <summary>按游戏帧号算一次；作者随时读状态都只跑一遍判定。</summary>
    internal void Tick()
    {
        if (_table.IsEmpty) return;

        _table.Tick(IsEnabledOf, PressedOf);
        SyncActionTables();
    }

    /// <summary>上一次把 mod 动作号写进原生表时的上限（Unbound + 数量）；没动过是 -1。</summary>
    private int _written;

    /// <summary>
    /// 游戏的输入表只认自己的动作号：mod 动作不在 <c>DefaultMode</c> / <c>RemappedMode</c> /
    /// <c>RemappedBindings</c> 里，绑定就落空。这里把上限以内没写的槽补成自己的动作号
    /// （表的长度不够就先扩），这样游戏自己的刷新/重映射路径也把 mod 按钮当一等公民。
    /// </summary>
    private void SyncActionTables()
    {
        var last = LastAction;
        if (last < 0 || _written == last) return;

        try
        {
            // 静态表：interop 代理把私有静态字段暴露成公开属性，直接赋值即可；
            // 只在数量变化时写（平时每帧早退）。
            SyncStatic(VirtualControllerInputState.DefaultMode, v => VirtualControllerInputState.DefaultMode = v, last);
            SyncStatic(VirtualControllerInputState.RemappedMode, v => VirtualControllerInputState.RemappedMode = v, last);

            var gui = GUIInstance.Active;
            if (gui is not null)
            {
                var perInstance = gui.VirtualControllerInputState;
                if (perInstance is not null)
                {
                    var bindings = perInstance.RemappedBindings;
                    var oldLength = bindings?.Length ?? 0;
                    if (oldLength <= last)
                    {
                        var grown = new ContolType[Math.Max(last + 1, oldLength)];
                        if (bindings is not null) bindings.CopyTo(grown, 0);
                        perInstance.RemappedBindings = grown;
                        bindings = grown;
                    }

                    for (var i = Unbound + 1; i <= last; i++)
                    {
                        if ((int)bindings![i] != i) bindings[i] = (ContolType)i;
                    }

                    try { perInstance.RefreshAlternativeModeMappings(); }
                    catch { /* 旧布局刷新失败不致命：下一次布局加载还会走这 */ }
                }
            }

            _written = last;
        }
        catch (Exception e)
        {
            Safe.Report("touchbutton.actiontables", e);
            _written = last; // 失败也记，避免每帧重试放大异常
        }
    }

    /// <summary>把静态表扩到 last+1 并把 mod 段补成自己的动作号；长度够且全对时不回写。</summary>
    private static void SyncStatic(ContolType[]? current, Action<ContolType[]> write, int last)
    {
        if (current is not null && current.Length > last)
        {
            var dirty = false;
            for (var i = Unbound + 1; i <= last; i++)
            {
                if ((int)current[i] != i) { current[i] = (ContolType)i; dirty = true; }
            }
            if (!dirty) return;
            write(current);
            return;
        }

        var old = current?.Length ?? 0;
        var grown = new ContolType[Math.Max(last + 1, old)];
        if (current is not null) Array.Copy(current, grown, old);
        for (var i = old; i <= last; i++) grown[i] = (ContolType)i;
        write(grown);
    }

    private bool IsEnabledOf(TouchButtonEntry entry) => EnabledOf(entry);

    internal TouchButtonEntry[] All => _table.All;

    internal bool EnabledOf(TouchButtonEntry entry)
    {
        var func = entry.EnabledFunc;
        if (func is null) return true;

        try
        {
            return func();
        }
        catch (Exception e)
        {
            Safe.Report(entry.Key + ".Enabled", e);
            return true;
        }
    }

    /// <summary>手指按在它的控件上吗（控件号是 FirstControl + 序号）。</summary>
    private static bool PressedOf(TouchButtonEntry entry, bool _)
    {
        var controller = Controller();
        if (controller is null) return false;

        var pressed = controller.Pressed;
        var control = FirstControl + entry.Index;
        return pressed is not null && control >= 0 && control < pressed.Length && pressed[control];
    }

    internal static GUIVirtualInputController? Controller()
    {
        try
        {
            return GUIInstance.Active?.GUIVirtualInputController;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>程序触发：只置 latch，和手指按在同一帧的判定里合并。</summary>
    internal void Press(TouchButtonEntry entry)
    {
        if (entry is null) return;

        _table.Latch(entry.Index);
        Tick();
    }

    internal bool VisibleOf(TouchButtonEntry entry)
    {
        var func = entry.VisibleFunc;
        if (func is null) return true;

        try
        {
            return func();
        }
        catch (Exception e)
        {
            Safe.Report(entry.Key + ".Visible", e);
            return true;
        }
    }

    internal string LabelOf(TouchButtonEntry entry)
    {
        var func = entry.LabelFunc;
        if (func is not null)
        {
            try
            {
                var own = func();
                if (!string.IsNullOrEmpty(own)) return own;
            }
            catch (Exception e)
            {
                Safe.Report(entry.Key + ".Label", e);
            }
        }

        var texts = entry.Texts;
        if (texts is not null)
        {
            for (var i = 0; i < texts.Length; i++)
            {
                try
                {
                    if (Terraria.Localization.Language.Exists(texts[i]))
                    {
                        return Terraria.Localization.Language.GetTextValue(texts[i]);
                    }
                }
                catch
                {
                    // 本地化没就绪：继续退。
                }
            }
        }

        return entry.Name;
    }

    internal Texture2D? TextureOf(TouchButtonEntry entry)
    {
        if (entry.TextureResolved) return entry.TextureValue;

        entry.TextureResolved = true;

        var func = entry.IconFunc;
        if (func is null) return null;

        try
        {
            entry.TextureValue = func();
        }
        catch (Exception e)
        {
            Safe.Report(entry.Key + ".Icon", e);
        }

        return entry.TextureValue;
    }

    #endregion

    #region 安装（游戏 UI 就绪后）

    internal void RequestInstall(ModLogger logger)
    {
        _log = logger;
        _pending = true;
    }

    internal void TryInstallPending()
    {
        if (Applied || !_pending) return;

        try
        {
            if (GUIInstance.Active is null) return;
        }
        catch
        {
            return;
        }

        _pending = false;

        try
        {
            InstallHooks();
            Applied = true;
            _log?.Info("触屏按钮已挂（控制器 + 触点 + 编辑器 + 快捷栏 + 布局）");

            if (_table.Count > 0) TouchButtonLayouts.Apply(Styles());
        }
        catch (Exception e)
        {
            _log?.Error("触屏按钮挂钩失败：" + Safe.Describe(e));
        }
    }

    internal void ApplySaved()
    {
        if (!Applied) return;

        try
        {
            TouchButtonLayouts.Apply(Styles());
        }
        catch (Exception e)
        {
            Safe.Report("touchbutton.apply", e);
        }
    }

    internal VirtualControlsStyles_Layout? Styles()
    {
        try
        {
            return VirtualControlsStyles_Layout.Instance;
        }
        catch
        {
            return null;
        }
    }

    internal bool IsBound(TouchButtonEntry entry)
    {
        var styles = Styles();
        if (styles is null) return false;

        foreach (var profile in TouchButtonLayouts.Profiles(styles))
        {
            var controls = profile.Controls;
            if (controls is null) continue;

            var action = ActionOf(entry);
            for (var i = 0; i < controls.Length; i++)
            {
                var binding = controls[i];
                if (binding is null) continue;

                if ((int)binding.Action == action) return true;

                var groups = binding.GroupActions;
                if (groups is null) continue;

                for (var j = 0; j < groups.Length; j++)
                {
                    if ((int)groups[j] == action) return true;
                }
            }
        }

        return false;
    }

    #endregion

    #region 钩子

    private void InstallHooks()
    {
        On.GUIVirtualInputController.Update += ControllerUpdate;
        On.GUIVirtualInputController.Draw += ControllerDraw;
        On.GUIVirtualInputController.HandleTouchCursorDown += TouchDown;
        On.GUIVirtualInputController.DetermineButtonVisible += ButtonVisible;
        On.GUIVirtualInputControllerEdit.GetControlName += EditName;
        On.GUIVirtualInputControllerEdit.GetControlTextureFrame += EditIcon;
        On.GUIVirtualInputControllerEdit.Draw += EditDraw;
        On.VirtualControlsStyles_Layout.SaveConfiguration += SaveLayout;
        On.VirtualControlsStyles_Layout.LoadConfiguration += LoadLayout;
        On.VirtualControlsStyles_Layout.GetShareCode += ShareCode;
        On.GUIQuickActionButton.GetIconFrame += QuickIconFrame;
    }

    private void ControllerUpdate(On.GUIVirtualInputController.orig_Update orig, GUIVirtualInputController self,
                                  float elapsedTime)
    {
        EnsureController(self);
        orig(self, elapsedTime);
        Tick();
    }

    private void ControllerDraw(On.GUIVirtualInputController.orig_Draw orig, GUIVirtualInputController self)
    {
        EnsureController(self);
        orig(self);
    }

    /// <summary>触点落在我们按钮上：把对应控件标成按下，并记一次按下事件。</summary>
    private void TouchDown(On.GUIVirtualInputController.orig_HandleTouchCursorDown orig,
                           GUIVirtualInputController self, int slot, Vector2 cursorUIPosition, Cursor cursor)
    {
        orig(self, slot, cursorUIPosition, cursor);

        if (_table.IsEmpty) return;

        var entry = SlotAt(self, slot);
        if (entry is null) return;

        try
        {
            var pressed = self.Pressed;
            var control = ControlOf(entry);
            if (pressed is not null && control >= 0 && control < pressed.Length) pressed[control] = true;

            _table.Latch(entry.Index);

            var player = Main.player[Main.myPlayer];
            entry.OnPress?.Invoke(player);
        }
        catch (Exception e)
        {
            Fail(entry, HookId.RightClick, e);
        }
    }

    /// <summary>隐藏的按钮不显示：可见性由作者函数回答（编辑器里始终显示）。</summary>
    private bool ButtonVisible(On.GUIVirtualInputController.orig_DetermineButtonVisible orig,
                               GUIVirtualInputController self, int slot, ContolType action,
                               VirtualControlsHardwareConfigurationMapping_Layout slotConfig)
    {
        if (IsModded((int)action) && ByAction((int)action) is { } entry && !VisibleOf(entry)) return false;

        return orig(self, slot, action, slotConfig);
    }

    /// <summary>编辑器里显示作者的名字。</summary>
    private string EditName(On.GUIVirtualInputControllerEdit.orig_GetControlName orig,
                            GUIVirtualInputControllerEdit self, ContolType control)
    {
        if (IsModded((int)control) && ByAction((int)control) is { } entry) return LabelOf(entry);

        return orig(self, control);
    }

    /// <summary>编辑器里显示作者的图标。</summary>
    private void EditIcon(On.GUIVirtualInputControllerEdit.orig_GetControlTextureFrame orig,
                          GUIVirtualInputControllerEdit self, ContolType controlType, out Texture2D texture,
                          out Rectangle frame)
    {
        orig(self, controlType, out texture, out frame);

        if (!IsModded((int)controlType)) return;
        if (ByAction((int)controlType) is not { } entry) return;

        var icon = TextureOf(entry);
        if (icon is null) return;

        texture = icon;
    }

    /// <summary>
    /// 快捷动作按钮条上的图标：mod 快捷号（<c>QuickBase + index</c>）换成作者的贴图。
    /// 只换图标、不换边框——按钮的外框与按下动画仍是游戏那一套，作者不用重画。
    /// </summary>
    private void QuickIconFrame(On.GUIQuickActionButton.orig_GetIconFrame orig,
                                QuickActionButton_Layout.QuickActionControl actionType, out Texture2D iconTexture,
                                out Rectangle frame)
    {
        if (!_table.IsEmpty && ByQuick((int)actionType) is { } entry)
        {
            var icon = TextureOf(entry);
            if (icon is not null)
            {
                iconTexture = icon;
                frame = new Rectangle(0, 0, icon.Width, icon.Height);
                return;
            }
        }

        orig(actionType, out iconTexture, out frame);
    }

    private void EditDraw(On.GUIVirtualInputControllerEdit.orig_Draw orig, GUIVirtualInputControllerEdit self)
    {
        Ensure(false);
        orig(self);
    }

    private void SaveLayout(On.VirtualControlsStyles_Layout.orig_SaveConfiguration orig,
                            VirtualControlsStyles_Layout self, Terraria.IO.Preferences preferences)
    {
        TouchButtonLayouts.Capture(self);
        TouchButtonLayouts.BeginStrip(TouchButtonLayouts.Profiles(self));

        try
        {
            orig(self, preferences);
        }
        finally
        {
            TouchButtonLayouts.EndStrip();
        }
    }

    private void LoadLayout(On.VirtualControlsStyles_Layout.orig_LoadConfiguration orig,
                            VirtualControlsStyles_Layout self, Terraria.IO.Preferences preferences)
    {
        orig(self, preferences);

        Ensure(true);
        ApplySaved();
    }

    /// <summary>分享码里不该出现别的设备读不懂的动作。</summary>
    private string ShareCode(On.VirtualControlsStyles_Layout.orig_GetShareCode orig,
                             VirtualControlsProfile_Layout profile)
    {
        if (_table.IsEmpty) return orig(profile);

        TouchButtonLayouts.BeginStrip(profile is null ? [] : [profile]);

        try
        {
            return orig(profile);
        }
        finally
        {
            TouchButtonLayouts.EndStrip();
        }
    }

    #endregion

    #region 控制器数组与槽位

    /// <summary>把控制器的按下表扩到能装下我们的控件号；按类型数门控，不每帧扫。</summary>
    private void EnsureController(GUIVirtualInputController controller)
    {
        if (_table.IsEmpty || controller is null) return;

        var needed = FirstControl + _table.Count;

        try
        {
            var pressed = controller.Pressed;
            if (pressed is not null && pressed.Length >= needed) return;

            controller.Pressed = Grow(pressed, needed);
            controller.WasPressed = Grow(controller.WasPressed, needed);
        }
        catch (Exception e)
        {
            Safe.Once("touchbutton.ensure", "触屏按钮：控制器数组扩不了 —— " + Safe.Describe(e));
        }
    }

    private static bool[] Grow(bool[]? source, int needed)
    {
        var grown = new bool[needed];
        if (source is not null) Array.Copy(source, grown, Math.Min(source.Length, needed));

        return grown;
    }

    private void Ensure(bool force)
    {
        if (_table.IsEmpty) return;

        // 布局重载时才需要强制跑一遍；平时 60 帧一次足够。
        if (!force && _ensureAge++ % 60 != 0) return;

        try
        {
            if (Controller() is { } controller) EnsureController(controller);
        }
        catch (Exception e)
        {
            Safe.Report("touchbutton.ensure.layout", e);
        }
    }

    /// <summary>某个触点槽位上是哪个按钮（按当前布局的绑定查）。</summary>
    private TouchButtonEntry? SlotAt(GUIVirtualInputController controller, int slot)
    {
        var styles = Styles();
        if (styles is null) return null;

        foreach (var profile in TouchButtonLayouts.Profiles(styles))
        {
            var controls = profile.Controls;
            if (controls is null || slot < 0 || slot >= controls.Length) continue;

            var binding = controls[slot];
            if (binding is null) continue;

            if (ByAction((int)binding.Action) is { } main) return main;

            var groups = binding.GroupActions;
            if (groups is null) continue;

            // 小组：按组里第一个 mod 动作算（组的细分状态归游戏）。
            for (var j = 0; j < groups.Length; j++)
            {
                if (ByAction((int)groups[j]) is { } inGroup) return inGroup;
            }
        }

        return null;
    }

    #endregion

    private void Fail(TouchButtonEntry entry, HookId hook, Exception exception)
    {
        if (!_reported.Add(HashCode.Combine(entry.Index, hook))) return;
        ContentErrors.Report($"[touchbutton] {entry.Key}.{hook} 异常：{Safe.Describe(exception)}");
    }
}
