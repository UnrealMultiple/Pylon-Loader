using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;

namespace Pylon.ModLoader.Pipelines;
/// <summary>触屏按钮的公开门面：注册与按钮列表；状态与逻辑在 TouchButtonPipeline 里。</summary>
public static class TouchButtonLoader
{
    public static ModTouchButton RegisterButton(Mod mod, string name) => Register(mod, name, "");

    internal static ModTouchButton Register(Mod mod, string name, string defaultBinding) =>
        TouchButtonPipeline.Instance.RegisterButton(mod, name);

    public static int Count => TouchButtonPipeline.Instance.Count;

    public static IReadOnlyList<ModTouchButton> Buttons => TouchButtonPipeline.Instance.Buttons;

    internal static int UnboundValue => TouchButtonPipeline.Instance.UnboundValue;

    internal static int QuickUnboundValue => TouchButtonPipeline.Instance.QuickUnboundValue;

    internal static int ActionOf(TouchButtonEntry entry) => TouchButtonPipeline.Instance.ActionOf(entry);

    internal static int QuickOf(TouchButtonEntry entry) => TouchButtonPipeline.Instance.QuickOf(entry);

    internal static int ControlOf(TouchButtonEntry entry) => TouchButtonPipeline.Instance.ControlOf(entry);

    internal static int LastAction => TouchButtonPipeline.Instance.LastAction;

    internal static TouchButtonEntry? ByKey(string? key) => TouchButtonPipeline.Instance.ByKey(key);

    internal static TouchButtonEntry? ByAction(int action) => TouchButtonPipeline.Instance.ByAction(action);

    internal static TouchButtonEntry? ByQuick(int quick) => TouchButtonPipeline.Instance.ByQuick(quick);

    internal static bool IsModded(int action) => TouchButtonPipeline.Instance.IsModded(action);

    internal static string Label(TouchButtonEntry entry) => TouchButtonPipeline.Instance.LabelOf(entry);

    internal static string Label(ModKeybind keybind) => Label(keybind.Entry);

    internal static bool Visible(TouchButtonEntry entry) => TouchButtonPipeline.Instance.VisibleOf(entry);

    internal static bool Enabled(TouchButtonEntry entry) => TouchButtonPipeline.Instance.EnabledOf(entry);

    internal static bool IsBound(TouchButtonEntry entry) => TouchButtonPipeline.Instance.IsBound(entry);

    internal static bool IsBound(ModKeybind keybind) => IsBound(keybind.Entry);

    internal static Texture2D? Texture(TouchButtonEntry entry) => TouchButtonPipeline.Instance.TextureOf(entry);

    /// <summary>按帧号算一次状态（读 Current/JustPressed 之前会自己调）。</summary>
    internal static void Tick() => TouchButtonPipeline.Instance.Tick();

    /// <summary>程序触发一次按下（和手指按同一套判定）。</summary>
    internal static void Press(TouchButtonEntry entry) => TouchButtonPipeline.Instance.Press(entry);

    internal static GUIVirtualInputController? Controller() => TouchButtonPipeline.Controller();

    internal static VirtualControlsStyles_Layout? Styles() => TouchButtonPipeline.Instance.Styles();
}
