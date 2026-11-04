using System.Text;

namespace Pylon.ModLoader;

/// <summary>
/// 异常与日志边界：Mod 回调抛错只记一条、不外炸；同一标签只记一次，避免每帧刷屏。
/// </summary>
internal static class Safe
{
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);
    private static IModLog _sink = NullLog.Instance;

    internal static IModLog Sink
    {
        get => _sink;
        set => _sink = value ?? NullLog.Instance;
    }

    internal static void Info(string message) => Sink.Info(message);

    internal static void Warn(string message) => Sink.Warn(message);

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

    /// <summary>同一标签只记一次；之后完全静默。</summary>
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

    internal static void Once(string key, string text, bool error = false)
    {
        if (!Reported.Add(key)) return;
        if (error) Sink.Warn(text);
        else Sink.Info(text);
    }

    /// <summary>异常链压成一行：保留"谁抛的"，不刷堆栈。</summary>
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
