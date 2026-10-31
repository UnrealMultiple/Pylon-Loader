using System.Text;

namespace Pylon.ModLoader;

/// <summary>
/// 异常边界：包住 Mod 回调，抛错只记日志、不向外炸；同一 label 只记一条，防止每帧刷屏。
/// </summary>
internal static class Safe
{
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);
    private static IModLog _sink = NullLog.Instance;

    /// <summary>日志出口。宿主初始化时接上；未接上时静默。</summary>
    internal static IModLog Sink
    {
        get => _sink;
        set => _sink = value ?? NullLog.Instance;
    }

    /// <summary>普通信息走当前日志出口。</summary>
    internal static void Info(string message) => Sink.Info(message);

    /// <summary>警告走当前日志出口。</summary>
    internal static void Warn(string message) => Sink.Warn(message);

    /// <summary>跑一段 Mod 回调，抛错记一条日志（不重抛）。</summary>
    internal static void Run(string label, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Report(label, e);
        }
    }

    /// <summary>跑一段带回值的 Mod 回调，抛错记日志并返回 fallback。</summary>
    internal static T Run<T>(string label, Func<T> action, T fallback = default!)
    {
        try
        {
            return action();
        }
        catch (Exception e)
        {
            Report(label, e);
            return fallback;
        }
    }

    /// <summary>记一条异常及堆栈首行；同一 label 只记一次，之后完全静默。</summary>
    internal static void Report(string label, Exception? exception)
    {
        if (!Reported.Add(label)) return;
        if (exception is null)
        {
            Sink.Warn(label);
            return;
        }

        Sink.Warn($"{label} 出错：{Describe(exception)}");
        if (exception.StackTrace is { } stack)
        {
            var firstLine = stack.AsSpan();
            var newline = firstLine.IndexOf('\n');
            if (newline >= 0) firstLine = firstLine[..newline];
            Sink.Warn("  " + firstLine.Trim().ToString());
        }
    }

    /// <summary>同一 key 只记一条；error=true 走警告级。</summary>
    internal static void Once(string key, string text, bool error = false)
    {
        if (!Reported.Add(key)) return;
        if (error) Sink.Warn(text);
        else Sink.Info(text);
    }

    /// <summary>把异常链压成一行：保留"谁抛的"，不刷堆栈。</summary>
    internal static string Describe(Exception? exception)
    {
        if (exception is null) return "?";

        var text = new StringBuilder(128);
        Exception? current = exception;
        for (var depth = 0; current is not null && depth < 6; depth++)
        {
            if (depth > 0) text.Append(" <- ");
            text.Append(current.GetType().Name).Append(": ").Append(current.Message);
            current = current.InnerException;
        }
        return text.ToString();
    }
}
