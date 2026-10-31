namespace Pylon.Managed.Hosting;

/// <summary>
/// 全局错误边界：所有漏网的异常统一落到 logcat，并按"位置 + 异常类型"去重，
/// 避免每帧抛错把日志刷爆。
///
/// 边界模型（从外到内）：
///   1. <see cref="Install"/>：AppDomain / 未观察的 Task —— 只能记录，无法阻止进程终止；
///   2. <see cref="Entrypoint"/>：两个托管入口各包一层，启动失败不会静默；
///   3. hook 引擎：每个 On.* 调用由 stub 包住（异常记日志并返回默认值），游戏不会被打断；
///   4. <c>Pylon.ModLoader.Safe</c>：同一个 hook 里多个 Mod 的隔离，一次失败不影响其余 Mod。
/// 所以业务代码里只保留真正需要的边界，不要再逐调用写 try/catch。
/// </summary>
internal static class CrashGuard
{
    private const int MaxRemembered = 256;

    private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report("unhandled", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Report("unobserved-task", e.Exception);
            e.SetObserved();
        };
    }

    /// <summary>记录一个异常（含堆栈）。同一位置同一类型的重复异常静默丢弃。</summary>
    public static void Report(string label, Exception? exception)
    {
        if (exception is null)
        {
            HostLog.Write($"[error] {label}");
            return;
        }

        var key = label + "|" + exception.GetType().Name;
        if (Reported.Count < MaxRemembered && !Reported.Add(key)) return;

        HostLog.Write($"[error] {label}: {HostLog.Describe(exception)}");
        if (exception.StackTrace is { } stack) HostLog.Write(stack.TrimEnd());
    }
}
