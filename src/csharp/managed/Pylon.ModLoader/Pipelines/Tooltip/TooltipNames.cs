using Pylon.ModLoader.Logging;
using Terraria;
using Terraria.Localization;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 行命名：先看角色（位置标记直接给出），再看词缀，再看物品自己的 ToolTip 顺序，最后按原版文本比对。
/// 原版文本直接读游戏的语言数组（tip/inter/misc），没有静态的原版文本表——那样每跟一次游戏版本就要重对几百行。
/// </summary>
internal sealed class TooltipNames
{
    private readonly Item _item;
    private readonly List<string> _own = [];
    private int _next;

    internal TooltipNames(Item item)
    {
        _item = item;

        try
        {
            var tooltip = item.ToolTip;
            if (tooltip is null) return;

            var count = tooltip.Lines;
            for (var i = 0; i < count; i++) _own.Add(tooltip.GetLine(i) ?? "");
        }
        catch (Exception e)
        {
            Safe.Report("tooltip.own-lines", e);
        }
    }

    private static string Tip(int index) => At(Lang.tip, index);

    private static string Inter(int index) => At(Lang.inter, index);

    private static string Misc(int index) => At(Lang.misc, index);

    private static string At(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<LocalizedText>? texts, int index)
    {
        try
        {
            if (texts is null || index < 0 || index >= texts.Length) return "";
            var text = texts[index];
            return text is null ? "" : text.Value ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static string Key(string key)
    {
        try
        {
            return Language.GetTextValue(key) ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static bool Is(string text, string expected) => expected.Length > 0 && text == expected;

    private static bool Ends(string text, string suffix) =>
        suffix.Length > 0 && text.EndsWith(suffix, StringComparison.Ordinal);

    private static bool Fits(string text, string template)
    {
        var at = template.IndexOf("{0}", StringComparison.Ordinal);
        if (at < 0) return template.Length > 0 && text == template;

        var head = template[..at];
        var tail = template[(at + 3)..];
        if (tail.IndexOf('{') >= 0) tail = "";

        return text.Length >= head.Length + tail.Length &&
               text.StartsWith(head, StringComparison.Ordinal) &&
               text.EndsWith(tail, StringComparison.Ordinal);
    }

    internal string Of(string text, string role, bool modifier)
    {
        switch (role)
        {
            case "logo": return "OneDropLogo";
            case "research": return Fits(text, Key("CommonItemTooltip.ItemUnlockedByTeammate")) ? "JourneyResearchByTeammate" : "JourneyResearch";
            case "materials": return "CraftingMaterials";
            case "setBonus": return "SetBonus";
            case "shared": return "LoadoutShareHint";
        }

        if (modifier) return Prefix(text);

        if (_next < _own.Count && text == _own[_next]) return "Tooltip" + _next++;

        for (var i = _next; i < _own.Count; i++)
        {
            if (text != _own[i]) continue;

            _next = i + 1;
            return "Tooltip" + i;
        }

        if (Is(text, Tip(56))) return "Favorite";
        if (Is(text, Tip(57))) return "FavoriteDesc";
        if (Is(text, Tip(61))) return "NoSocial";
        for (var t = 6; t <= 13; t++)
        {
            if (Is(text, Tip(t))) return "Speed";
        }
        for (var t = 14; t <= 22; t++)
        {
            if (Is(text, Tip(t))) return "Knockback";
        }
        if (Is(text, Tip(23)) || Is(text, Tip(60))) return "Equipable";
        if (Is(text, Tip(24))) return "Vanity";
        if (Is(text, Tip(33))) return "Placeable";
        if (Is(text, Tip(34))) return "Ammo";
        if (Is(text, Tip(35))) return "Consumable";
        if (Is(text, Tip(36))) return "Material";
        if (Is(text, Tip(59))) return "Tooltip0";
        if (Is(text, Inter(65))) return "Quest";
        if (Is(text, Misc(104))) return "EtherianManaWarning";
        if (Is(text, Misc(40))) return "WellFedExpert";
        if (Is(text, Key("GameUI.Expert"))) return "Expert";
        if (Is(text, Key("GameUI.Master"))) return "Master";
        if (Is(text, Key("GameUI.BaitRequired"))) return "NeedsBait";
        if (Is(text, Key("CommonItemTooltip.Wireable"))) return "Wireable";
        if (Is(text, Key("CommonItemTooltip.Container"))) return "Container";
        if (Is(text, Key("UI.ItemLoadoutShared"))) return "Shared";
        if (Is(text, Key("UI.ItemCannotBePlacedInsideItself"))) return "NoTransfer";
        if (Is(text, Key("CommonItemTooltip.WizardHatDuringAnniversary"))) return "WizardHatDuringAnniversary";
        if (Is(text, Key("CommonItemTooltip.BurningBlock"))) return "BurningBlock";
        if (Is(text, Key("CommonItemTooltip.MechSummonDuringEverything"))) return "MechSummonDuringEverything";
        if (Is(text, Key("CommonItemTooltip.MechdusaSummonNotDuringEverything"))) return "MechdusaSummonNotDuringEverything";

        if (Ends(text, Tip(5)) && _item.damage > 0) return "CritChance";

        var damage = new[] { 2, 3, 4, 53, 55 };
        for (var i = 0; i < damage.Length; i++)
        {
            if (Ends(text, Tip(damage[i])) && _item.damage > 0) return "Damage";
        }

        if (Ends(text, Tip(25))) return "Defense";
        if (Ends(text, Tip(26))) return "PickPower";
        if (Ends(text, Tip(27))) return "AxePower";
        if (Ends(text, Tip(28))) return "HammerPower";
        if (Ends(text, Tip(54))) return "TileBoost";
        if (Tip(52).Length > 0 && text.StartsWith(Tip(52), StringComparison.Ordinal)) return "WandConsumes";
        if (Fits(text, Key("GameUI.PrecentFishingPower"))) return "FishingPower";
        if (Fits(text, Key("GameUI.BaitPower"))) return "BaitPower";
        if (Fits(text, Key("CommonItemTooltip.RestoresLife")) || Fits(text, Key("CommonItemTooltip.RestoresLifeRange"))) return "HealLife";
        if (Fits(text, Key("CommonItemTooltip.RestoresMana"))) return "HealMana";
        if (Fits(text, Key("CommonItemTooltip.UsesMana"))) return "UseMana";
        if (Fits(text, Key("CommonItemTooltip.SecondDuration")) || Fits(text, Key("CommonItemTooltip.MinuteDuration"))) return "BuffTime";
        if (Fits(text, Key("UI.ItemLoadoutSharedFrom"))) return "SharedFrom";
        if (Fits(text, Key("CommonItemTooltip.WireTrigger"))) return "WireTrigger";

        return "BestiaryNotes";
    }

    private string Prefix(string text)
    {
        var accessory = _item.accessory;

        if (Ends(text, Tip(39))) return accessory && !text.Contains('%') ? "PrefixAccDamage" : "PrefixDamage";
        if (Ends(text, Tip(40))) return "PrefixSpeed";
        if (Ends(text, Tip(41))) return "PrefixCritChance";
        if (Ends(text, Tip(42))) return "PrefixUseMana";
        if (Ends(text, Tip(43))) return "PrefixSize";
        if (Ends(text, Tip(44))) return "PrefixShootSpeed";
        if (Ends(text, Tip(45))) return "PrefixKnockback";
        if (Ends(text, Tip(25))) return "PrefixAccDefense";
        if (Ends(text, Tip(31))) return "PrefixAccMaxMana";
        if (Ends(text, Tip(5))) return "PrefixAccCritChance";
        if (Ends(text, Tip(46))) return "PrefixAccMoveSpeed";
        if (Ends(text, Tip(47))) return "PrefixAccMeleeSpeed";
        if (Fits(text, Key("CommonItemTooltip.PrefixArmorPenetration"))) return "PrefixArmorPenetration";
        if (Fits(text, Key("CommonItemTooltip.PrefixTagDamage"))) return "PrefixTagDamage";

        return "Prefix";
    }
}
