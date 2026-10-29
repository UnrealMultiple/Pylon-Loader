using System;
using System.Collections.Generic;

namespace Pylon.ModLoader
{
    /// <summary>GlobalTile 的注册表：按方法名给出"覆写了它"的模板（GlobalTileLoader 的 ForList）。</summary>
    internal static class GlobalTileRegistry
    {
        private static readonly List<GlobalTile> Templates = new();
        private static readonly List<GlobalTile> Empty = new();
        private static readonly Dictionary<Type, OverridePlan> Plans = new();
        private static Dictionary<string, List<GlobalTile>> _byMethod;

        internal static void Register(GlobalTile global)
        {
            if (global == null || Templates.Contains(global)) return;
            Templates.Add(global);
            _byMethod = null;
        }

        internal static IReadOnlyList<GlobalTile> All => Templates;

        /// <summary>覆写了这个方法、要按注册序跑的 Global。</summary>
        internal static List<GlobalTile> For(string method)
        {
            var table = _byMethod ??= Build();
            return table.TryGetValue(method, out var list) ? list : Empty;
        }

        private static OverridePlan PlanOf(GlobalTile global)
        {
            var type = global.GetType();
            if (Plans.TryGetValue(type, out var plan)) return plan;
            plan = OverridePlan.Build(type, typeof(GlobalTile));
            Plans[type] = plan;
            return plan;
        }

        private static Dictionary<string, List<GlobalTile>> Build()
        {
            var table = new Dictionary<string, List<GlobalTile>>(StringComparer.Ordinal);
            foreach (var global in Templates)
            {
                foreach (var name in PlanOf(global).Names)
                {
                    if (!table.TryGetValue(name, out var list)) table[name] = list = new List<GlobalTile>();
                    if (!list.Contains(global)) list.Add(global);
                }
            }
            return table;
        }
    }
}
