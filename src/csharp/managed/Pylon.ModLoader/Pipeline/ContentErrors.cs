namespace Pylon.ModLoader.Pipeline;
/// <summary>内容管线的失败出口：管线内部记一条原因，由宿主接到日志。</summary>
internal static class ContentErrors
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    public static Action<string> Report { get; set; } = static _ => { };

    public static void Callback(string source, Exception exception)
    {
        lock (Gate)
        {
            if (!Reported.Add(source + "|" + exception.GetType().FullName)) return;
        }
        Report(source + " 异常：" + Logging.Safe.Describe(exception));
    }
}
