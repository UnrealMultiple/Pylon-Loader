using Pylon.ModLoader.Pipelines;
using Terraria;

namespace Pylon.ModLoader.Content;
/// <summary>
/// 触屏按钮：玩家在游戏自己的控制编辑里放置它，mod 像读 tModLoader 的 ModKeybind 一样读它。
/// Icon/Label/Visible/Enabled/OnPress 可在注册后赋值，登记到按钮记录上。
/// </summary>
public sealed class ModTouchButton : ModKeybind
{
    internal ModTouchButton(Mod mod, string name, string defaultBinding, TouchButtonEntry entry)
        : base(mod, name, defaultBinding, entry)
    {
    }

    public new Mod Mod => base.Mod;

    public new string Name => base.Name;

    public new string FullName => base.FullName;

    public string? Icon
    {
        get => Entry.IconPath;
        set => Entry.IconPath = value;
    }

    public Func<string>? Label
    {
        get => Entry.LabelFunc;
        set => Entry.LabelFunc = value;
    }

    public Func<bool>? Visible
    {
        get => Entry.VisibleFunc;
        set => Entry.VisibleFunc = value;
    }

    public Func<bool>? Enabled
    {
        get => Entry.EnabledFunc;
        set => Entry.EnabledFunc = value;
    }

    public Action<Player>? OnPress
    {
        get => Entry.OnPress;
        set => Entry.OnPress = value;
    }

    public bool IsBound => TouchButtonLoader.IsBound(Entry);
}
