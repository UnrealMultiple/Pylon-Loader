using Pylon.Managed.Hosting;
using Pylon.Managed.Mods;

namespace Pylon.Managed;

/// <summary>CoreCLR 入口。内核按名字解析这两个方法，签名与类型全名不可更改。</summary>
public static class Entrypoint
{
    /// <summary>引导线程：启动原生互操作与框架宿主，扫描 Mod 目录（不执行任何 Mod 代码）。</summary>
    public static void Start() => Guarded("启动", HostBootstrap.Start);

    /// <summary>游戏线程：按依赖顺序加载 Mod，随后武装"内容就绪"探测。</summary>
    public static void StartMods() => Guarded("加载 Mod", ModPipeline.LoadAll);

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
