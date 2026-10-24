// 层：Mod 框架 / Core —— 宿主级框架引导（与"有没有 Mod"无关）。
// UI / ImGui 是加载器自己的能力：Mods/ 为空或全部加载失败，"Pylon Mod" 菜单也要能起来。
// 托管入口启动时 Initialize 一次（不碰游戏对象，可在引导线程调）；内容就绪时
// EnsureContentReady 用宿主的 Harmony / 日志装 UI（ModUI 幂等，重复调不会重复挂）。
using System;
using HarmonyLib;
using Pylon.Mod;

namespace Pylon.ModLoader
{
    /// <summary>框架级引导：UI / ImGui / 菜单不依赖 Mod 列表是否为空。</summary>
    public static class Framework
    {
        private static IModContext _context;
        private static ModLogger _logger;

        /// <summary>宿主是否已初始化（不依赖 Mod）。</summary>
        public static bool Initialized => _context != null;

        /// <summary>宿主数据根（用户 Mods 目录；框架自己的设置写在这里的 .pylon/ 下）。</summary>
        internal static string HostDirectory => _context?.Directory;

        /// <summary>绑定宿主上下文（幂等）：只保存对象、不碰 Unity/游戏类型，可在引导线程调；日志与 Harmony 归宿主，无 Mod 时框架消息也有出口。</summary>
        public static void Initialize(IModContext context)
        {
            if (context == null || _context != null) return;

            _context = context;
            _logger = new ModLogger(context);

            // 无 Mod 时 Safe 默认出口是空委托，接上宿主日志告警才看得见；有 Mod 时 ContentInstaller 会换成首个 Mod 的日志。
            Safe.Log = _logger.Info;
            Safe.Warn = _logger.Warn;
            _logger.Info("框架宿主已初始化（UI / ImGui 不依赖 Mod 数量）");
        }

        /// <summary>内容就绪（游戏线程）：装框架 UI 的 hook。没有 Mod 也必须调。</summary>
        public static void EnsureContentReady()
        {
            if (_context == null) return;

            try
            {
                UI.ModUI.EnsureInitialized(_context.Harmony, _logger);
                _logger.Info("框架 UI 已就绪（游戏配置屏 / ImGui 悬浮窗）");
            }
            catch (Exception e)
            {
                _logger.Error("框架 UI 初始化失败：" + Flatten(e));
            }
        }

        private static string Flatten(Exception e)
        {
            var text = new System.Text.StringBuilder();
            var current = e;
            for (var depth = 0; current != null && depth < 6; depth++, current = current.InnerException)
            {
                if (depth > 0) text.Append(" <- ");
                text.Append(current.GetType().Name).Append(": ").Append(current.Message);
            }
            return text.ToString();
        }
    }
}
