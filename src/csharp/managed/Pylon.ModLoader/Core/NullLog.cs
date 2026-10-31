namespace Pylon.ModLoader;

/// <summary>没有接日志时的空实现，省掉调用方的判空。</summary>
internal sealed class NullLog : IModLog
{
    public static readonly NullLog Instance = new();

    private NullLog() { }

    public void Info(string message) { }

    public void Warn(string message) { }

    public void Error(string message) { }
}
