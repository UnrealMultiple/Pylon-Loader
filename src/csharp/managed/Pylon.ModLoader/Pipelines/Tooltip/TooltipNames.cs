using Terraria;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>
/// 行命名：先看角色（位置标记直接给出），再看词缀，再看物品自己的 ToolTip 顺序，最后按原版文本比对。
/// 没有静态的原版文本表——那样每跟一次游戏版本就要重对几百行。
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

    private static string Tip(int index)
    {
        try
        {
            return Language.GetTextValue("LegacyTooltip." + index);
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
            return Language.GetTextValue(key);
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

    /// <summary>给一行定名字；role 非空时直接按角色命名。</summary>
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
        if (Ends(text, Tip(23)) || Ends(text, Tip(60))) return "Equipable";
        if (Ends(text, Tip(24))) return "Vanity";
        if (Ends(text, Tip(33))) return "Placeable";
        if (Ends(text, Tip(34))) return "Ammo";
        if (Ends(text, Tip(35))) return "Consumable";
        if (Ends(text, Tip(36))) return "Material";
        if (Is(text, Key("GameUI.Expert"))) return "Expert";
        if (Is(text, Key("GameUI.Master"))) return "Master";
        if (Fits(text, Key("CommonItemTooltip.RestoresLife")) || Fits(text, Key("CommonItemTooltip.RestoresLifeRange"))) return "HealLife";
        if (Fits(text, Key("CommonItemTooltip.RestoresMana"))) return "HealMana";
        if (Fits(text, Key("CommonItemTooltip.UsesMana"))) return "UseMana";
        if (Fits(text, Key("CommonItemTooltip.SecondDuration")) || Fits(text, Key("CommonItemTooltip.MinuteDuration"))) return "BuffTime";

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

        return "Prefix";
    }
}
