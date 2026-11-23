namespace Pylon.ModLoader.Pipeline;
/// <summary>内容管线的失败出口：管线内部记一条原因，由宿主接到日志。</summary>
internal static class ContentErrors
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    public static Action<string> Report { get; set; } = static _ => { };

    /// <summary>按首行去重后投递：同一异常每帧重抛只落一条完整堆栈。</summary>
    public static void Line(string message)
    {
        var key = message.IndexOf('\n') is var cut && cut >= 0 ? message[..cut] : message;
        lock (Gate)
        {
            if (!Reported.Add(key)) return;
        }
        Report(message);
    }

    public static void Callback(string source, Exception exception) =>
        Line(source + " 异常：" + Logging.Safe.Dump(exception));
}
