namespace Pylon.ModLoader;

/// <summary>内容管线的失败出口：管线内部记一条原因，由宿主接到日志。</summary>
internal static class ContentErrors
{
    public static Action<string> Report { get; set; } = static _ => { };
}
