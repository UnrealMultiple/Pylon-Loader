using System;
using Terraria;
using GameTexture2D = Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D;

namespace Pylon.ModLoader;

/// <summary>一个触屏按钮的注册记录：注册时填一次元数据，之后由状态机逐帧维护状态。</summary>
public sealed class TouchButtonEntry
{
    internal TouchButtonEntry(string key, int index)
    {
        Key = key;
        Index = index;
    }

    /// <summary>'mod/Name'。</summary>
    public string Key { get; }

    /// <summary>注册序号，决定动作编号。</summary>
    public int Index { get; }

    public Mod? Mod { get; internal set; }

    public string Name { get; internal set; } = string.Empty;

    /// <summary>'Mods.&lt;id&gt;.TouchButtons.&lt;Name&gt;.DisplayName'。</summary>
    internal string TextKey = string.Empty;

    /// <summary>本地化候选（TouchButtons.&lt;Name&gt; / Keybinds.&lt;Name&gt;）。</summary>
    internal string[]? Texts;

    internal Func<string>? LabelFunc;
    internal Func<GameTexture2D>? IconFunc;
    internal GameTexture2D? TextureValue;
    internal bool TextureResolved;

    internal Func<bool>? VisibleFunc;
    internal Func<bool>? EnabledFunc;
    internal Action<Player>? OnPress;

    // 状态（由 Tick 按帧算）
    internal bool Current;
    internal bool Old;
    internal bool JustPressed;
    internal bool JustReleased;
    internal bool Latch;

    /// <summary>按钮本体（Mod 手里那个）。</summary>
    public ModTouchButton? Button { get; internal set; }
}
