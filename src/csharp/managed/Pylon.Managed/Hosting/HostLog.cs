using System.Runtime.InteropServices;
using System.Text;

namespace Pylon.Managed.Hosting;

/// <summary>托管层的唯一日志出口：写进内核 logcat。</summary>
internal static partial class HostLog
{
    private const string Prefix = "[managed] ";

    /// <summary>写一行日志。日志自身永不抛异常——它常出现在错误处理路径上。</summary>
    public static void Write(string message)
    {
        try
        {
            NativeLog(Prefix + message);
        }
        catch (Exception)
        {
            Console.Error.WriteLine(Prefix + message);
        }
    }

    /// <summary>把异常链压成一行：保留"谁抛的"，不刷堆栈。</summary>
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
        return text.ToString();
    }

    /// <summary>记录一次失败，并把堆栈顶部一行带进日志（定位"谁空引用了"用）。</summary>
    public static void Failure(string label, Exception exception)
    {
        Write($"{label}：{Describe(exception)}");
        if (exception.StackTrace is { } stack) Write(stack.TrimEnd());
    }

    [LibraryImport("pylon", EntryPoint = "PylonMod_Log", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void NativeLog(string message);
}
