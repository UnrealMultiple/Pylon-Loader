namespace Pylon.ModLoader.Effects;

/// <summary>屏幕效果操作失败。</summary>
public sealed class ScreenEffectsException : Exception
{
    public ScreenEffectsException(string message)
        : base(message) { }
}
