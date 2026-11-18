using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader;

/// <summary>回给玩家的那句错（消息 + 颜色）；不填消息就回 Usage，颜色缺省是红。</summary>
public class UsageException : Exception
{
    internal string? msg;

    internal Color color = new(255, 0, 0, 255);

    public UsageException()
    {
    }

    public UsageException(string msg)
    {
        this.msg = msg;
    }

    public UsageException(string msg, Color color)
    {
        this.msg = msg;
        this.color = color;
    }
}
