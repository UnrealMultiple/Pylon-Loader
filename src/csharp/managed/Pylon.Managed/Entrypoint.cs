using System.Diagnostics;
using System.Reflection;
using Pylon.Managed.Hosting;
using Pylon.Managed.Mods;

namespace Pylon.Managed;

/// <summary>
/// CoreCLR 入口。内核按名字解析这两个方法，签名与类型全名不可更改。
/// 启动横幅打印四个托管程序集的版本与文件信息（大小、UTC 修改时间），部署核对全靠它：
/// 崩溃后 logcat 可能被系统禁言、文件日志可能 0 字节，日志首行的横幅是判断
/// "部署是不是只覆盖了一半" 的唯一依据；版本不匹配在加载 Mod 前拒绝启动。
/// </summary>
public static class Entrypoint
{
    private static readonly string[] Assemblies =
    [
        "Pylon.Managed",
        "Pylon.ModLoader",
        "Pylon.Mod.Abstractions",
        "Pylon.NaviteAPI",
    ];

    public static void Start()
    {
        Banner();
        Guarded("启动", HostBootstrap.Start);
    }

    public static void StartMods() => Guarded("加载 Mod", () =>
    {
        EnsureLoaderVersionMatches();
        ModPipeline.LoadAll();
    });

    private static void Banner()
    {
        foreach (var name in Assemblies)
        {
            try
            {
                var assembly = Assembly.Load(name);
                var version = assembly.GetName().Version;
                long size = 0;
                var stamp = "?";
                try
                {
                    var info = new FileInfo(assembly.Location);
                    if (info.Exists)
                    {
                        size = info.Length;
                        stamp = info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }
                catch
                {
                    Debug.WriteLine($"banner: {name} 文件信息不可读");
                }

                HostLog.Write($"[pylon] {name} v{version} ({size} B, {stamp})");
            }
            catch (Exception e)
            {
                HostLog.Write($"[pylon] {name} 未加载：{e.Message}");
            }
        }
    }

    private static void EnsureLoaderVersionMatches()
    {
        var managed = typeof(Entrypoint).Assembly.GetName().Version;
        Version? loader;
        try
        {
            loader = Assembly.Load("Pylon.ModLoader").GetName().Version;
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"Pylon.ModLoader 加载失败，拒绝加载 Mod：{e.Message}");
        }

        if (managed is null || loader is null || managed != loader)
            throw new InvalidOperationException(
                $"Pylon.Managed v{managed} 与 Pylon.ModLoader v{loader} 版本不一致（部署只覆盖了一半？），拒绝加载 Mod");
    }

    /// <summary>入口只在这里兜底：再往里的失败都由各自的边界记录。</summary>
    private static void Guarded(string stage, Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            CrashGuard.Report(stage, e);
        }
    }
}
