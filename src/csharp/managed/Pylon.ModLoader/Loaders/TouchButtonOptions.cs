using System;
using Terraria;
using GameTexture2D = Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D;

namespace Pylon.ModLoader;

public sealed class TouchButtonOptions
{
    /// <summary>图标：相对路径 / Texture2D / Asset&lt;Texture2D&gt; / 返回其中之一的函数。</summary>
    public Func<GameTexture2D> Icon { get; set; }

    /// <summary>标签：文本或返回文本的函数（不设则用本地化显示名）。</summary>
    public Func<string> Label { get; set; }

    /// <summary>可见性：false 隐藏（游玩时，编辑器里照常）。</summary>
    public Func<bool> Visible { get; set; }

    /// <summary>可用性：false 置灰且不响应点击。</summary>
    public Func<bool> Enabled { get; set; }

    /// <summary>按下回调（每 tap 一次，立刻）。</summary>
    public Action<Player> OnPress { get; set; }
}
