using Pylon.Mod;

namespace Pylon.ModLoader;

/// <summary>框架级引导：UI / ImGui / 菜单不依赖 Mod 列表是否为空。</summary>
public static class Framework
{
    private static IModContext? _context;
    private static ModLogger? _logger;

    public static bool Initialized => _context is not null;

    /// <summary>宿主数据根（用户 Mods 目录）；框架自己的设置写在这里的 .pylon/ 下。</summary>
    internal static string? HostDirectory => _context?.Directory;

    /// <summary>绑定宿主上下文（幂等）：只保存对象、不碰 Unity 类型，可以在引导线程调用。</summary>
    public static void Initialize(IModContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_context is not null) return;

        _context = context;
        _logger = new ModLogger(context);
        Safe.Sink = _logger;
        _logger.Info("框架宿主已初始化（UI / ImGui 不依赖 Mod 数量）");
    }

    /// <summary>内容就绪（游戏线程）：装框架 UI 的 hook。没有 Mod 也必须调。</summary>
    public static void EnsureContentReady()
    {
        if (_logger is null) return;

        try
        {
            UI.ModUI.EnsureInitialized(_logger);
            Effects.ScreenGraphics.Install();
            _logger.Info("框架 UI 已就绪（游戏配置屏 / ImGui 悬浮窗）");
        }
        catch (Exception e)
        {
            _logger.Error("框架 UI 初始化失败：" + Safe.Describe(e));
        }
    }
}
