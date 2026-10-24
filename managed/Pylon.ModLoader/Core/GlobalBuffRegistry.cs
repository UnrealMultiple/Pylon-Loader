// 层：Mod 框架 / Core —— GlobalBuff 注册表与分派（GlobalList 的 buff 版）。
//
// buff 非实体（只有编号 + 时间）：一个 GlobalBuff 对所有 buff 生效，钩子自己按 type 挑。
//   For(method) = 覆写该方法的模板（按加载序，缓存）；空列表共用一份，别每帧新建。
// 逐个回调的 try/catch 由调用方写，热路径不分配闭包。
using System;
using System.Collections.Generic;

namespace Pylon.ModLoader
{
    /// <summary>GlobalBuff 的注册表与分派（按方法名索引）。</summary>
    internal static class GlobalBuffRegistry
    {
        private static readonly List<GlobalBuff> Templates = new();
        private static readonly List<GlobalBuff> EmptyList = new();
        private static Dictionary<string, List<GlobalBuff>> _byMethod;

        /// <summary>注册一个 GlobalBuff 模板（Mod 发现时由管线调）。</summary>
        internal static void Register(GlobalBuff g)
        {
            if (g == null || Templates.Contains(g)) return;
            Templates.Add(g);
            _byMethod = null;   // 索引作废：后注册的 Global 也要能被分派到
        }

        /// <summary>全部已注册的 Global（注册序）。</summary>
        internal static IReadOnlyList<GlobalBuff> All => Templates;

        /// <summary>覆写了这个方法的所有模板（按注册序，GlobalList.With）。</summary>
        internal static List<GlobalBuff> For(string method)
        {
            var byMethod = _byMethod ??= BuildMethodTable();
            return byMethod.TryGetValue(method, out var list) ? list : EmptyList;
        }

        private static Dictionary<string, List<GlobalBuff>> BuildMethodTable()
        {
            var table = new Dictionary<string, List<GlobalBuff>>(StringComparer.Ordinal);
            foreach (var g in Templates)
            {
                if (g.Plan == null) continue;
                foreach (var name in g.Plan.Names)
                {
                    if (!table.TryGetValue(name, out var list)) { list = new List<GlobalBuff>(); table[name] = list; }
                    list.Add(g);
                }
            }
            return table;
        }
    }
}
