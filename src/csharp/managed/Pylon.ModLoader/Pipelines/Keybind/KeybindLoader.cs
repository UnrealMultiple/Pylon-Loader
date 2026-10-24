using Il2CppMicrosoft.Xna.Framework.Input;
using Pylon.ModLoader.Content;

namespace Pylon.ModLoader.Pipelines;
/// <summary>tModLoader 兼容的按键注册（拿到的是同一个 ModTouchButton，玩家在游戏控制编辑器里放它）。</summary>
public static class KeybindLoader
{
    public static ModKeybind RegisterKeybind(Mod mod, string name, Keys defaultBinding) =>
        RegisterKeybind(mod, name, defaultBinding.ToString());

    public static ModKeybind RegisterKeybind(Mod mod, string name, string defaultBinding) =>
        KeybindPipeline.Instance.Register(mod, name, defaultBinding);
}
