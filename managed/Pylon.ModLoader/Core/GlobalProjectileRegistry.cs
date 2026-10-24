// 层：Mod 框架 / Core —— GlobalProjectile 注册表与分派（GlobalRegistry 的 projectile 版）。
//
// 与 GlobalItemRegistry 同一模型：模板按注册序排队，方法分派按覆写集合过滤。
// 弹幕有"迟到实例化"：SetDefaults 之后类型才定（AppliesToEntity(p, lateInstantiation:true)），
// 所以 Attach 每次 SetDefaults 时重建实体级列表；无 per-entity 需求时复用模板表，零分配。
using System;
using System.Collections.Generic;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>GlobalProjectile 的注册表与分派（For/AnyFor/TemplatesFor/Attach）。</summary>
    internal static class GlobalProjectileRegistry
    {
        private static readonly List<GlobalProjectile> Templates = new();
        private static Dictionary<string, List<GlobalProjectile>> _byMethod;   // 方法名 → 声明了它的 Global
        private static bool _cached;   // 有任一 Global 需要 per-entity/过滤

        private static readonly List<GlobalProjectile> EmptyList = new();

        /// <summary>注册一个 GlobalProjectile 模板（Mod 发现时由管线调）。</summary>
        internal static void Register(GlobalProjectile g)
        {
            if (g == null || Templates.Contains(g)) return;
            Templates.Add(g);
            if (g.InstancePerEntity || g.Plan.Has("AppliesToEntity")) _cached = true;
            _byMethod = null;
            AttachedCache.Clear();
        }

        /// <summary>全部已注册的 Global（注册序）。</summary>
        internal static IReadOnlyList<GlobalProjectile> All => Templates;

        /// <summary>覆写了这个方法的所有模板（不含实体过滤；钩子目标还没实体时用，例如 FireGrapple）。</summary>
        internal static List<GlobalProjectile> TemplatesFor(string method)
        {
            var byMethod = _byMethod ??= BuildMethodTable();
            return byMethod.TryGetValue(method, out var overrides) ? overrides : EmptyList;
        }

        /// <summary>这个方法在这个弹幕上要跑的 Global（按覆写过滤 + 实体过滤）。</summary>
        internal static List<GlobalProjectile> For(Projectile entity, string method)
        {
            var byMethod = _byMethod ??= BuildMethodTable();
            if (!byMethod.ContainsKey(method)) return EmptyList;
            return Of(entity);
        }

        /// <summary>任一 Global 覆写了这些方法之一（快速门：没覆写直接跳过整段 hook）。</summary>
        internal static bool AnyWith(params string[] methods)
        {
            var byMethod = _byMethod ??= BuildMethodTable();
            foreach (var name in methods)
                if (byMethod.ContainsKey(name)) return true;
            return false;
        }

        /// <summary>这个方法在这个弹幕上有没有 Global 要跑。</summary>
        internal static bool AnyFor(Projectile entity, params string[] methods)
        {
            var byMethod = _byMethod ??= BuildMethodTable();
            foreach (var name in methods)
                if (byMethod.ContainsKey(name) && Of(entity).Count > 0) return true;
            return false;
        }

        /// <summary>这个实体的一套 Global 实例（无附加需求时就是模板表本身）。</summary>
        internal static List<GlobalProjectile> Of(Projectile entity)
        {
            if (!_cached || entity == null) return Templates;
            return Attached(entity);
        }

        private static readonly Dictionary<long, List<GlobalProjectile>> AttachedCache = new();

        /// <summary>SetDefaults 时重建实体级列表（registry.Attach(p, lateInstantiation:true)）。</summary>
        internal static void Attach(Projectile entity, bool lateInstantiation)
        {
            if (entity == null) return;
            var addr = ProjectileEntities.Address(entity);
            if (addr == 0) return;
            AttachedCache[addr] = Build(entity, lateInstantiation);
        }

        private static List<GlobalProjectile> Attached(Projectile entity)
        {
            var addr = ProjectileEntities.Address(entity);
            if (addr == 0) return Templates;
            if (AttachedCache.TryGetValue(addr, out var list)) return list;
            // 没走过 SetDefaults（例如原版就存在、补丁装在后面的弹幕）：按"迟到"语义补一份。
            list = Build(entity, true);
            AttachedCache[addr] = list;
            return list;
        }

        private static List<GlobalProjectile> Build(Projectile entity, bool lateInstantiation)
        {
            var list = new List<GlobalProjectile>(Templates.Count);
            foreach (var template in Templates)
            {
                if (template.Plan.Has("AppliesToEntity") && !template.AppliesToEntity(entity, lateInstantiation)) continue;
                list.Add(template.InstancePerEntity ? template.NewInstance(entity) : template);
            }
            return list;
        }

        private static Dictionary<string, List<GlobalProjectile>> BuildMethodTable()
        {
            var table = new Dictionary<string, List<GlobalProjectile>>(StringComparer.Ordinal);
            foreach (var g in Templates)
            {
                if (g.Plan == null) continue;
                foreach (var name in g.Plan.Names)
                {
                    if (!table.TryGetValue(name, out var list)) { list = new List<GlobalProjectile>(); table[name] = list; }
                    list.Add(g);
                }
            }
            return table;
        }
    }
}
