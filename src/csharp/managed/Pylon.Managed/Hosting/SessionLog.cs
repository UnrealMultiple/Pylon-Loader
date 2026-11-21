using System.Text;

namespace Pylon.Managed.Hosting;

/// <summary>
/// 托管侧会话文件日志：一次游戏进程 = 一个 <c>logs/pylon_yyyyMMdd_HHmmss.txt</c>。
///
/// logcat 在设备上没有 adb 就读不到，而且进程一死缓冲就没了；把托管层（框架 + Mod）
/// 的日志落到文件，启动器才能在崩溃后把"Mod 报了什么错"展示给用户。文件放在
/// Mods 目录旁边的 logs/（app 自己的外部目录），启动器无权限也能读。
/// 日志自身永不抛异常——它常出现在错误处理路径上。
/// </summary>
internal static class SessionLog
{
    private const int KeepSessions = 5;
    private const long MaxBytes = 4 * 1024 * 1024;

    private static readonly object Gate = new();
    private static FileInfo? _sessionFile;
    private static bool _sizeExceeded;

    /// <summary>开一个新会话文件，并删掉更早的（保留最近 KeepSessions 个）。</summary>
    public static void Initialize(string modsRoot)
    {
        var cleanRoot = (modsRoot ?? "").TrimEnd('/', '\\');
        try
        {
            var modsParent = Path.GetDirectoryName(Path.GetFullPath(cleanRoot)) ?? ".";
            var logsDir = Path.Combine(modsParent, "logs");
            Directory.CreateDirectory(logsDir);

            foreach (var stale in Directory.GetFiles(logsDir, "pylon_*.txt")
                         .Select(f => new FileInfo(f))
                         .OrderByDescending(f => f.Name)
                         .Skip(KeepSessions - 1))
            {
                stale.Delete();
            }

            var name = "pylon_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
            _sessionFile = new FileInfo(Path.Combine(logsDir, name));
            File.WriteAllText(_sessionFile.FullName, $"=== Pylon 会话日志 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
            HostLog.Write($"[log] 会话日志就绪：{_sessionFile.FullName}");
        }
        catch (Exception e)
        {
            _sessionFile = null;
            HostLog.Write("[log] 会话日志初始化失败：" + HostLog.Describe(e));
        }
    }

    /// <summary>会话文件（可能为 null：初始化失败时只走 logcat）。</summary>
    public static FileInfo? SessionFile => _sessionFile;

    public static void Info(string source, string message) => Write('I', source, message);

    public static void Warn(string source, string message) => Write('W', source, message);

    public static void Error(string source, string message) => Write('E', source, message);

    /// <summary>按 "HH:mm:ss.fff L [来源] 消息" 追加一行（多行消息压成一行）。L ∈ I/W/E。</summary>
    public static void Write(char level, string source, string message)
    {
        if (_sessionFile is null || _sizeExceeded) return;
        try
        {
            lock (Gate)
            {
                if (_sessionFile.Length > MaxBytes)
                {
                    _sizeExceeded = true;
                    AppendLine('W', "log", "会话日志超过 4MB，本会话不再写入文件（logcat 仍在继续）");
                    return;
                }
                var oneLine = (message ?? "").Replace("\r", "").Replace("\n", " ⏎ ");
                AppendLine(level, source, oneLine);
            }
        }
        catch (Exception)
        {
            // 文件日志失败不能影响游戏与错误处理路径。
        }
    }

    private static void AppendLine(char level, string source, string message)
    {
        var line = new StringBuilder(64)
            .Append(DateTime.Now.ToString("HH:mm:ss.fff"))
            .Append(' ').Append(level)
            .Append(" [").Append(source).Append("] ")
            .Append(message)
            .Append('\n');
        File.AppendAllText(_sessionFile!.FullName, line.ToString());
    }
}
