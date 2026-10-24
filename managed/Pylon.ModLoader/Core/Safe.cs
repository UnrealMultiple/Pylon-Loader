// 层：Mod 框架 / Core —— 错误隔离与去重日志
// Run：包一段 mod 回调，抛错只记日志不往外炸（一个 mod 崩不拖垮游戏）；每帧 hook 别用 Run（每次分配 closure+标签），手写 try/catch + Report。
// Once：同一 key 只记一条（防刷屏）；error=true 走 Warn 级（游戏内错误面板）。
// 日志出口对应 ModLogger.Info/Warn/Error。
using System;
using System.Collections.Generic;

namespace Pylon.ModLoader
{
    /// <summary>统一异常边界：Run 包回调、Report/Once 去重日志。</summary>
    internal static class Safe
    {
        /// <summary>日志出口（由 Mod 管线在首个 Mod 加载时接上）。</summary>
        internal static Action<string> Log = _ => { };
        internal static Action<string> Warn = _ => { };

        private static readonly HashSet<string> Reported = new(StringComparer.Ordinal);

        /// <summary>跑一段 mod 回调；抛错记一条日志（不重抛）。</summary>
        internal static void Run(string label, Action action)
        {
            try { action(); }
            catch (Exception e) { Report(label, e); }
        }

        /// <summary>跑一段返回值的回调；抛错记日志并返回 fallback。</summary>
        internal static T Run<T>(string label, Func<T> action, T fallback = default)
        {
            try { return action(); }
            catch (Exception e) { Report(label, e); return fallback; }
        }

        /// <summary>记一条异常（含堆栈第一行）；同一 label 只记一条，之后完全静默（防刷屏）。</summary>
        /// <remarks>
        /// 每帧 hook 异常每次都写日志会把帧率拖死（真机主菜单前几秒卡顿）；所以一个键只写一行，重复的只记账。
        /// </remarks>
        internal static void Report(string label, Exception e)
        {
            var key = label ?? (e != null ? e.GetType().Name : "?");
            if (!Reported.Add(key)) return;

            var text = e != null ? $"{e.GetType().Name}: {e.Message}" : label;
            Warn?.Invoke($"{label} 出错：{text}");
            if (e != null && e.StackTrace != null)
            {
                var first = e.StackTrace.Split('\n');
                if (first.Length > 0) Warn?.Invoke($"  {first[0].Trim()}");
            }
        }

        /// <summary>同一 key 只记一条（防刷屏）；error=true 走 Warn 级。</summary>
        internal static void Once(string key, string text, bool error = false)
        {
            if (!Reported.Add(key)) return;
            if (error) Warn?.Invoke(text); else Log?.Invoke(text);
        }
    }
}