using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader;

/// <summary>回给玩家的那句错（消息 + 颜色）；不填消息就回 Usage。</summary>
public class UsageException : Exception
{
    /// <summary>消息可以没有；颜色 null = 管线用自己的红。</summary>
    public UsageException(string? msg = null, Color? color = null)
        : base(msg ?? "")
    {
        Msg = msg;
        Color = color;
    }

    /// <summary>要回给玩家的文本（null = 没写，管线改成回 Usage）。</summary>
    public string? Msg { get; }

    /// <summary>回这句话的颜色（null = 用管线的红）。</summary>
    public Color? Color { get; }
}
