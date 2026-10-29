// 层：Mod 框架 / Core —— ModProjectile 实体实例化与绑定（对照 ItemEntities）
// 模板（注册时 new 一次）vs 实体实例（每个游戏 Projectile 一份）：构造器重跑 + 只叠加框架字段，用实体地址做 key。
// 槽位换内容时 SetDefaults 会再调且同类型复用同一地址：只按地址查表会拿到上一发遗留状态（Spawned 等），
// 所以 SetDefaults 走 Rebind（每次新建），hook 走 InstanceOf（复用当前绑定）。
using System;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>ModProjectile 实体实例的映射与创建（Projectile 实体 ↔ ModProjectile 实例）。</summary>
    internal static class ProjectileEntities
    {
        private static readonly System.Collections.Generic.Dictionary<long, ModProjectile> ByEntity = new();

        /// <summary>拿这个实体 Projectile 对应的 ModProjectile 实例；没有（或 type 变了）就新建并绑定。</summary>
        internal static ModProjectile InstanceOf(Projectile entity)
        {
            if (entity == null) return null;
            var template = ModContent.Find(ContentKind.Projectile, entity.type) as ModProjectile;
            if (template == null) return null;

            var addr = Address(entity);
            if (ByEntity.TryGetValue(addr, out var current) && current.Type == entity.type)
                return current;

            return Bind(template, entity, addr);
        }

        /// <summary>SetDefaults 时强制换一份新实例（槽位复用/同类型重生都不继承旧状态）。</summary>
        internal static ModProjectile Rebind(Projectile entity)
        {
            if (entity == null) return null;
            var template = ModContent.Find(ContentKind.Projectile, entity.type) as ModProjectile;
            if (template == null) return null;
            return Bind(template, entity, Address(entity));
        }

        private static ModProjectile Bind(ModProjectile template, Projectile entity, long addr)
        {
            var instance = (ModProjectile)Activator.CreateInstance(template.GetType());
            instance.Type = template.Type;
            instance.Registered = template.Registered;
            instance.Mod = template.Mod;
            instance.Plan = template.Plan;
            instance.Template = template.Template;
            instance.Texture = template.Texture;
            instance.Spawned = false;
            instance.BoundEntity = entity;
            ByEntity[addr] = instance;
            return instance;
        }

        /// <summary>实体地址（无包装返回 0）。</summary>
        internal static long Address(Projectile entity) =>
            entity is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj ? (long)obj.Pointer : 0;
    }
}
