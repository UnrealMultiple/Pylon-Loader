#nullable disable
// 层：Hook 引擎 —— 全局清点 / 撤销。
using System;
using System.Collections.Generic;
using System.Text;

namespace Il2CppInterop.RuntimeDetour
{
    /// <summary>所有活着的 hook 的统一入口（诊断、宿主关停）。</summary>
    public static class HookManager
    {
        /// <summary>当前挂着的 hook 数量。</summary>
        public static int AppliedCount
        {
            get { lock (HookState.Active) return HookState.Active.Count; }
        }

        /// <summary>一行摘要：Type.Method(槽) …，太多时截断。</summary>
        public static string Describe(int limit = 12)
        {
            List<Hook> snapshot;
            lock (HookState.Active) snapshot = new List<Hook>(HookState.Active);
            if (snapshot.Count == 0) return "没有挂任何 hook";

            var text = new StringBuilder();
            for (var i = 0; i < snapshot.Count && i < limit; i++)
            {
                if (i > 0) text.Append("；");
                text.Append(snapshot[i].Label);
            }
            if (snapshot.Count > limit) text.Append("；…共 ").Append(snapshot.Count).Append(" 条");
            return text.ToString();
        }

        /// <summary>撤销全部 hook（宿主卸载 / 测试）。</summary>
        public static void UndoAll()
        {
            List<Hook> snapshot;
            lock (HookState.Active) snapshot = new List<Hook>(HookState.Active);
            foreach (var hook in snapshot)
            {
                try { hook.Dispose(); } catch (Exception e) { HookLog.Fail("hook:undo-all", e); }
            }
        }
    }
}



