#nullable disable
// 层：Hook 引擎 —— 日志出口。
// 引擎不认识宿主（Pylon.ModLoader / Pylon.Managed），只向上抛两条委托；宿主启动时接上。
// 不接也不会崩：Info 为空就静默，Error 为空退回 Info。
using System;

namespace Il2CppInterop.RuntimeDetour
{
    /// <summary>Hook 引擎的日志出口（宿主装配时接上自己的日志）。</summary>
    public static class HookLog
    {
        /// <summary>普通信息（挂钩/撤销/链位置）。</summary>
        public static Action<string> Info;

        /// <summary>错误（handler 抛异常、桩生成失败）。</summary>
        public static Action<string, Exception> Error;

        internal static void Say(string message)
        {
            if (message == null) return;
            try { Info?.Invoke(message); } catch { }
        }

        internal static void Fail(string label, Exception exception)
        {
            try
            {
                if (Error != null) Error(label, exception);
                else Info?.Invoke($"[hook] {label}: {exception?.GetType().Name}: {exception?.Message}");
            }
            catch { }
        }
    }
}



