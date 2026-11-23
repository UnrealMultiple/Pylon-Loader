using System.Text;
using Pylon.NaviteAPI.Host;

namespace Pylon.Managed.Hosting;

/// <summary>托管层的唯一日志出口：写进内核 logcat，同时落会话文件（启动器崩溃后展示用）。</summary>
internal static class HostLog
{
    private const string Prefix = "[managed] ";

    /// <summary>写一行日志。日志自身永不抛异常——它常出现在错误处理路径上。</summary>
    public static void Write(string message)
    {
        SessionLog.Write('I', "host", message);
        Logcat(message);
    }

    /// <summary>错误级：会话文件记 E，logcat 同步一份。</summary>
    public static void Error(string message)
    {
        SessionLog.Error("host", message);
        Logcat(message);
    }

    private static void Logcat(string message)
    {
        try
        {
            LogNative.Write(Prefix + message);
        }
        catch (Exception)
        {
            Console.Error.WriteLine(Prefix + message);
        }
    }

    /// <summary>把异常链压成一行：保留"谁抛的"和源码帧位置，不刷完整堆栈。</summary>
    public static string Describe(Exception exception)
    {
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

    /// <summary>记录一次失败：完整异常（含堆栈）进会话文件与 logcat。</summary>
    public static void Failure(string label, Exception exception)
    {
        Error($"{label}：{exception.ToString().Replace("\r", "").TrimEnd()}");
    }
}
