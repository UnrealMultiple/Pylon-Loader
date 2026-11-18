using System.Text.RegularExpressions;
using Pylon.ModLoader.Pipelines;
using Terraria.GameInput;
using Terraria.Localization;

namespace Pylon.ModLoader.Content;
/// <summary>
/// 一个 mod 的键位；手机端按键就是触屏按钮，状态由 TouchButton 管线按帧驱动。
/// Current 是按住，JustPressed/JustReleased 是本帧变化，Old 是上一帧按住；RetroCurrent 在聊天/对话/牌子打开时为 false。
/// </summary>
public class ModKeybind
{
    internal ModKeybind(Mod mod, string name, string defaultBinding, TouchButtonEntry entry)
    {
        Mod = mod;
        Name = name;
        DefaultBinding = defaultBinding;
        Entry = entry;
    }

    internal int Index { get; set; }

    internal Mod Mod { get; }

    internal string Name { get; }

    internal string FullName => $"{Mod.Name}/{Name}";

    internal string DefaultBinding { get; }

    internal TouchButtonEntry Entry { get; }

    public LocalizedText DisplayName => new($"Mods.{Mod.Name}.Keybinds.{Name}.DisplayName", PrettyName());

    internal string PrettyName() => Regex.Replace(Name, "([A-Z])", " $1").Trim();

    public List<string> GetAssignedKeys(InputMode mode = InputMode.Keyboard) =>
        TouchButtonLoader.IsBound(this) ? new List<string> { TouchButtonLoader.Label(this) } : new List<string>();

    public bool RetroCurrent => KeybindPipeline.Instance.RetroCurrent(this);

    public bool Current
    {
        get
        {
            TouchButtonLoader.Tick();
            return Entry.Current;
        }
    }

    public bool JustPressed
    {
        get
        {
            TouchButtonLoader.Tick();
            return Entry.JustPressed;
        }
    }

    public bool JustReleased
    {
        get
        {
            TouchButtonLoader.Tick();
            return Entry.JustReleased;
        }
    }

    public bool Old
    {
        get
        {
            TouchButtonLoader.Tick();
            return Entry.Old;
        }
    }

    public override string ToString() => FullName;
}
