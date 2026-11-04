using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader;

/// <summary>触屏按钮的公开门面：状态与逻辑在 TouchButtonPipeline 里，这里只转发作者与屏幕用到的入口。</summary>
public static class TouchButtonLoader
{
    /// <summary>注册一个按钮（同名重复注册返回同一个）。</summary>
    public static ModTouchButton RegisterButton(Mod mod, string name, TouchButtonOptions? options = null) =>
        TouchButtonPipeline.Instance.RegisterButton(mod, name, options);

    public static int Count => TouchButtonPipeline.Instance.Count;

    public static IReadOnlyList<TouchButtonEntry> All => TouchButtonPipeline.Instance.All;

    public static int UnboundValue => TouchButtonPipeline.Instance.UnboundValue;

    public static int QuickUnboundValue => TouchButtonPipeline.Instance.QuickUnboundValue;

    public static int ActionOf(TouchButtonEntry entry) => TouchButtonPipeline.Instance.ActionOf(entry);

    public static int QuickOf(TouchButtonEntry entry) => TouchButtonPipeline.Instance.QuickOf(entry);

    public static int ControlOf(TouchButtonEntry entry) => TouchButtonPipeline.Instance.ControlOf(entry);

    public static int LastAction => TouchButtonPipeline.Instance.LastAction;

    public static TouchButtonEntry? ByKey(string? key) => TouchButtonPipeline.Instance.ByKey(key);

    public static TouchButtonEntry? ByAction(int action) => TouchButtonPipeline.Instance.ByAction(action);

    public static TouchButtonEntry? ByQuick(int quick) => TouchButtonPipeline.Instance.ByQuick(quick);

    public static bool IsModded(int action) => TouchButtonPipeline.Instance.IsModded(action);

    public static string Label(TouchButtonEntry entry) => TouchButtonPipeline.Instance.LabelOf(entry);

    public static bool Visible(TouchButtonEntry entry) => TouchButtonPipeline.Instance.VisibleOf(entry);

    public static bool Enabled(TouchButtonEntry entry) => TouchButtonPipeline.Instance.EnabledOf(entry);

    public static bool IsBound(TouchButtonEntry entry) => TouchButtonPipeline.Instance.IsBound(entry);

    public static Texture2D? Texture(TouchButtonEntry entry) => TouchButtonPipeline.Instance.TextureOf(entry);

    /// <summary>按帧号算一次状态（读 Current/JustPressed 之前会自己调）。</summary>
    public static void Tick() => TouchButtonPipeline.Instance.Tick();

    /// <summary>程序触发一次按下（和手指按同一套判定）。</summary>
    public static void Press(TouchButtonEntry entry) => TouchButtonPipeline.Instance.Press(entry);

    public static GUIVirtualInputController? Controller() => TouchButtonPipeline.Controller();

    public static VirtualControlsStyles_Layout? Styles() => TouchButtonPipeline.Instance.Styles();
}
