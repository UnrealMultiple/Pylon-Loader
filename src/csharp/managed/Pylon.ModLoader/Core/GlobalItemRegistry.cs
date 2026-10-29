// 层：Mod 框架 / Core —— GlobalItem 注册表与分派（GlobalRegistry 的 item 版）。
//
// 全部 GlobalItem 的模板列表（注册序）；方法分派用 OverridePlan（注册时算好的覆写集合）过滤。
// 无 per-entity 需求（InstancePerEntity=false 且 AppliesToEntity 没覆写）时直接复用模板列表，零分配；
// 任一 Global 需要 per-entity / 过滤时，每个实体 Attach 自己的实例列表（按实体地址缓存）。
using System;
using System.Collections.Generic;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>GlobalItem 的注册表与分派（For/Each/All/AnyWith）。</summary>
    internal static class GlobalItemRegistry
    {
        private static readonly List<GlobalItem> Templates = new();
        private static Dictionary<string, List<GlobalItem>> _byMethod;   // 方法名 → 覆写它的 Global（注册后失效重算）
        private static bool _cached;   // 有任一 Global 需要 per-entity/过滤：所有实体都走 Attach 路径

        /// <summary>注册一个 GlobalItem 模板（Mod 发现时由管线调）。</summary>
        internal static void Register(GlobalItem g)
        {
            if (g == null || Templates.Contains(g)) return;
            Templates.Add(g);
            if (g.InstancePerEntity || g.Plan.Has("AppliesToEntity")) _cached = true;
            _byMethod = null;   // 新模板加入：方法缓存失效
            _attachedCache.Clear();
        }

        /// <summary>这个实体的一套 Global 实例（无附加需求时就是模板表本身）。</summary>
        internal static List<GlobalItem> Of(Item entity)
        {
            if (!_cached || entity == null) return Templates;
            return Attached(entity);
        }

        /// <summary>这个方法在这个实体上要跑的 Global（按覆写缓存过滤）。</summary>
        internal static List<GlobalItem> For(Item entity, string method)
        {
            var byMethod = _byMethod ??= BuildMethodTable();
            if (!byMethod.TryGetValue(method, out var overrides) || overrides.Count == 0) return EmptyList;
            var list = Of(entity);
            if (ReferenceEquals(list, Templates)) return overrides;
            return Filter(overrides, entity);
        }

        private static readonly List<GlobalItem> EmptyList = new();
        private static readonly Dictionary<long, List<GlobalItem>> _attachedCache = new();

        /// <summary>实体挂载列表（InstancePerEntity 的要 NewInstance，其余复用模板；AppliesToEntity 过滤）。</summary>
        private static List<GlobalItem> Attached(Item entity)
        {
            var addr = ItemEntities.Address(entity);
            if (addr == 0) return Templates;
            if (_attachedCache.TryGetValue(addr, out var list)) return list;

            list = new List<GlobalItem>(Templates.Count);
            foreach (var template in Templates)
            {
                if (template.Plan.Has("AppliesToEntity") && !template.AppliesToEntity(entity, false)) continue;
                list.Add(template.InstancePerEntity ? template.NewInstance(entity) : template);
            }
            _attachedCache[addr] = list;
            return list;
        }

        /// <summary>附加后的列表里只留覆写了该方法的（实体级缓存）。</summary>
        private static List<GlobalItem> Filter(List<GlobalItem> overrides, Item entity)
        {
            var list = Attached(entity);
            for (var i = 0; i < list.Count; i++)
            {
                if (!overrides.Contains(list[i])) { list.RemoveAt(i); i--; }
            }
            return list;
        }

        private static Dictionary<string, List<GlobalItem>> BuildMethodTable()
        {
            var table = new Dictionary<string, List<GlobalItem>>(StringComparer.Ordinal);
            foreach (var g in Templates)
            {
                if (g.Plan == null) continue;
                foreach (var name in g.Plan.Names)
                {
                    if (!table.TryGetValue(name, out var list)) { list = new List<GlobalItem>(); table[name] = list; }
                    list.Add(g);
                }
            }
            return table;
        }

        /// <summary>任一 Global 覆写了这些方法之一（快速门：没覆写直接跳过整段 hook，避免逐实体查）。</summary>
        internal static bool AnyWith(params string[] methods)
        {
            var byMethod = _byMethod ??= BuildMethodTable();
            foreach (var name in methods)
                if (byMethod.ContainsKey(name)) return true;
            return false;
        }

        /// <summary>全部已注册的 Global（注册序）。</summary>
        internal static IReadOnlyList<GlobalItem> All => Templates;
    }
}