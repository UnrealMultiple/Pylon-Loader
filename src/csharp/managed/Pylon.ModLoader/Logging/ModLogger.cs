using Pylon.Mod;

namespace Pylon.ModLoader.Logging;

/// <summary>带 Mod 前缀的日志。等级只体现在前缀上，便于按等级 grep。</summary>
public sealed class ModLogger(IModContext context) : IModLog
{
    /// <summary>全部日志输出的汇聚点（级别 info/warn/error，来源 Mod id，消息），运行日志页订阅它。</summary>
    public static event Action<string, string, string>? Captured;

    public void Info(string message)
    {
        context.Log(message);
        Captured?.Invoke("info", context.Id, message);
    }

    public void Warn(string message)
    {
        context.Log("警告：" + message);
        Captured?.Invoke("warn", context.Id, message);
    }

    public void Error(string message)
    {
        context.Log("错误：" + message);
        Captured?.Invoke("error", context.Id, message);
    }
}
