// ItemLoader：ModItem 的使用/装备/背包接线——注册期订阅一次 On.*，按需分派（OverridePlan + GlobalItemRegistry，没覆写零成本跳过）。
// 分工：SetDefaults/RebuildTooltip 与 tooltip 各在别处；此文件管 CanUseItem/UseItem/UseStyle/HoldStyle/装备/背包更新/射击。
// 写法是 MonoMod 的：handler 首参 orig，末尾自己调原函数；原版之后要跑的就把 orig 放最前面。
using System;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader
{
    /// <summary>ModItem 的使用与装备 hook 接线。</summary>
    internal static class ItemLoader
    {
        private static ModLogger _logger;
        private static bool _installed;

        /// <summary>注册期装一次（幂等）：第一个 mod 注册 item 时由管线调。</summary>
        internal static void Install(ModLogger logger)
        {
            if (_installed) return;
            _installed = true;
            _logger = logger;

            On.Terraria.Player.ItemCheck_CheckCanUse_Inner += CanUse;
            On.Terraria.Player.ItemCheck_StartActualUse += Use;
            On.Terraria.Player.ItemCheck_ApplyUseStyle += UseStyle;
            On.Terraria.Player.ApplyEquipFunctional += EquipFunctional;
            On.Terraria.Player.ApplyEquipVanity += EquipVanity;
            On.Terraria.Player.GrantArmorBenefits += GrantArmorBenefits;
            On.Terraria.Player.UpdateEquips += UpdateInventory;
            On.Terraria.Player.ItemCheck_Shoot += ShootGate;
            On.Terraria.Projectile.NewProjectile_1 += NewProjectile;

            _logger?.Info("[item] ItemLoader 已挂（使用/装备/背包更新/射击）");
        }

        private static bool IsModItem(Item item) =>
            item != null && item.type >= VanillaCounts.Item && ModContent.IsCustom(ContentKind.Item, item.type);

        // ---- CanUseItem ----

        private static bool CanUse(On.Terraria.Player.orig_ItemCheck_CheckCanUse_Inner orig,
                                   Player self, Item sItem, bool ignoreCursed)
        {
            try
            {
                if (IsModItem(sItem))
                {
                    var mod = ItemEntities.InstanceOf(sItem);
                    if (mod != null && mod.Plan.Has("CanUseItem") && !mod.CanUseItem(sItem, self)) return false;
                    foreach (var g in GlobalItemRegistry.For(sItem, "CanUseItem"))
                        if (g.Plan.Has("CanUseItem") && !g.CanUseItem(sItem, self)) return false;
                }
            }
            catch (Exception e) { _logger?.Warn($"[item] CanUseItem 异常：{e.Message}"); }
            return orig(self, sItem, ignoreCursed);
        }

        // ---- UseItem（原版之后；返回 true 补 ApplyItemTime）----

        private static void Use(On.Terraria.Player.orig_ItemCheck_StartActualUse orig, Player self, Item sItem)
        {
            orig(self, sItem);
            try
            {
                if (!IsModItem(sItem)) return;
                var mod = ItemEntities.InstanceOf(sItem);
                var used = mod != null && mod.Plan.Has("UseItem") && mod.UseItem(sItem, self);
                foreach (var g in GlobalItemRegistry.For(sItem, "UseItem"))
                    if (g.Plan.Has("UseItem") && g.UseItem(sItem, self)) used = true;
                if (used) ApplyItemTimeSafe(self, sItem);
            }
            catch (Exception e) { _logger?.Warn($"[item] UseItem 异常：{e.Message}"); }
        }

        private static void ApplyItemTimeSafe(Player player, Item item)
        {
            try { player.ApplyItemTime(item); }
            catch { /* 找不到重载时忽略（用不用得上的补时是锦上添花） */ }
        }

        // ---- UseStyle / HoldStyle / HoldItem（游戏自己的样式之后）----

        private static void UseStyle(On.Terraria.Player.orig_ItemCheck_ApplyUseStyle orig,
                                     Player self, float mountOffset, Item sItem, Rectangle heldItemFrame)
        {
            orig(self, mountOffset, sItem, heldItemFrame);
            try
            {
                if (!IsModItem(sItem)) return;
                var mod = ItemEntities.InstanceOf(sItem);
                if (mod != null)
                {
                    if (mod.Plan.Has("UseStyle")) mod.UseStyle(sItem, self, mountOffset, heldItemFrame);
                    if (mod.Plan.Has("HoldStyle")) mod.HoldStyle(sItem, self, mountOffset, heldItemFrame);
                    if (mod.Plan.Has("HoldItem")) mod.HoldItem(sItem, self);
                }
                foreach (var g in GlobalItemRegistry.For(sItem, "UseStyle")) if (g.Plan.Has("UseStyle")) g.UseStyle(sItem, self, mountOffset, heldItemFrame);
                foreach (var g in GlobalItemRegistry.For(sItem, "HoldStyle")) if (g.Plan.Has("HoldStyle")) g.HoldStyle(sItem, self, mountOffset, heldItemFrame);
                foreach (var g in GlobalItemRegistry.For(sItem, "HoldItem")) if (g.Plan.Has("HoldItem")) g.HoldItem(sItem, self);
            }
            catch (Exception e) { _logger?.Warn($"[item] UseStyle 异常：{e.Message}"); }
        }

        // ---- 装备 / 饰品 / 时装每帧 ----

        private static void EquipFunctional(On.Terraria.Player.orig_ApplyEquipFunctional orig,
                                            Player self, int itemSlot, Item currentItem)
        {
            orig(self, itemSlot, currentItem);
            RunUpdateEquip(self, currentItem);
        }

        private static void GrantArmorBenefits(On.Terraria.Player.orig_GrantArmorBenefits orig,
                                               Player self, Item armorPiece)
        {
            orig(self, armorPiece);
            RunUpdateEquip(self, armorPiece);
        }

        private static void RunUpdateEquip(Player player, Item item)
        {
            try
            {
                if (!IsModItem(item)) return;
                var mod = ItemEntities.InstanceOf(item);
                if (mod != null && mod.Plan.Has("UpdateEquip")) mod.UpdateEquip(item, player);
                foreach (var g in GlobalItemRegistry.For(item, "UpdateEquip")) if (g.Plan.Has("UpdateEquip")) g.UpdateEquip(item, player);
            }
            catch (Exception e) { _logger?.Warn($"[item] UpdateEquip 异常：{e.Message}"); }
        }

        private static void EquipVanity(On.Terraria.Player.orig_ApplyEquipVanity orig,
                                        Player self, int itemSlot, Item currentItem)
        {
            orig(self, itemSlot, currentItem);
            try
            {
                if (!IsModItem(currentItem)) return;
                var mod = ItemEntities.InstanceOf(currentItem);
                if (mod != null)
                {
                    if (mod.Plan.Has("UpdateAccessory")) mod.UpdateAccessory(currentItem, self, true, false);
                    if (mod.Plan.Has("UpdateVanity")) mod.UpdateVanity(currentItem, self);
                }
                foreach (var g in GlobalItemRegistry.For(currentItem, "UpdateAccessory"))
                    if (g.Plan.Has("UpdateAccessory")) g.UpdateAccessory(currentItem, self, true, false);
            }
            catch (Exception e) { _logger?.Warn($"[item] UpdateAccessory 异常：{e.Message}"); }
        }

        // ---- 背包里的每帧更新（UpdateInventory）----

        private static void UpdateInventory(On.Terraria.Player.orig_UpdateEquips orig, Player self, int i)
        {
            orig(self, i);
            try
            {
                if (i < 0 || i >= self.inventory.Length) return;
                var item = self.inventory[i];
                if (!IsModItem(item)) return;
                var mod = ItemEntities.InstanceOf(item);
                if (mod != null && mod.Plan.Has("UpdateInventory")) mod.UpdateInventory(item, self);
                foreach (var g in GlobalItemRegistry.For(item, "UpdateInventory"))
                    if (g.Plan.Has("UpdateInventory")) g.UpdateInventory(item, self);
            }
            catch (Exception e) { _logger?.Warn($"[item] UpdateInventory 异常：{e.Message}"); }
        }

        // ---- 射击链（ItemCheck_Shoot 门 → NewProjectile 拦截）----

        private static Item _shootingItem;   // 正在射击的物品（ItemCheck_Shoot 期间设置，NewProjectile 拦截时消费）
        private static Player _shootingPlayer;

        /// <summary>射击门：CanShoot false 就跳过原版（不发射）；否则记下物品+玩家，供 NewProjectile 拦截消费。</summary>
        private static void ShootGate(On.Terraria.Player.orig_ItemCheck_Shoot orig,
                                      Player self, int i, Item sItem, int weaponDamage, bool withAudioVisualFeedback)
        {
            var skip = false;
            try
            {
                if (sItem != null && IsModItem(sItem))
                {
                    var mod = ItemEntities.InstanceOf(sItem);
                    if (mod != null && mod.Plan.Has("CanShoot") && !mod.CanShoot(sItem, self)) skip = true;
                    else
                    {
                        foreach (var g in GlobalItemRegistry.For(sItem, "CanShoot"))
                            if (g.Plan.Has("CanShoot") && !g.CanShoot(sItem, self)) { skip = true; break; }
                    }

                    if (!skip)
                    {
                        _shootingItem = sItem;        // 一次射击可能发多个弹（穿透/多重）：整个射击期间保持
                        _shootingPlayer = self;
                    }
                }
            }
            catch (Exception e) { _logger?.Warn($"[item] CanShoot 异常：{e.Message}"); }

            try
            {
                if (!skip) orig(self, i, sItem, weaponDamage, withAudioVisualFeedback);
            }
            finally
            {
                // 射击结束：清上下文（防止泄漏到下一次射击）
                _shootingItem = null;
                _shootingPlayer = null;
            }
        }

        /// <summary>发射拦截：ModifyShootStats 改参数 + Shoot 分派；Shoot false 返回 1000 哨兵（不生成）。</summary>
        private static int NewProjectile(On.Terraria.Projectile.orig_NewProjectile_1 orig,
                                         IEntitySource spawnSource, float X, float Y, float SpeedX, float SpeedY,
                                         int Type, int Damage, float KnockBack, int Owner,
                                         float ai0, float ai1, float ai2, NewProjectileModifier modifer)
        {
            try
            {
                var item = _shootingItem;
                if (item == null || !IsModItem(item))
                    return orig(spawnSource, X, Y, SpeedX, SpeedY, Type, Damage, KnockBack, Owner, ai0, ai1, ai2, modifer);

                // ⚠ 先清上下文再调 mod 代码：否则 Shoot() 里自己 NewProjectile 会再次触发本钩子 → 无限递归 → SIGABRT。
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

                // Shoot：false = 游戏不发射自己的弹（mod 自己造）
                var canShoot = true;
                if (mod != null && mod.Plan.Has("Shoot"))
                    canShoot = mod.Shoot(item, player, stats.Position, stats.Velocity, stats.Type, stats.Damage, stats.KnockBack);
                foreach (var g in GlobalItemRegistry.For(item, "Shoot"))
                    if (canShoot && g.Plan.Has("Shoot") && !g.Shoot(item, player, stats.Position, stats.Velocity, stats.Type, stats.Damage, stats.KnockBack, spawnSource)) canShoot = false;

                if (!canShoot) return 1000;   // 哨兵：无槽位，游戏跳过

                return orig(spawnSource, stats.Position.X, stats.Position.Y, stats.Velocity.X, stats.Velocity.Y,
                            stats.Type, stats.Damage, stats.KnockBack, Owner, ai0, ai1, ai2, modifer);
            }
            catch (Exception e)
            {
                _logger?.Warn($"[item] Shoot 异常：{e.Message}");
                return orig(spawnSource, X, Y, SpeedX, SpeedY, Type, Damage, KnockBack, Owner, ai0, ai1, ai2, modifer);
            }
        }
    }
}
