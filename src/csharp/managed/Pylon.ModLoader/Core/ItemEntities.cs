// 层：Mod 框架 / Core —— 实体实例化与绑定（Entities 的 Item 版）。
//
// 模板（注册时 new 一次）vs 实体实例（每个游戏对象一份）：
// 实体实例 = 构造器重跑 + 叠加"注册后写入的框架字段"（Type/Mod/Texture…）；
// 作者在 SetDefaults(item) 里写游戏对象，自己的状态字段随实例独立。
// 用实体地址（Il2CppObjectBase.Pointer）做 key：持地址不持引用，游戏销毁后自然失联。
//
// 本文件只做 Item（ModItem）；Projectile/NPC 的 Entities 按同一模式补。
using System;
using System.Collections.Generic;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>ModItem 实体实例的映射与创建（Item 实体 ↔ ModItem 实例）。</summary>
    internal static class ItemEntities
    {
        // 实体 Item 地址 → 它的 ModItem 实例（实体销毁后条目留着，下次 SetDefaults 换绑时覆盖）
        private static readonly Dictionary<long, ModItem> ByEntity = new();

        /// <summary>拿这个实体 Item 对应的 ModItem 实例；没有（或 type 变了）就新建并绑定。</summary>
        internal static ModItem InstanceOf(Item entity)
        {
            if (entity == null) return null;
            var template = ModContent.Find(ContentKind.Item, entity.type) as ModItem;
            if (template == null) return null;

            var addr = Address(entity);
            if (ByEntity.TryGetValue(addr, out var current) && current.Type == entity.type)
                return current;

            current = (ModItem)Activator.CreateInstance(template.GetType());
            CopyFrameworkFields(template, current);
            current.Item = entity;
            ByEntity[addr] = current;
            return current;
        }

        /// <summary>新建一个模板的实例（不绑定实体；Clone 复制字段用）。</summary>
        internal static ModItem NewInstance(ModItem template)
        {
            var instance = (ModItem)Activator.CreateInstance(template.GetType());
            CopyFrameworkFields(template, instance);
            return instance;
        }

        private static void CopyFrameworkFields(ModItem from, ModItem to)
        {
            to.Type = from.Type;
            to.Registered = from.Registered;
            to.Mod = from.Mod;
            to.Plan = from.Plan;
            to.Texture = from.Texture;
        }

        /// <summary>实体地址（无包装返回 0）。</summary>
        internal static long Address(Item entity) =>
            entity is Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase obj ? (long)obj.Pointer : 0;
    }
}