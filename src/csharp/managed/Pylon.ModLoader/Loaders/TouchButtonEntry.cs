using System;
using Terraria;
using GameTexture2D = Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D;

namespace Pylon.ModLoader;

public sealed class TouchButtonEntry
{
    internal TouchButtonEntry(string key, int index)
    {
        Key = key;
        Index = index;
    }

    public string Key { get; }            // 'mod/Name'
    public int Index { get; }             // 注册序号（决定动作编号）
    public Mod Mod { get; internal set; }
    public string Name { get; internal set; }

    // 显示
    internal string TextKey;              // 'Mods.<id>.TouchButtons.<Name>.DisplayName'
    internal string[] Texts;              // 本地化候选（TouchButtons.<Name>.DisplayName / Keybinds.<Name>.DisplayName）
    internal Func<string> LabelFunc;      // options.label（函数或文本）
    internal Func<GameTexture2D> IconFunc;
    internal GameTexture2D TextureValue;  // 缓存（resolve 一次）
    internal bool TextureResolved;

    // 选项
    internal Func<bool> VisibleFunc;
    internal Func<bool> EnabledFunc;
    internal Action<Player> OnPress;

    // 状态（由 Tick 按帧算）
    internal bool Current;
    internal bool Old;
    internal bool JustPressed;
    internal bool JustReleased;
    internal bool Latch;

    /// <summary>按钮本体（Mod 手里那个）。</summary>
    public ModTouchButton Button { get; internal set; }
}
