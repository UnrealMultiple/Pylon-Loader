using Pylon.ModLoader.Tables;
using Pylon.ModLoader.Textures;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>
/// 物品管线：把 ModItem 接进"用 / 挥 / 装备 / 射"这条链。
/// 分派顺序固定为本物品先、GlobalItem 后；射击上下文是实例字段，不跨线程、不跨射击。
/// </summary>
internal sealed class ItemPipeline : ILoaderModule
{
    public static ItemPipeline Instance { get; } = new();

    private readonly ItemBinding _binding = new();
    private readonly ItemDefaults _defaults;

    private Item? _shootingItem;
    private Player? _shootingPlayer;

    private ItemPipeline() => _defaults = new ItemDefaults(_binding);

    public string Name => "item";

    public IReadOnlyList<TableRequest> Tables =>
        [new(ItemTables.Set, () => ModContent.NextType(ContentKind.Item), early: true)];


    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        _defaults.Install();

        On.Terraria.Player.ItemCheck_CheckCanUse_Inner += CanUse;
        On.Terraria.Player.ItemCheck_StartActualUse += Use;
        On.Terraria.Player.ItemCheck_ApplyUseStyle += ApplyUseStyle;
        On.Terraria.Player.ApplyEquipFunctional += EquipFunctional;
        On.Terraria.Player.ApplyEquipVanity += EquipVanity;
        On.Terraria.Player.GrantArmorBenefits += GrantArmorBenefits;
        On.Terraria.Player.UpdateEquips += UpdateInventory;
        On.Terraria.Player.ItemCheck_Shoot += ShootGate;
        On.Terraria.Projectile.NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float_NewProjectileModifier += NewProjectile;

        Log.Info("[item] 已挂（使用 / 装备 / 背包 / 射击）");
        return true;
    }

    private static bool IsModItem(Item? item) =>
        item is not null && ModContent.IsCustom(ContentKind.Item, item.type);

    private ModItem? ModOf(Item item) => _binding.InstanceOf(item);

    // ===================== 使用链 =====================

    private bool CanUse(On.Terraria.Player.orig_ItemCheck_CheckCanUse_Inner orig,
                        Player self, Item sItem, bool ignoreCursed)
    {
        try
        {
            if (IsModItem(sItem))
            {
                var mod = ModOf(sItem);
                if (mod is not null && mod.Plan.Has(HookId.CanUseItem) && !mod.CanUseItem(sItem, self)) return false;

                foreach (var global in GlobalItemRegistry.For(sItem, HookId.CanUseItem))
                {
                    if (global.Plan.Has(HookId.CanUseItem) && !global.CanUseItem(sItem, self)) return false;
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn("[item] CanUseItem 异常：" + e.Message);
        }

        return orig(self, sItem, ignoreCursed);
    }

    private void Use(On.Terraria.Player.orig_ItemCheck_StartActualUse orig, Player self, Item sItem)
    {
        orig(self, sItem);

        try
        {
            if (!IsModItem(sItem)) return;

            var used = false;
            var mod = ModOf(sItem);
            if (mod is not null && mod.Plan.Has(HookId.UseItem)) used = mod.UseItem(sItem, self);

            foreach (var global in GlobalItemRegistry.For(sItem, HookId.UseItem))
            {
                if (global.Plan.Has(HookId.UseItem) && global.UseItem(sItem, self)) used = true;
            }

            if (used) ApplyItemTime(self, sItem);
        }
        catch (Exception e)
        {
            Log.Warn("[item] UseItem 异常：" + e.Message);
        }
    }

    private static void ApplyItemTime(Player player, Item item)
    {
        try
        {
            player.ApplyItemTime(item);
        }
        catch
        {
            // 补时是锦上添花：重载缺失时忽略。
        }
    }

    private void ApplyUseStyle(On.Terraria.Player.orig_ItemCheck_ApplyUseStyle orig,
                               Player self, float mountOffset, Item sItem, Rectangle heldItemFrame)
    {
        orig(self, mountOffset, sItem, heldItemFrame);

        try
        {
            if (!IsModItem(sItem)) return;

            var mod = ModOf(sItem);
            if (mod is not null)
            {
                if (mod.Plan.Has(HookId.UseStyle)) mod.UseStyle(sItem, self, mountOffset, heldItemFrame);
                if (mod.Plan.Has(HookId.HoldStyle)) mod.HoldStyle(sItem, self, mountOffset, heldItemFrame);
                if (mod.Plan.Has(HookId.HoldItem)) mod.HoldItem(sItem, self);
            }

            foreach (var global in GlobalItemRegistry.For(sItem, HookId.UseStyle))
            {
                if (global.Plan.Has(HookId.UseStyle)) global.UseStyle(sItem, self, mountOffset, heldItemFrame);
            }

            foreach (var global in GlobalItemRegistry.For(sItem, HookId.HoldStyle))
            {
                if (global.Plan.Has(HookId.HoldStyle)) global.HoldStyle(sItem, self, mountOffset, heldItemFrame);
            }

            foreach (var global in GlobalItemRegistry.For(sItem, HookId.HoldItem))
            {
                if (global.Plan.Has(HookId.HoldItem)) global.HoldItem(sItem, self);
            }
        }
        catch (Exception e)
        {
            Log.Warn("[item] UseStyle 异常：" + e.Message);
        }
    }

    // ===================== 装备链 =====================

    private void EquipFunctional(On.Terraria.Player.orig_ApplyEquipFunctional orig,
                                 Player self, int itemSlot, Item currentItem)
    {
        orig(self, itemSlot, currentItem);
        UpdateEquip(self, currentItem);
    }

    private void GrantArmorBenefits(On.Terraria.Player.orig_GrantArmorBenefits orig, Player self, Item armorPiece)
    {
        orig(self, armorPiece);
        UpdateEquip(self, armorPiece);
    }

    private void UpdateEquip(Player player, Item item)
    {
        try
        {
            if (!IsModItem(item)) return;

            var mod = ModOf(item);
            if (mod is not null && mod.Plan.Has(HookId.UpdateEquip)) mod.UpdateEquip(item, player);

            foreach (var global in GlobalItemRegistry.For(item, HookId.UpdateEquip))
            {
                if (global.Plan.Has(HookId.UpdateEquip)) global.UpdateEquip(item, player);
            }
        }
        catch (Exception e)
        {
            Log.Warn("[item] UpdateEquip 异常：" + e.Message);
        }
    }

    private void EquipVanity(On.Terraria.Player.orig_ApplyEquipVanity orig,
                             Player self, int itemSlot, Item currentItem)
    {
        orig(self, itemSlot, currentItem);

        try
        {
            if (!IsModItem(currentItem)) return;

            var mod = ModOf(currentItem);
            if (mod is not null)
            {
                if (mod.Plan.Has(HookId.UpdateAccessory)) mod.UpdateAccessory(currentItem, self, true, false);
                if (mod.Plan.Has(HookId.UpdateVanity)) mod.UpdateVanity(currentItem, self);
            }

            foreach (var global in GlobalItemRegistry.For(currentItem, HookId.UpdateAccessory))
            {
                if (global.Plan.Has(HookId.UpdateAccessory)) global.UpdateAccessory(currentItem, self, true, false);
            }
        }
        catch (Exception e)
        {
            Log.Warn("[item] UpdateAccessory 异常：" + e.Message);
        }
    }

    private void UpdateInventory(On.Terraria.Player.orig_UpdateEquips orig, Player self, int i)
    {
        orig(self, i);

        try
        {
            if (i < 0 || i >= self.inventory.Length) return;

            var item = self.inventory[i];
            if (!IsModItem(item)) return;

            var mod = ModOf(item);
            if (mod is not null && mod.Plan.Has(HookId.UpdateInventory)) mod.UpdateInventory(item, self);

            foreach (var global in GlobalItemRegistry.For(item, HookId.UpdateInventory))
            {
                if (global.Plan.Has(HookId.UpdateInventory)) global.UpdateInventory(item, self);
            }
        }
        catch (Exception e)
        {
            Log.Warn("[item] UpdateInventory 异常：" + e.Message);
        }
    }

    // ===================== 射击链 =====================

    /// <summary>射击门：CanShoot 否决就跳过原版；否则记下上下文供 NewProjectile 消费。</summary>
    private void ShootGate(On.Terraria.Player.orig_ItemCheck_Shoot orig,
                           Player self, int i, Item sItem, int weaponDamage, bool withAudioVisualFeedback)
    {
        var skip = false;

        try
        {
            if (IsModItem(sItem))
            {
                var mod = ModOf(sItem);
                if (mod is not null && mod.Plan.Has(HookId.CanShoot) && !mod.CanShoot(sItem, self))
                {
                    skip = true;
                }
                else
                {
                    foreach (var global in GlobalItemRegistry.For(sItem, HookId.CanShoot))
                    {
                        if (!global.Plan.Has(HookId.CanShoot) || global.CanShoot(sItem, self)) continue;
                        skip = true;
                        break;
                    }
                }

                if (!skip)
                {
                    _shootingItem = sItem;
                    _shootingPlayer = self;
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn("[item] CanShoot 异常：" + e.Message);
        }

        try
        {
            if (!skip) orig(self, i, sItem, weaponDamage, withAudioVisualFeedback);
        }
        finally
        {
            _shootingItem = null;
            _shootingPlayer = null;
        }
    }

    /// <summary>发射拦截：先改参数再问 Shoot；Shoot 否决时返回无槽位哨兵。</summary>
    private int NewProjectile(On.Terraria.Projectile.orig_NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float_NewProjectileModifier orig,
                              IEntitySource spawnSource, float x, float y, float speedX, float speedY,
                              int type, int damage, float knockBack, int owner,
                              float ai0, float ai1, float ai2, NewProjectileModifier modifier)
    {
        try
        {
            var item = _shootingItem;
            if (item is null || !IsModItem(item))
                return orig(spawnSource, x, y, speedX, speedY, type, damage, knockBack, owner, ai0, ai1, ai2, modifier);

            // 先清上下文：作者在 Shoot 里自己发弹时不能再吃同一份上下文（否则无限递归）。
            var player = _shootingPlayer ?? Main.player[Main.myPlayer];
            _shootingItem = null;
            _shootingPlayer = null;

            var stats = new ModItem.ShootStats
            {
                Position = new Vector2(x, y),
                Velocity = new Vector2(speedX, speedY),
                Type = type,
                Damage = damage,
                KnockBack = knockBack,
            };

            var mod = ModOf(item);
            if (mod is not null && mod.Plan.Has(HookId.ModifyShootStats)) mod.ModifyShootStats(item, player, stats);

            foreach (var global in GlobalItemRegistry.For(item, HookId.ModifyShootStats))
            {
                if (global.Plan.Has(HookId.ModifyShootStats)) global.ModifyShootStats(item, player, stats);
            }

            var shoot = true;
            if (mod is not null && mod.Plan.Has(HookId.Shoot))
                shoot = mod.Shoot(item, player, stats.Position, stats.Velocity, stats.Type, stats.Damage, stats.KnockBack);

            foreach (var global in GlobalItemRegistry.For(item, HookId.Shoot))
            {
                if (!shoot || !global.Plan.Has(HookId.Shoot)) continue;
                if (!global.Shoot(item, player, stats.Position, stats.Velocity, stats.Type, stats.Damage,
                        stats.KnockBack, spawnSource)) shoot = false;
            }

            if (!shoot) return 1000;

            return orig(spawnSource, stats.Position.X, stats.Position.Y, stats.Velocity.X, stats.Velocity.Y,
                        stats.Type, stats.Damage, stats.KnockBack, owner, ai0, ai1, ai2, modifier);
        }
        catch (Exception e)
        {
            Log.Warn("[item] Shoot 异常：" + e.Message);
            return orig(spawnSource, x, y, speedX, speedY, type, damage, knockBack, owner, ai0, ai1, ai2, modifier);
        }
    }
}

/// <summary>物品编号相关的原生表：物品、Lang、贴图、配方、前缀饰品槽……</summary>
internal static class ItemTables
{
    private static readonly Type[] Types =
    [
        typeof(Terraria.ID.ItemID.Sets),
        typeof(Terraria.ID.ItemID),
        typeof(Terraria.ID.AmmoID.Sets),
        typeof(Terraria.Item),
        typeof(Terraria.Lang),
        typeof(Terraria.Main),
        typeof(Terraria.Player),
        typeof(Terraria.Recipe),
        typeof(Terraria.GameContent.TextureAssets),
        typeof(Terraria.GameContent.EmergencyStacking),
        typeof(Terraria.GameContent.Items.ItemVariants),
        typeof(Terraria.GameContent.Prefixes.PrefixLegacy.ItemSets),
        typeof(Terraria.DataStructures.ArmorSetBonuses),
        typeof(Terraria.UI.ItemSorting),
        typeof(Terraria.UI.ItemSlot),
        typeof(VirtualControllerInputState),
    ];

    internal static readonly TableSet Set = new("物品", () => Terraria.ID.ItemID.Count, Types);

}
