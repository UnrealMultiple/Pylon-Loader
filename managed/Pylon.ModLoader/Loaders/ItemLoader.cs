// ItemLoader：ModItem 的使用/装备/背包接线——注册期挂一次 hook，按需分派（OverridePlan + GlobalItemRegistry，没覆写零成本跳过）。
// 分工：SetDefaults/RebuildTooltip 与 tooltip 各在别处；此文件管 CanUseItem/UseItem/UseStyle/HoldStyle/装备/背包更新/射击。
// Harmony IL2CPP 约束：补丁体只调静态方法（内联易炸 IL Compile Error），逻辑抽到静态 RunXXX。
using System;
using HarmonyLib;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader
{
    /// <summary>ModItem 的使用与装备 hook 接线。</summary>
    internal static class ItemLoader
    {
        private static Harmony _harmony;
        private static ModLogger _logger;
        private static bool _installed;

        /// <summary>注册期装一次（幂等）：第一个 mod 注册 item 时由管线调。</summary>
        internal static void Install(Harmony harmony, ModLogger logger)
        {
            if (_installed) return;
            _installed = true;
            _harmony = harmony;
            _logger = logger;

            GameHooks.Install(harmony, logger, typeof(ItemLoader), Targets());
            PatchProjectileNewProjectile();

            _logger?.Info("[item] ItemLoader 已挂（使用/装备/背包更新/射击）");
        }

        private static GameHooks.Hook[] Targets() => new[]
        {
            // CanUseItem：用不用得了（在游戏判否决之前）
            new GameHooks.Hook { Type = typeof(Player), Method = "ItemCheck_CheckCanUse_Inner", ParamCount = 2, Prefix = true, Handler = nameof(CanUsePrefix) },
            // UseItem：真正开始用；返回 true 还要补 ApplyItemTime（游戏只在 itemTime==max 时扣消耗）
            new GameHooks.Hook { Type = typeof(Player), Method = "ItemCheck_StartActualUse", ParamCount = 1, Handler = nameof(UsePostfix) },
            // UseStyle/HoldStyle/HoldItem：手上与姿势（游戏自己的样式之后跑）
            new GameHooks.Hook { Type = typeof(Player), Method = "ItemCheck_ApplyUseStyle", ParamCount = 3, Handler = nameof(UseStylePostfix) },
            // 装备/饰品/时装每帧
            new GameHooks.Hook { Type = typeof(Player), Method = "ApplyEquipFunctional", ParamCount = 2, Handler = nameof(EquipFunctionalPostfix) },
            new GameHooks.Hook { Type = typeof(Player), Method = "ApplyEquipVanity", ParamCount = 2, Handler = nameof(EquipVanityPostfix) },
            new GameHooks.Hook { Type = typeof(Player), Method = "GrantArmorBenefits", ParamCount = 1, Handler = nameof(UpdateEquipPostfix) },
            // 背包里的每帧更新
            new GameHooks.Hook { Type = typeof(Player), Method = "UpdateEquips", ParamCount = 1, Handler = nameof(UpdateInventoryPostfix) },
            // 射击链：CanShoot 门 + NewProjectile 拦截（ModifyShootStats + Shoot）
            new GameHooks.Hook { Type = typeof(Player), Method = "ItemCheck_Shoot", ParamCount = 4, Prefix = true, Handler = nameof(ShootGatePrefix) },
            new GameHooks.Hook { Type = typeof(Player), Method = "ItemCheck_Shoot", ParamCount = 4, Handler = nameof(ShootGatePostfix) },
        };

        private static bool IsModItem(Item item) =>
            item != null && item.type >= VanillaCounts.Item && ModContent.IsCustom(ContentKind.Item, item.type);

        // ---- CanUseItem ----

        private static bool CanUsePrefix(Player __instance, Item __0, bool __1, ref bool __result)
        {
            try
            {
                if (!IsModItem(__0)) return true;
                var mod = ItemEntities.InstanceOf(__0);
                if (mod != null && mod.Plan.Has("CanUseItem") && !mod.CanUseItem(__0, __instance)) { __result = false; return false; }
                foreach (var g in GlobalItemRegistry.For(__0, "CanUseItem"))
                    if (g.Plan.Has("CanUseItem") && !g.CanUseItem(__0, __instance)) { __result = false; return false; }
                return true;
            }
            catch (Exception e) { _logger?.Warn($"[item] CanUseItem 异常：{e.Message}"); return true; }
        }

        // ---- UseItem（原版之后；返回 true 补 ApplyItemTime）----

        private static void UsePostfix(Player __instance, Item __0)
        {
            try
            {
                if (!IsModItem(__0)) return;
                var mod = ItemEntities.InstanceOf(__0);
                var used = mod != null && mod.Plan.Has("UseItem") && mod.UseItem(__0, __instance);
                foreach (var g in GlobalItemRegistry.For(__0, "UseItem"))
                    if (g.Plan.Has("UseItem") && g.UseItem(__0, __instance)) used = true;
                if (used) ApplyItemTimeSafe(__instance, __0);
            }
            catch (Exception e) { _logger?.Warn($"[item] UseItem 异常：{e.Message}"); }
        }

        private static void ApplyItemTimeSafe(Player player, Item item)
        {
            try { player.ApplyItemTime(item); }
            catch { /* 找不到重载时忽略（用不用得上的补时是锦上添花） */ }
        }

        // ---- UseStyle / HoldStyle / HoldItem（游戏自己的样式之后）----

        private static void UseStylePostfix(Player __instance, float __0, Item __1, Rectangle __2)
        {
            try
            {
                if (!IsModItem(__1)) return;
                var mod = ItemEntities.InstanceOf(__1);
                if (mod != null)
                {
                    if (mod.Plan.Has("UseStyle")) mod.UseStyle(__1, __instance, __0, __2);
                    if (mod.Plan.Has("HoldStyle")) mod.HoldStyle(__1, __instance, __0, __2);
                    if (mod.Plan.Has("HoldItem")) mod.HoldItem(__1, __instance);
                }
                foreach (var g in GlobalItemRegistry.For(__1, "UseStyle")) if (g.Plan.Has("UseStyle")) g.UseStyle(__1, __instance, __0, __2);
                foreach (var g in GlobalItemRegistry.For(__1, "HoldStyle")) if (g.Plan.Has("HoldStyle")) g.HoldStyle(__1, __instance, __0, __2);
                foreach (var g in GlobalItemRegistry.For(__1, "HoldItem")) if (g.Plan.Has("HoldItem")) g.HoldItem(__1, __instance);
            }
            catch (Exception e) { _logger?.Warn($"[item] UseStyle 异常：{e.Message}"); }
        }

        // ---- 装备 / 饰品 / 时装每帧 ----

        private static void EquipFunctionalPostfix(Player __instance, int __0, Item __1)
        {
            try
            {
                if (!IsModItem(__1)) return;
                var mod = ItemEntities.InstanceOf(__1);
                if (mod != null && mod.Plan.Has("UpdateEquip")) mod.UpdateEquip(__1, __instance);
                foreach (var g in GlobalItemRegistry.For(__1, "UpdateEquip")) if (g.Plan.Has("UpdateEquip")) g.UpdateEquip(__1, __instance);
            }
            catch (Exception e) { _logger?.Warn($"[item] UpdateEquip 异常：{e.Message}"); }
        }

        private static void EquipVanityPostfix(Player __instance, int __0, Item __1)
        {
            try
            {
                if (!IsModItem(__1)) return;
                var mod = ItemEntities.InstanceOf(__1);
                if (mod != null)
                {
                    if (mod.Plan.Has("UpdateAccessory")) mod.UpdateAccessory(__1, __instance, true, false);
                    if (mod.Plan.Has("UpdateVanity")) mod.UpdateVanity(__1, __instance);
                }
                foreach (var g in GlobalItemRegistry.For(__1, "UpdateAccessory")) if (g.Plan.Has("UpdateAccessory")) g.UpdateAccessory(__1, __instance, true, false);
            }
            catch (Exception e) { _logger?.Warn($"[item] UpdateAccessory 异常：{e.Message}"); }
        }

        private static void UpdateEquipPostfix(Player __instance, Item __0)
        {
            try { EquipFunctionalPostfix(__instance, 0, __0); }
            catch { }
        }

        // ---- 背包里的每帧更新（UpdateInventory）----

        private static void UpdateInventoryPostfix(Player __instance, int __0)
        {
            try
            {
                if (__0 < 0 || __0 >= __instance.inventory.Length) return;
                var item = __instance.inventory[__0];
                if (!IsModItem(item)) return;
                var mod = ItemEntities.InstanceOf(item);
                if (mod != null && mod.Plan.Has("UpdateInventory")) mod.UpdateInventory(item, __instance);
                foreach (var g in GlobalItemRegistry.For(item, "UpdateInventory")) if (g.Plan.Has("UpdateInventory")) g.UpdateInventory(item, __instance);
            }
            catch (Exception e) { _logger?.Warn($"[item] UpdateInventory 异常：{e.Message}"); }
        }

        // ---- 射击链（ItemCheck_Shoot 门 → NewProjectile 拦截）----

        private static Item _shootingItem;   // 正在射击的物品（ItemCheck_Shoot 期间设置，NewProjectile 拦截时消费）
        private static Player _shootingPlayer;

        private static void PatchProjectileNewProjectile()
        {
            // 挂 13 参 float 版（游戏内部发射走它）：NewProjectile(IEntitySource, X, Y, SpeedX, SpeedY, Type, Damage, KnockBack, Owner, ai0..ai2, NewProjectileModifier)
            var target = GameHooks.Find(typeof(Projectile), "NewProjectile",
                new[] { typeof(IEntitySource), typeof(float), typeof(float), typeof(float), typeof(float),
                        typeof(int), typeof(int), typeof(float), typeof(int),
                        typeof(float), typeof(float), typeof(float), typeof(Terraria.NewProjectileModifier) });
            if (target == null) { _logger?.Warn("[item] 找不到 Projectile.NewProjectile(float 版)，射击链不可用"); return; }
            GameHooks.Patch(_harmony, _logger, typeof(ItemLoader), target, prefixHandler: nameof(NewProjectilePrefix));
        }

        /// <summary>射击门：CanShoot false 就跳过原版（不发射）；否则记下物品+玩家，供 NewProjectile 拦截消费。</summary>
        // 参数按名字绑定（原生过滤只让 mod 物品进）；诊断打印全部参数值，区分"绑定错位"与"拿的就是这个物品"。
        private static bool ShootGatePrefix(Player __instance, int i, Item sItem, int weaponDamage, bool withAudioVisualFeedback)
        {
            try
            {
                if (sItem == null || !IsModItem(sItem)) return true;
                var mod = ItemEntities.InstanceOf(sItem);
                if (mod != null && mod.Plan.Has("CanShoot") && !mod.CanShoot(sItem, __instance)) return false;
                foreach (var g in GlobalItemRegistry.For(sItem, "CanShoot"))
                    if (g.Plan.Has("CanShoot") && !g.CanShoot(sItem, __instance)) return false;
                _shootingItem = sItem;        // 一次射击可能发多个弹（穿透/多重）：整个射击期间保持
                _shootingPlayer = __instance;
                return true;
            }
            catch (Exception e) { _logger?.Warn($"[item] CanShoot 异常：{e.Message}"); return true; }
        }

        /// <summary>射击门结束：清上下文（防止泄漏到下一次射击）。</summary>
        private static void ShootGatePostfix(Player __instance, int i, Item sItem, int weaponDamage, bool withAudioVisualFeedback)
        {
            _shootingItem = null;
            _shootingPlayer = null;
        }

        /// <summary>发射拦截：ModifyShootStats 改参数 + Shoot 分派；Shoot false 返回 1000 哨兵（不生成）。</summary>
        // 参数按名字绑定（float 版）。ai0/ai1/ai2 不声明：Harmony 对"声明了 ref 但没赋值"的参数可能置零，
        // 破坏弹幕 ai 数组（近战剑气/追踪弹靠 ai 传数据）；原样放行。
        private static bool NewProjectilePrefix(IEntitySource spawnSource, ref float X, ref float Y,
                                                ref float SpeedX, ref float SpeedY, ref int Type, ref int Damage,
                                                ref float KnockBack, ref int Owner, ref int __result)
        {
            try
            {
                var item = _shootingItem;
                if (item == null || !IsModItem(item)) return true;

                // ⚠ 先清上下文再调 mod 代码：否则 Shoot() 里自己 NewProjectile 会再次触发本 prefix → 无限递归 → SIGABRT。
                var player = _shootingPlayer;
                _shootingItem = null;
                _shootingPlayer = null;
                if (player == null) player = Main.player[Main.myPlayer];
                var position = new Vector2(X, Y);
                var velocity = new Vector2(SpeedX, SpeedY);

                // 参数改写：ModItem 先、Global 后；stat 是 per-shot 的状态对象
                var stats = new ModItem.ShootStats
                {
                    Position = position,
                    Velocity = velocity,
                    Type = Type,
                    Damage = Damage,
                    KnockBack = KnockBack,
                };
                var mod = ItemEntities.InstanceOf(item);
                if (mod != null && mod.Plan.Has("ModifyShootStats")) mod.ModifyShootStats(item, player, stats);
                foreach (var g in GlobalItemRegistry.For(item, "ModifyShootStats"))
                    if (g.Plan.Has("ModifyShootStats")) g.ModifyShootStats(item, player, stats);
                X = stats.Position.X;
                Y = stats.Position.Y;
                SpeedX = stats.Velocity.X;
                SpeedY = stats.Velocity.Y;
                Type = stats.Type;
                Damage = stats.Damage;
                KnockBack = stats.KnockBack;

                // Shoot：false = 游戏不发射自己的弹（mod 自己造）
                var canShoot = true;
                if (mod != null && mod.Plan.Has("Shoot"))
                    canShoot = mod.Shoot(item, player, stats.Position, stats.Velocity, stats.Type, stats.Damage, stats.KnockBack);
                foreach (var g in GlobalItemRegistry.For(item, "Shoot"))
                    if (canShoot && g.Plan.Has("Shoot") && !g.Shoot(item, player, stats.Position, stats.Velocity, stats.Type, stats.Damage, stats.KnockBack, spawnSource)) canShoot = false;
                if (!canShoot) __result = 1000;   // 哨兵：无槽位，游戏跳过
                return canShoot;
            }
            catch (Exception e)
            {
                _logger?.Warn($"[item] Shoot 异常：{e.Message}");
                return true;
            }
        }
    }
}