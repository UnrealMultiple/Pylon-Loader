using System.Text;

namespace Pylon.ModLoader.Logging;
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
        Sink.Warn(exception is null ? label : $"{label} 出错：{Dump(exception)}");
    }

    internal static void Once(string key, string text, bool error = false)
    {
        if (!Reported.Add(key)) return;
        if (error) Sink.Warn(text);
        else Sink.Info(text);
    }

    /// <summary>异常链压成一行：保留"谁抛的"和源码帧位置，不刷完整堆栈。</summary>
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
        if (SourceFrame(exception) is { } frame) text.Append(" @ ").Append(frame);
        return text.ToString();
    }

    /// <summary>完整异常：类型、消息链与整条堆栈，供日志直接输出。</summary>
    internal static string Dump(Exception? exception)
    {
        if (exception is null) return "?";
        return exception.ToString().Replace("\r", "").TrimEnd();
    }

    private static string? SourceFrame(Exception? exception)
    {
        var stack = exception?.StackTrace;
        if (string.IsNullOrEmpty(stack)) return null;
        string? fallback = null;
        foreach (var raw in stack.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("at ", StringComparison.Ordinal)) continue;
            var body = line[3..].Trim();
            var cut = body.IndexOf(" in ", StringComparison.Ordinal);
            if (cut < 0)
            {
                fallback ??= body;
                continue;
            }
            var method = body[..cut].Trim();
            var file = body[(cut + 4)..].Trim();
            var lineNo = string.Empty;
            var idx = file.LastIndexOf(":line ", StringComparison.Ordinal);
            if (idx >= 0)
            {
                lineNo = file[(idx + 6)..].Trim();
                file = file[..idx];
            }
            return $"{method}({Path.GetFileName(file)}:{lineNo})";
        }
        return fallback;
    }
}
