using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using Terraria.DataStructures;
using CheatMenu.Infra;

namespace CheatMenu.Features;

/// <summary>
/// 多套装备生效。1:1 对应原版 <c>LoadoutEffectTool</c>：
/// UpdateEquips 前对其它勾选套装直接调用 GrantArmorBenefits / GrantPrefixBenefits /
/// ApplyEquipFunctional / 翅膀收益，UpdateArmorSets 前对其它套装调用套装奖励委托；
/// 原生调用后按并集还原三个 acc 标志。
/// </summary>
public sealed class LoadoutFeature : CheatFeature
{
    private const int SummaryRefreshFrames = 30;

    private readonly string[] _summary = ["", "", ""];
    private int _summaryCountdown;

    public override string Id => "player.loadout";
    public override string Title => "多套装备生效";
    public override CheatCategory Category => CheatCategory.Main;
    public override string SubPage => "主要";

    public override void Install()
    {
        Instance = this;
        On.Terraria.Player.UpdateEquips += UpdateEquips;
        On.Terraria.Player.UpdateArmorSets += UpdateArmorSets;
    }

    private static bool Enabled => CheatFeatureHost.Config is { LoadoutEffects: true };

    private static bool LoadoutOn(int index)
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return false;
        return index switch
        {
            0 => cfg.Loadout0,
            1 => cfg.Loadout1,
            _ => cfg.Loadout2,
        };
    }

    // ---------------- UpdateEquips ----------------

    private static void UpdateEquips(On.Terraria.Player.orig_UpdateEquips orig, Player self, int i)
    {
        if (!Game.IsLocal(self) || !Enabled)
        {
            orig(self, i);
            return;
        }

        var armor = self.armor;
        if (armor is null || armor.Length < 20)
        {
            orig(self, i);
            return;
        }

        var cfg = CheatFeatureHost.Config;
        var current = self.CurrentLoadoutIndex;
        var loadouts = self.Loadouts;

        for (var li = 0; li < 3; li++)
        {
            if (!LoadoutOn(li)) continue;
            var items = GetItems(li, current, armor, loadouts);
            if (items is null || items.Length < 20) continue;

            if (li == current)
            {
                if (cfg is { LoadoutVanity: true }) ApplyVanityItemEffects(self, items);
            }
            else
            {
                ApplyLoadoutItemEffects(self, items, cfg is { LoadoutVanity: true });
            }
        }

        var preWall = self.equippedAnyWallSpeedAcc;
        var preTile = self.equippedAnyTileSpeedAcc;
        var preRange = self.equippedAnyTileRangeAcc;

        orig(self, i);

        self.equippedAnyWallSpeedAcc = preWall | self.equippedAnyWallSpeedAcc;
        self.equippedAnyTileSpeedAcc = preTile | self.equippedAnyTileSpeedAcc;
        self.equippedAnyTileRangeAcc = preRange | self.equippedAnyTileRangeAcc;

        var feature = Instance;
        if (feature is not null && --feature._summaryCountdown <= 0)
        {
            feature._summaryCountdown = SummaryRefreshFrames;
            feature.RefreshSummary(self, current, armor, loadouts);
        }
    }

    // ---------------- UpdateArmorSets ----------------

    private static void UpdateArmorSets(On.Terraria.Player.orig_UpdateArmorSets orig, Player self, int i)
    {
        if (Game.IsLocal(self) && Enabled)
        {
            var armor = self.armor;
            if (armor is not null && armor.Length >= 20)
            {
                var cfg = CheatFeatureHost.Config;
                var current = self.CurrentLoadoutIndex;
                var loadouts = self.Loadouts;

                for (var li = 0; li < 3; li++)
                {
                    if (li == current || !LoadoutOn(li)) continue;
                    var items = GetItems(li, current, armor, loadouts);
                    if (items is null || items.Length < 3) continue;
                    ApplySetBonusFromTypes(self, ItemTypeOrZero(items[0]), ItemTypeOrZero(items[1]),
                        ItemTypeOrZero(items[2]));
                }

                if (cfg is { LoadoutVanity: true })
                {
                    for (var li = 0; li < 3; li++)
                    {
                        if (!LoadoutOn(li)) continue;
                        var items = GetItems(li, current, armor, loadouts);
                        if (items is null || items.Length < 13) continue;
                        ApplySetBonusFromTypes(self, ItemTypeOrZero(items[10]), ItemTypeOrZero(items[11]),
                            ItemTypeOrZero(items[12]));
                    }
                }
            }
        }

        orig(self, i);
    }

    // ---------------- 装备收益 ----------------

    private static Il2CppReferenceArray<Item> GetItems(int loadout, int current,
        Il2CppReferenceArray<Item> activeArmor, Il2CppReferenceArray<EquipmentLoadout> loadouts)
    {
        if (loadout == current) return activeArmor;
        if (loadouts is not null && loadout >= 0 && loadout < loadouts.Length)
        {
            var entry = loadouts[loadout];
            if (entry is not null) return entry.Armor;
        }
        return null;
    }

    private static void ApplyLoadoutItemEffects(Player player, Il2CppReferenceArray<Item> items, bool vanity)
    {
        if (items.Length < 20) return;

        for (var slot = 0; slot < 3; slot++)
        {
            var item = items[slot];
            if (item is null || item.IsAir) continue;
            player.GrantArmorBenefits(item);
        }

        for (var slot = 3; slot < 10; slot++)
        {
            var item = items[slot];
            if (item is null || item.IsAir) continue;
            player.GrantPrefixBenefits(item);
            player.GrantArmorBenefits(item);
            player.ApplyEquipFunctional(slot, item);
            ApplyWingBenefits(player, item);
        }

        if (vanity) ApplyVanityItemEffects(player, items);
    }

    private static void ApplyVanityItemEffects(Player player, Il2CppReferenceArray<Item> items)
    {
        if (items.Length < 20) return;

        for (var slot = 10; slot < 20; slot++)
        {
            var item = items[slot];
            if (item is null || item.IsAir) continue;
            player.GrantArmorBenefits(item);
            player.GrantPrefixBenefits(item);
            player.ApplyEquipFunctional(slot, item);
            ApplyWingBenefits(player, item);
        }
    }

    private static void ApplyWingBenefits(Player player, Item item)
    {
        if (item is null) return;
        var wingSlot = item.wingSlot;
        if (wingSlot == -1) return;

        var stats = player.GetWingStats(wingSlot);
        player.wingTimeMax = stats.FlyTime;
        player.wingsLogic = wingSlot;
        player.noFallDmg = true;
        if (wingSlot == 26) player.ignoreWater = true;
    }

    private static void ApplySetBonusFromTypes(Player player, int head, int body, int leg)
    {
        if (head == 0 && body == 0 && leg == 0) return;

        var context = new ArmorSetBonus.QueryContext(player)
        {
            HeadItem = head,
            BodyItem = body,
            LegItem = leg,
        };

        var setBonus = ArmorSetBonuses.GetCompleteSet(context);
        var effect = setBonus?.Effect;
        effect?.Invoke(player);
    }

    private static int ItemTypeOrZero(Item item) =>
        item is null || item.IsAir ? 0 : item.type;

    // ---------------- 套装摘要 ----------------

    public static LoadoutFeature Instance { get; private set; }

    private void RefreshSummary(Player player, int current, Il2CppReferenceArray<Item> activeArmor,
        Il2CppReferenceArray<EquipmentLoadout> loadouts)
    {
        for (var li = 0; li < 3; li++)
        {
            var items = GetItems(li, current, activeArmor, loadouts);
            if (items is null)
            {
                _summary[li] = "（无盔甲）";
                continue;
            }

            var armorNames = "";
            var accessories = 0;
            var length = Math.Min(items.Length, 20);
            for (var slot = 0; slot < length; slot++)
            {
                var item = items[slot];
                if (item is null || item.IsAir) continue;
                if (slot < 3)
                {
                    if (armorNames.Length > 0) armorNames += " / ";
                    armorNames += item.Name ?? "?";
                }
                else if (slot < 10)
                {
                    accessories++;
                }
            }

            if (armorNames.Length == 0) armorNames = "（无盔甲）";
            var suffix = li == current ? "（当前）" : "";
            _summary[li] = $"{armorNames}  · 饰品 {accessories}{suffix}";
        }
    }

    // ---------------- UI ----------------

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("多套装备生效", "LOADOUTS");

        BeginRowCheckbox("多套装备生效", "##loadoutenabled", ref cfg.LoadoutEffects);
        if (cfg.LoadoutEffects)
        {
            BeginRowCheckbox("时装也生效", "##loadoutvanity", ref cfg.LoadoutVanity);

            for (var i = 0; i < 3; i++)
            {
                var on = i switch { 0 => cfg.Loadout0, 1 => cfg.Loadout1, _ => cfg.Loadout2 };
                var next = on;
                ModernUI.BeginRow();
                ModernUI.RowLabel($"装备 {i + 1}", _summary[i]);
                if (ModernUI.Checkbox($"##loadout{i}", ref next))
                {
                    switch (i)
                    {
                        case 0: cfg.Loadout0 = next; break;
                        case 1: cfg.Loadout1 = next; break;
                        default: cfg.Loadout2 = next; break;
                    }
                    CheatFeatureHost.MarkDirty();
                }
                ModernUI.EndRow();
            }
        }

        ModernUI.EndCard();
    }

    private static void BeginRowCheckbox(string label, string id, ref bool value)
    {
        ModernUI.BeginRow();
        ModernUI.RowLabel(label);
        if (ModernUI.Checkbox(id, ref value)) CheatFeatureHost.MarkDirty();
        ModernUI.EndRow();
    }
}