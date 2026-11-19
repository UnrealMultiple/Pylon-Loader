using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.Tables;
using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader.Pipelines;
/// <summary>物品管线：把 ModItem 接进"用/挥/装备/射"链；分派为本物品先、GlobalItem 后，射击上下文是实例字段。</summary>
internal sealed class ItemPipeline : ILoaderModule, ILoaderModuleRefresh
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

    /// <summary>item 管线的安装门：条件与订阅同居一门，由 GateHub 自动发现与派发。</summary>
    internal static class Gates
    {
        internal struct Core : IGate
        {
            public static bool Wanted() => true;

            public static void Install() => Instance.InstallCore();
        }

        internal struct CanUse : IGate
        {
            public static bool Wanted() =>
                HookGuard.Wants<ModItem>(HookId.CanUseItem) || GlobalItemRegistry.AnyWith(HookId.CanUseItem);

            public static void Install() => On.Terraria.Player.ItemCheck_CheckCanUse_Inner += Instance.CanUse;
        }

        internal struct StartUse : IGate
        {
            public static bool Wanted() =>
                HookGuard.Wants<ModItem>(HookId.UseItem) || GlobalItemRegistry.AnyWith(HookId.UseItem);

            public static void Install() => On.Terraria.Player.ItemCheck_StartActualUse += Instance.Use;
        }

        internal struct UseStyle : IGate
        {
            public static bool Wanted() =>
                HookGuard.Wants<ModItem>(HookId.UseStyle, HookId.HoldItem) ||
                GlobalItemRegistry.AnyWith(HookId.UseStyle, HookId.HoldItem);

            public static void Install() => On.Terraria.Player.ItemCheck_ApplyUseStyle += Instance.ApplyUseStyle;
        }

        internal struct HoldStyle : IGate
        {
            public static bool Wanted() =>
                HookGuard.Wants<ModItem>(HookId.HoldStyle, HookId.HoldItem, HookId.HoldoutOffset) ||
                GlobalItemRegistry.AnyWith(HookId.HoldStyle, HookId.HoldItem, HookId.HoldoutOffset);

            public static void Install() => On.Terraria.Player.ItemCheck_ApplyHoldStyle += Instance.ApplyHoldStyle;
        }

        internal struct Equip : IGate
        {
            public static bool Wanted() =>
                HookGuard.Wants<ModItem>(HookId.UpdateEquip, HookId.UpdateAccessory, HookId.UpdateVanity) ||
                GlobalItemRegistry.AnyWith(HookId.UpdateEquip, HookId.UpdateAccessory, HookId.UpdateVanity);

            public static void Install()
            {
                On.Terraria.Player.ApplyEquipFunctional += Instance.EquipFunctional;
                On.Terraria.Player.ApplyEquipVanity += Instance.EquipVanity;
                On.Terraria.Player.GrantArmorBenefits += Instance.GrantArmorBenefits;
            }
        }

        internal struct Inventory : IGate
        {
            public static bool Wanted() =>
                HookGuard.Wants<ModItem>(HookId.UpdateInventory) || GlobalItemRegistry.AnyWith(HookId.UpdateInventory);

            public static void Install() => On.Terraria.Player.UpdateEquips += Instance.UpdateInventory;
        }

        internal struct Shoot : IGate
        {
            public static bool Wanted() => ShootWanted();

            public static void Install()
            {
                On.Terraria.Player.ItemCheck_Shoot += Instance.ShootGate;
                On.Terraria.Projectile.NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float_NewProjectileModifier += Instance.NewProjectile;
            }
        }

        internal struct Alpha : IGate
        {
            public static bool Wanted() =>
                HookGuard.Wants<ModItem>(HookId.GetAlpha) || GlobalItemRegistry.AnyWith(HookId.GetAlpha);

            public static void Install() => On.Terraria.Item.GetAlpha += GetAlpha;
        }

        internal struct Angler : IGate
        {
            public static bool Wanted() => ModContent.GetContent<ModItem>().Count > 0;

            public static void Install() => ItemAnglerQuest.Install();
        }

        internal struct Ammo : IGate
        {
            public static bool Wanted() => AmmoWanted();

            public static void Install() => ItemAmmoHooks.Install();
        }

        internal struct Heal : IGate
        {
            public static bool Wanted() => HealWanted();

            public static void Install() => ItemHealHooks.Install();
        }

        internal struct Pvp : IGate
        {
            public static bool Wanted() => ItemPvpHooks.AnyActive();

            public static void Install() => ItemPvpHooks.Install();
        }

        internal struct Pickup : IGate
        {
            public static bool Wanted() => PickupWanted();

            public static void Install() => ItemPickupHooks.Install();
        }

        internal struct Sorting : IGate
        {
            public static bool Wanted() => ModContent.GetContent<ModItem>().Count > 0;

            public static void Install() => ItemSortingHooks.Install();
        }

        internal struct Save : IGate
        {
            public static bool Wanted() =>
                ModContent.GetContent<ModItem>().Count > 0 || ModContent.GetContent<ModPrefix>().Count > 0;

            public static void Install() => ItemSavePipeline.Instance.Install();
        }

        internal struct Timing : IGate
        {
            public static bool Wanted() => TimingWanted();

            public static void Install() => ItemTimingHooks.Install();
        }

        internal struct Frame : IGate
        {
            public static bool Wanted() =>
                HookGuard.Wants<ModItem>(HookId.UseItemFrame, HookId.HoldItemFrame) ||
                GlobalItemRegistry.AnyWith(HookId.UseItemFrame, HookId.HoldItemFrame);

            public static void Install() => ItemFrameHooks.Install();
        }

        internal struct Mana : IGate
        {
            public static bool Wanted() => ManaWanted();

            public static void Install() => ItemManaHooks.Install();
        }

        internal struct Hitbox : IGate
        {
            public static bool Wanted() => HitboxWanted();

            public static void Install() => ItemHitboxHooks.Install();
        }

        internal struct EquipUi : IGate
        {
            public static bool Wanted() => EquipUiWanted();

            public static void Install() => ItemEquipHooks.Install();
        }
    }

    public bool Install() => GateHub<ItemPipeline>.Run();

    private void InstallCore()
    {
        RefreshGlobals();
        _defaults.Install();
        ItemTml.Install();
        Log.Info("[item] 已挂（SetDefaults 分派枢纽；使用 / 装备 / 射击链按覆写分装）");
    }

    public void Refresh() => RefreshGlobals();

    private static void RefreshGlobals()
    {
        _globalShoot = GlobalItemRegistry.AnyWith(HookId.CanShoot, HookId.ModifyShootStats, HookId.Shoot);
        ItemTimingHooks.RefreshGlobals();
        ItemManaHooks.RefreshGlobals();
        ItemHitboxHooks.RefreshGlobals();
        ItemFrameHooks.RefreshGlobals();
    }

    private static bool PlayerWants(params ReadOnlySpan<HookId> hooks)
    {
        foreach (var player in PlayerPipeline.Instance.Binding.Templates)
        {
            var plan = HookScan.Of(player.GetType(), typeof(ModPlayer));
            foreach (var hook in hooks)
            {
                if (plan.Has(hook)) return true;
            }
        }

        return false;
    }

    private static bool ShootWanted() =>
        HookGuard.Wants<ModItem>(HookId.CanShoot, HookId.ModifyShootStats, HookId.Shoot) ||
        GlobalItemRegistry.AnyWith(HookId.CanShoot, HookId.ModifyShootStats, HookId.Shoot) ||
        PlayerWants(HookId.CanShoot, HookId.ModifyShootStats, HookId.Shoot);

    private static bool AmmoWanted() =>
        HookGuard.Wants<ModItem>(HookId.CanChooseAmmo, HookId.CanBeChosenAsAmmo, HookId.NeedsAmmo,
            HookId.PickAmmo, HookId.CanConsumeAmmo, HookId.CanBeConsumedAsAmmo, HookId.OnConsumeAmmo, HookId.OnConsumedAsAmmo) ||
        GlobalItemRegistry.AnyWith(HookId.CanChooseAmmo, HookId.CanBeChosenAsAmmo, HookId.NeedsAmmo,
            HookId.PickAmmo, HookId.CanConsumeAmmo, HookId.CanBeConsumedAsAmmo, HookId.OnConsumeAmmo, HookId.OnConsumedAsAmmo) ||
        PlayerWants(HookId.CanConsumeAmmo, HookId.OnConsumeAmmo);

    private static bool HealWanted() =>
        HookGuard.Wants<ModItem>(HookId.GetHealLife, HookId.GetHealMana) ||
        GlobalItemRegistry.AnyWith(HookId.GetHealLife, HookId.GetHealMana) ||
        PlayerWants(HookId.GetHealLife, HookId.GetHealMana);

    private static bool PickupWanted() =>
        HookGuard.Wants<ModItem>(HookId.CanPickup, HookId.GrabRange, HookId.GrabStyle, HookId.ItemSpace, HookId.OnPickup) ||
        GlobalItemRegistry.AnyWith(HookId.CanPickup, HookId.GrabRange, HookId.GrabStyle, HookId.ItemSpace, HookId.OnPickup);

    private static bool TimingWanted() =>
        HookGuard.Wants<ModItem>(HookId.UseTimeMultiplier, HookId.UseAnimationMultiplier, HookId.UseSpeedMultiplier,
            HookId.UseAnimation, HookId.CanAutoReuseItem, HookId.AltFunctionUse) ||
        GlobalItemRegistry.AnyWith(HookId.UseTimeMultiplier, HookId.UseAnimationMultiplier, HookId.UseSpeedMultiplier,
            HookId.UseAnimation, HookId.CanAutoReuseItem, HookId.AltFunctionUse) ||
        PlayerWants(HookId.UseTimeMultiplier, HookId.UseAnimationMultiplier, HookId.UseSpeedMultiplier, HookId.CanAutoReuseItem);

    private static bool ManaWanted() =>
        HookGuard.Wants<ModItem>(HookId.ModifyManaCost, HookId.OnMissingMana, HookId.OnConsumeMana) ||
        GlobalItemRegistry.AnyWith(HookId.ModifyManaCost, HookId.OnMissingMana, HookId.OnConsumeMana) ||
        PlayerWants(HookId.ModifyManaCost, HookId.OnMissingMana, HookId.OnConsumeMana);

    private static bool HitboxWanted() =>
        HookGuard.Wants<ModItem>(HookId.UseItemHitbox, HookId.MeleeEffects) ||
        GlobalItemRegistry.AnyWith(HookId.UseItemHitbox, HookId.MeleeEffects) ||
        PlayerWants(HookId.MeleeEffects);

    private static bool EquipUiWanted() =>
        HookGuard.Wants<ModItem>(HookId.CanEquipAccessory, HookId.CanAccessoryBeEquippedWith,
            HookId.DrawArmorColor, HookId.ArmorArmGlowMask) ||
        GlobalItemRegistry.AnyWith(HookId.CanEquipAccessory, HookId.CanAccessoryBeEquippedWith,
            HookId.DrawArmorColor, HookId.ArmorArmGlowMask);

    private static bool IsModItem(Item? item) =>
        item is not null && ModContent.IsCustom(ContentKind.Item, item.type);

    private ModItem? ModOf(Item item) => _binding.InstanceOf(item);

    internal ModItem? InstanceOf(Item item) => _binding.InstanceOf(item);

    internal void Restore(Item item, ModItem? mod) => _binding.Restore(item, mod);

    private static Color GetAlpha(On.Terraria.Item.orig_GetAlpha orig, Item self, Color newColor)
    {
        if (self is null || self.type <= 0) return orig(self, newColor);

        foreach (var global in GlobalItemRegistry.For(self, HookId.GetAlpha))
        {
            try
            {
                if (global.GetAlpha(self, newColor) is Color changed) return changed;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[item] {global.FullKey}.GetAlpha 异常：{Safe.Describe(e)}");
            }
        }

        if (ModContent.IsCustom(ContentKind.Item, self.type))
        {
            var mod = Instance.InstanceOf(self);
            if (mod is not null && mod.Plan.Has(HookId.GetAlpha))
            {
                try
                {
                    if (mod.GetAlpha(newColor) is Color changed) return changed;
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[item] {mod.FullKey}.GetAlpha 异常：{Safe.Describe(e)}");
                }
            }
        }

        return orig(self, newColor);
    }

    private bool CanUse(On.Terraria.Player.orig_ItemCheck_CheckCanUse_Inner orig,
                        Player self, Item sItem, bool ignoreCursed)
    {
        try
        {
            if (IsModItem(sItem))
            {
                var mod = ModOf(sItem);
                if (mod is not null && mod.Plan.Has(HookId.CanUseItem) && !mod.CanUseItem(self)) return false;

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

            bool? used = null;

            foreach (var global in GlobalItemRegistry.For(sItem, HookId.UseItem))
            {
                if (!global.Plan.Has(HookId.UseItem)) continue;
                var value = global.UseItem(sItem, self);
                if (value.HasValue && used != false) used = value.Value;
            }

            var mod = ModOf(sItem);
            if (mod is not null && mod.Plan.Has(HookId.UseItem))
            {
                var value = mod.UseItem(self);
                if (value.HasValue && used != false) used = value.Value;
            }

            if (used == true) ApplyItemTime(self, sItem);
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
                if (mod.Plan.Has(HookId.UseStyle)) mod.UseStyle(self, heldItemFrame);
                if (mod.Plan.Has(HookId.HoldItem)) mod.HoldItem(self);
            }

            foreach (var global in GlobalItemRegistry.For(sItem, HookId.UseStyle))
            {
                if (global.Plan.Has(HookId.UseStyle)) global.UseStyle(sItem, self, heldItemFrame);
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

    private void ApplyHoldStyle(On.Terraria.Player.orig_ItemCheck_ApplyHoldStyle orig,
                                Player self, float mountOffset, Item sItem, Rectangle heldItemFrame)
    {
        orig(self, mountOffset, sItem, heldItemFrame);

        try
        {
            if (sItem is null || sItem.type <= 0) return;

            var mod = ModOf(sItem);
            if (mod is not null && mod.Plan.Has(HookId.HoldoutOffset))
            {
                try { ApplyHoldout(self, mod.HoldoutOffset()); }
                catch (Exception e) { Log.Warn("[item] HoldoutOffset 异常：" + e.Message); }
            }

            foreach (var global in GlobalItemRegistry.For(sItem, HookId.HoldoutOffset))
            {
                try { ApplyHoldout(self, global.HoldoutOffset(sItem.type)); }
                catch (Exception e) { Log.Warn("[item] Global HoldoutOffset 异常：" + e.Message); }
            }

            if (!IsModItem(sItem) && GlobalItemRegistry.For(sItem, HookId.HoldStyle).Count == 0 &&
                GlobalItemRegistry.For(sItem, HookId.HoldItem).Count == 0)
            {
                return;
            }

            if (mod is not null)
            {
                if (mod.Plan.Has(HookId.HoldStyle)) mod.HoldStyle(self, heldItemFrame);
                if (mod.Plan.Has(HookId.HoldItem)) mod.HoldItem(self);
            }

            foreach (var global in GlobalItemRegistry.For(sItem, HookId.HoldStyle))
            {
                if (global.Plan.Has(HookId.HoldStyle)) global.HoldStyle(sItem, self, heldItemFrame);
            }

            foreach (var global in GlobalItemRegistry.For(sItem, HookId.HoldItem))
            {
                if (global.Plan.Has(HookId.HoldItem)) global.HoldItem(sItem, self);
            }
        }
        catch (Exception e)
        {
            Log.Warn("[item] HoldStyle 异常：" + e.Message);
        }
    }

    private static void ApplyHoldout(Player player, Vector2? offset)
    {
        if (offset is not { } o || (o.X == 0 && o.Y == 0)) return;
        float x = o.X * player.direction, y = o.Y * player.gravDir;
        float cos = MathF.Cos(player.itemRotation), sin = MathF.Sin(player.itemRotation);
        var location = player.itemLocation;
        location.X += x * cos - y * sin;
        location.Y += x * sin + y * cos;
        player.itemLocation = location;
    }

    private void EquipFunctional(On.Terraria.Player.orig_ApplyEquipFunctional orig,
                                 Player self, int itemSlot, Item currentItem)
    {
        orig(self, itemSlot, currentItem);

        try
        {
            if (currentItem is null || currentItem.IsAir) return;

            var hidden = self.hideVisibleAccessory;
            var hide = hidden is not null && itemSlot >= 0 && itemSlot < hidden.Length && hidden[itemSlot];

            var mod = IsModItem(currentItem) ? ModOf(currentItem) : null;
            if (mod is not null)
            {
                if (mod.Plan.Has(HookId.UpdateEquip)) mod.UpdateEquip(self);
                if (currentItem.accessory && mod.Plan.Has(HookId.UpdateAccessory)) mod.UpdateAccessory(self, hide);
            }

            foreach (var global in GlobalItemRegistry.For(currentItem, HookId.UpdateEquip))
            {
                if (global.Plan.Has(HookId.UpdateEquip)) global.UpdateEquip(currentItem, self);
            }

            if (currentItem.accessory)
            {
                foreach (var global in GlobalItemRegistry.For(currentItem, HookId.UpdateAccessory))
                {
                    if (global.Plan.Has(HookId.UpdateAccessory)) global.UpdateAccessory(currentItem, self, hide);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn("[item] UpdateEquip 异常：" + e.Message);
        }
    }

    private void GrantArmorBenefits(On.Terraria.Player.orig_GrantArmorBenefits orig, Player self, Item armorPiece)
    {
        orig(self, armorPiece);

        try
        {
            if (armorPiece is null || armorPiece.IsAir) return;

            var mod = IsModItem(armorPiece) ? ModOf(armorPiece) : null;
            if (mod is not null && mod.Plan.Has(HookId.UpdateEquip)) mod.UpdateEquip(self);

            foreach (var global in GlobalItemRegistry.For(armorPiece, HookId.UpdateEquip))
            {
                if (global.Plan.Has(HookId.UpdateEquip)) global.UpdateEquip(armorPiece, self);
            }
        }
        catch (Exception e)
        {
            Log.Warn("[item] UpdateEquip 异常：" + e.Message);
        }
    }

    internal static int ApplyEquipVanityCalls;

    private void EquipVanity(On.Terraria.Player.orig_ApplyEquipVanity orig,
                             Player self, int itemSlot, Item currentItem)
    {
        ApplyEquipVanityCalls++;
        orig(self, itemSlot, currentItem);

        try
        {
            if (currentItem is null || currentItem.IsAir) return;

            var mod = IsModItem(currentItem) ? ModOf(currentItem) : null;
            if (mod is not null && mod.Plan.Has(HookId.UpdateVanity)) mod.UpdateVanity(self);

            foreach (var global in GlobalItemRegistry.For(currentItem, HookId.UpdateVanity))
            {
                if (global.Plan.Has(HookId.UpdateVanity)) global.UpdateVanity(currentItem, self);
            }
        }
        catch (Exception e)
        {
            Log.Warn("[item] UpdateVanity 异常：" + e.Message);
        }
    }

    private void UpdateInventory(On.Terraria.Player.orig_UpdateEquips orig, Player self, int i)
    {
        orig(self, i);

        var inventory = self.inventory;
        if (inventory is null) return;

        for (var slot = 0; slot < inventory.Length; slot++)
        {
            var item = inventory[slot];
            if (!IsModItem(item)) continue;

            try
            {
                var mod = ModOf(item);
                if (mod is not null && mod.Plan.Has(HookId.UpdateInventory))
                    mod.UpdateInventory(self);

                foreach (var global in GlobalItemRegistry.For(item, HookId.UpdateInventory))
                {
                    if (!global.Plan.Has(HookId.UpdateInventory)) continue;
                    try
                    {
                        global.UpdateInventory(item, self);
                    }
                    catch (Exception e)
                    {
                        Log.Warn("[item] Global UpdateInventory 异常：" + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("[item] UpdateInventory 异常：" + e.Message);
            }
        }
    }

    private static bool _globalShoot;

    private static bool WantedShoot(Item? sItem) =>
        sItem is not null && sItem.type > 0 && (_globalShoot || ModContent.IsCustom(ContentKind.Item, sItem.type));

    private void ShootGate(On.Terraria.Player.orig_ItemCheck_Shoot orig,
                           Player self, int i, Item sItem, int weaponDamage, bool withAudioVisualFeedback)
    {
        try
        {
            if (WantedShoot(sItem) && !CanShoot(sItem, self))
            {
                self.ApplyItemTime(sItem);
                return;
            }
        }
        catch (Exception e)
        {
            Log.Warn("[item] CanShoot 异常：" + e.Message);
        }

        try
        {
            _shootingItem = sItem;
            _shootingPlayer = self;
            orig(self, i, sItem, weaponDamage, withAudioVisualFeedback);
        }
        finally
        {
            _shootingItem = null;
            _shootingPlayer = null;
        }
    }

    private static bool CanShoot(Item item, Player player)
    {
        var mods = PlayerPipeline.Instance.ModsOf(player);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
            if (!mod.HookEnabled(HookId.CanShoot)) continue;

            try
            {
                if (!mod.CanShoot(item)) return false;
            }
            catch (Exception e) { ReportAuthor(PlayerKey(mod), HookId.CanShoot, e); }
        }

        foreach (var g in GlobalItemRegistry.For(item, HookId.CanShoot))
        {
            try
            {
                if (!g.CanShoot(item, player)) return false;
            }
            catch (Exception e) { ReportAuthor(g.FullKey, HookId.CanShoot, e); }
        }

        if (ItemPipeline.Instance.InstanceOf(item) is { } m && m.Plan.Has(HookId.CanShoot))
        {
            try { return m.CanShoot(player); }
            catch (Exception e) { ReportAuthor(m.FullKey, HookId.CanShoot, e); }
        }

        return true;
    }

    private int NewProjectile(On.Terraria.Projectile.orig_NewProjectile_IEntitySource_float_float_float_float_int_int_float_int_float_float_float_NewProjectileModifier orig,
                              IEntitySource spawnSource, float x, float y, float speedX, float speedY,
                              int type, int damage, float knockBack, int owner,
                              float ai0, float ai1, float ai2, NewProjectileModifier modifier)
    {
        var item = _shootingItem;
        var player = _shootingPlayer;
        if (item is null || player is null || player.whoAmI != owner)
            return orig(spawnSource, x, y, speedX, speedY, type, damage, knockBack, owner, ai0, ai1, ai2, modifier);

        _shootingItem = null;
        _shootingPlayer = null;
        try
        {
            var position = new Vector2(x, y);
            var velocity = new Vector2(speedX, speedY);

            if (InstanceOf(item) is { } m && m.Plan.Has(HookId.ModifyShootStats))
            {
                try { m.ModifyShootStats(player, ref position, ref velocity, ref type, ref damage, ref knockBack); }
                catch (Exception e) { ReportAuthor(m.FullKey, HookId.ModifyShootStats, e); }
            }

            foreach (var global in GlobalItemRegistry.For(item, HookId.ModifyShootStats))
            {
                try { global.ModifyShootStats(item, player, ref position, ref velocity, ref type, ref damage, ref knockBack); }
                catch (Exception e) { ReportAuthor(global.FullKey, HookId.ModifyShootStats, e); }
            }

            var playerMods = PlayerPipeline.Instance.ModsOf(player);
            for (var i = 0; i < playerMods.Length; i++)
            {
                var mod = playerMods[i];
                if (!mod.HookEnabled(HookId.ModifyShootStats)) continue;

                try { mod.ModifyShootStats(item, ref position, ref velocity, ref type, ref damage, ref knockBack); }
                catch (Exception e) { ReportAuthor(PlayerKey(mod), HookId.ModifyShootStats, e); }
            }

            var source = spawnSource?.TryCast<Terraria.DataStructures.EntitySource_ItemUse_WithAmmo>();

            if (!Shoot(item, player, source, position, velocity, type, damage, knockBack)) return 1000;

            return orig(spawnSource, position.X, position.Y, velocity.X, velocity.Y,
                        type, damage, knockBack, owner, ai0, ai1, ai2, modifier);
        }
        finally
        {
            _shootingItem = item;
            _shootingPlayer = player;
        }
    }

    private static bool Shoot(Item item, Player player, Terraria.DataStructures.EntitySource_ItemUse_WithAmmo? source,
                              Vector2 position, Vector2 velocity, int type, int damage, float knockback)
    {
        var result = true;

        var mods = PlayerPipeline.Instance.ModsOf(player);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
            if (!mod.HookEnabled(HookId.Shoot)) continue;

            try { result &= mod.Shoot(item, source, position, velocity, type, damage, knockback); }
            catch (Exception e) { ReportAuthor(PlayerKey(mod), HookId.Shoot, e); }
        }

        foreach (var g in GlobalItemRegistry.For(item, HookId.Shoot))
        {
            try { result &= g.Shoot(item, player, source, position, velocity, type, damage, knockback); }
            catch (Exception e) { ReportAuthor(g.FullKey, HookId.Shoot, e); }
        }

        if (!result) return false;

        if (ItemPipeline.Instance.InstanceOf(item) is { } m && m.Plan.Has(HookId.Shoot))
        {
            try { return m.Shoot(player, source, position, velocity, type, damage, knockback); }
            catch (Exception e) { ReportAuthor(m.FullKey, HookId.Shoot, e); }
        }

        return true;
    }

    private static string PlayerKey(ModPlayer mod) => $"{mod.Mod?.Name}/{mod.GetType().Name}";

    private static void ReportAuthor(string key, HookId hook, Exception e) =>
        ContentErrors.Report($"[item] {key}.{hook} 异常：{Safe.Describe(e)}");
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
