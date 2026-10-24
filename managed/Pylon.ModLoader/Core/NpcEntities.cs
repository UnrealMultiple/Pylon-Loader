// 层：Mod 框架 / Core —— ModNpc 实体实例化与绑定（对照 ItemEntities / ProjectileEntities）
// 模板（注册时 new 一次）vs 实体实例（每个游戏 NPC 一份）：槽位复用频繁（同类型敌人同格重生），
// 所以 SetDefaults 一律 Rebind：每只新 NPC 都是新实例。
using System;
using System.Collections.Generic;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>ModNpc 实体实例的映射与创建（NPC 实体 ↔ ModNpc 实例）。</summary>
    internal static class NpcEntities
    {
        private static readonly Dictionary<long, ModNpc> ByEntity = new();

        /// <summary>拿实体 NPC 对应的 ModNpc 实例；没有（或 type 变了）就新建并绑定。原版类型一次比较就返回（每帧每个钩子都跑）。</summary>
        internal static ModNpc InstanceOf(NPC entity)
        {
            if (entity == null) return null;
            var type = entity.type;
            if (type < VanillaCounts.Npc) return null;

            var template = ModContent.Find(ContentKind.Npc, type) as ModNpc;
            if (template == null) return null;

            var addr = Address(entity);
            if (ByEntity.TryGetValue(addr, out var current) && current.Type == type) return current;

            return Bind(template, entity, addr);
        }

        /// <summary>SetDefaults 时强制换一份新实例（槽位复用/同类型重生都不继承旧状态）。</summary>
        internal static ModNpc Rebind(NPC entity)
        {
            if (entity == null) return null;
            var type = entity.type;
            if (type < VanillaCounts.Npc) return null;

            var template = ModContent.Find(ContentKind.Npc, type) as ModNpc;
            if (template == null) return null;
            return Bind(template, entity, Address(entity));
        }

        /// <summary>把调用方实例重新绑回实体：CloneDefaults 借用另一类型的 SetDefaults 后要还原（把 npc.ModNPC 换回原实例）。</summary>
        internal static void RebindTo(NPC entity, ModNpc instance)
        {
            if (entity == null || instance == null) return;
            var addr = Address(entity);
            if (addr != 0) ByEntity[addr] = instance;
        }

        private static ModNpc Bind(ModNpc template, NPC entity, long addr)
        {
            var instance = (ModNpc)Activator.CreateInstance(template.GetType());
            instance.Type = template.Type;
            instance.Registered = template.Registered;
            instance.Mod = template.Mod;
            instance.Plan = template.Plan;
            instance.Texture = template.Texture;
            instance.BoundEntity = entity;
            ByEntity[addr] = instance;
            return instance;
        }

        /// <summary>实体地址（无包装返回 0）。</summary>
        internal static long Address(NPC entity) =>
            entity is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj ? (long)obj.Pointer : 0;
    }
}
