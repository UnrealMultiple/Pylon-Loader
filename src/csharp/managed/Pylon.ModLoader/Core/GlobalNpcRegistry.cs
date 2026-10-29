// 层：Mod 框架 / Core —— GlobalNpc 注册表与分派（GlobalRegistry 的 npc 版）。
//
// 模型与 GlobalProjectileRegistry 一致：模板列表 + 每次 SetDefaults 重建实体级列表
// （AppliesToEntity 的 lateInstantiation 语义）；无过滤/副本需求时直接复用模板表，零分配。
//
// 对齐旧版偏离的两点（否则任何 GlobalNpc 都会让每个原版 NPC 每帧跑全部钩子）：
//   · 覆写判定用内容类的基类（OverridePlan.Build(cls, typeof(GlobalNpc))），不是 ModType；
//   · For(entity, method) 只给覆写了该方法的 Global（逐方法过滤，按列表缓存）。
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>GlobalNpc 的注册表与分派（For/AnyFor/AnyWith/Attach）。</summary>
    internal static class GlobalNpcRegistry
    {
        private static readonly List<GlobalNpc> Templates = new();
        private static readonly List<GlobalNpc> EmptyList = new();
        private static readonly Dictionary<Type, OverridePlan> Plans = new();

        private static Dictionary<string, MethodEntry> _byMethod;
        private static bool _cached;   // 有 Global 需要实体级列表（AppliesToEntity 过滤或 InstancePerEntity 副本）

        // 方法 → 覆写了它的模板（按注册序）
        private sealed class MethodEntry
        {
            public readonly List<GlobalNpc> Ordered = new();
            public readonly HashSet<GlobalNpc> Set = new();
        }

        // 实体级列表 → (方法 → 过滤后列表)；键随实体回收（WeakMap）
        private static ConditionalWeakTable<List<GlobalNpc>, Dictionary<string, List<GlobalNpc>>> _filtered = new();

        /// <summary>注册一个 GlobalNpc 模板（Mod 发现时由 Mod.DiscoverTypes 调）。</summary>
        internal static void Register(GlobalNpc g)
        {
            if (g == null || Templates.Contains(g)) return;
            Templates.Add(g);
            if (g.InstancePerEntity || PlanOf(g).Has("AppliesToEntity")) _cached = true;
            _byMethod = null;
            _filtered = new ConditionalWeakTable<List<GlobalNpc>, Dictionary<string, List<GlobalNpc>>>();
            AttachedCache.Clear();
        }

        /// <summary>全部已注册的 Global（注册序）。</summary>
        internal static IReadOnlyList<GlobalNpc> All => Templates;

        /// <summary>这个类相对 GlobalNpc 覆写了哪些钩子（Hooks.Overrides 的缓存版）。</summary>
        private static OverridePlan PlanOf(GlobalNpc global)
        {
            var type = global.GetType();
            if (Plans.TryGetValue(type, out var plan)) return plan;
            plan = OverridePlan.Build(type, typeof(GlobalNpc));
            Plans[type] = plan;
            return plan;
        }

        /// <summary>这个方法在这个 NPC 上要跑的 Global（只含覆写了它的那些）。</summary>
        internal static List<GlobalNpc> For(NPC entity, string method)
        {
            var byMethod = _byMethod ??= BuildMethodTable();
            if (!byMethod.TryGetValue(method, out var entry) || entry.Ordered.Count == 0) return EmptyList;

            // 没有过滤、没有副本：模板表就是"覆写了这个方法"的全体（!cached 直接返回）。
            if (!_cached) return entity != null && entity.type > 0 ? entry.Ordered : EmptyList;

            var list = Of(entity);
            if (list.Count == 0) return EmptyList;

            var table = _filtered.GetOrCreateValue(list);
            if (table.TryGetValue(method, out var filtered)) return filtered;

            filtered = new List<GlobalNpc>(list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                var g = list[i];
                if (entry.Set.Contains(g)) filtered.Add(g);
            }
            table[method] = filtered;
            return filtered;
        }

        /// <summary>任一 Global 覆写了这些方法之一（装 hook 前的快速门）。</summary>
        internal static bool AnyWith(params string[] methods)
        {
            var byMethod = _byMethod ??= BuildMethodTable();
            for (var i = 0; i < methods.Length; i++)
                if (byMethod.ContainsKey(methods[i])) return true;
            return false;
        }

        /// <summary>这个实体的一套 Global 实例（无过滤/副本时就是模板表）。</summary>
        internal static List<GlobalNpc> Of(NPC entity)
        {
            if (!_cached) return Templates;
            if (entity == null || entity.type <= 0) return EmptyList;
            return Attached(entity);
        }

        private static readonly Dictionary<long, List<GlobalNpc>> AttachedCache = new();

        /// <summary>SetDefaults 时重建实体级列表（registry.Attach(p, true)）；不需要实体表时不动。</summary>
        internal static void Attach(NPC entity, bool lateInstantiation)
        {
            if (!_cached || entity == null) return;
            var addr = NpcEntities.Address(entity);
            if (addr == 0) return;
            AttachedCache[addr] = Build(entity, lateInstantiation);
        }

        private static List<GlobalNpc> Attached(NPC entity)
        {
            var addr = NpcEntities.Address(entity);
            if (addr == 0) return Templates;
            if (AttachedCache.TryGetValue(addr, out var list)) return list;
            list = Build(entity, true);
            AttachedCache[addr] = list;
            return list;
        }

        private static List<GlobalNpc> Build(NPC entity, bool lateInstantiation)
        {
            var list = new List<GlobalNpc>(Templates.Count);
            for (var i = 0; i < Templates.Count; i++)
            {
                var template = Templates[i];
                if (PlanOf(template).Has("AppliesToEntity") && !template.AppliesToEntity(entity, lateInstantiation)) continue;
                list.Add(template.InstancePerEntity ? template.NewInstance(entity) : template);
            }
            return list;
        }

        private static Dictionary<string, MethodEntry> BuildMethodTable()
        {
            var table = new Dictionary<string, MethodEntry>(StringComparer.Ordinal);
            for (var i = 0; i < Templates.Count; i++)
            {
                var g = Templates[i];
                foreach (var name in PlanOf(g).Names)
                {
                    if (!table.TryGetValue(name, out var entry)) { entry = new MethodEntry(); table[name] = entry; }
                    if (entry.Set.Add(g)) entry.Ordered.Add(g);
                }
            }
            return table;
        }
    }
}
