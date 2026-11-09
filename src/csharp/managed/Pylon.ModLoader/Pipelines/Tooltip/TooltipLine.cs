using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader;

/// <summary>
/// tooltip 的一行。游戏的行 Mod 是 "Terraria"，行名按 TooltipNames 定（ItemName / Damage / PrefixDamage / Tooltip3…）。
/// 作者能改 Text、能 Hide()，也能指定整行颜色。
/// </summary>
public class TooltipLine
{
    public TooltipLine(string name, string text = "") : this("", name, text) { }

    public TooltipLine(Mod mod, string name, string text) : this(mod?.Name ?? "", name, text) { }

    public TooltipLine(string mod, string name, string text)
    {
        Mod = mod ?? "";
        Name = name ?? "";
        Text = text ?? "";
    }

    /// <summary>这行属于哪个 mod；游戏的行是 "Terraria"。</summary>
    public string Mod { get; }

    /// <summary>行名（作者靠它找行）。</summary>
    public string Name { get; }

    /// <summary>行的文字（null 当空串写回）。</summary>
    public string Text { get; set; }

    /// <summary>整行的颜色；null = 交给游戏按行角色定。</summary>
    public Color? OverrideColor { get; set; }

    /// <summary>是词缀的加成行（绿）。</summary>
    public bool IsModifier { get; set; }

    /// <summary>是词缀的减益行（红）。</summary>
    public bool IsModifierBad { get; set; }

    /// <summary>这一行画成 One Drop 的 logo。</summary>
    public bool OneDropLogo { get; set; }

    /// <summary>还画不画（Hide() 置 false，管线按它滤行）。</summary>
    public bool Visible { get; set; } = true;

    /// <summary>"Mod/Name"。</summary>
    public string FullName => Mod + "/" + Name;

    /// <summary>行角色（name / logo / research / materials / setBonus / shared；空 = 普通行）。</summary>
    internal string Role { get; set; } = "";

    /// <summary>把这行从 tooltip 里拿掉。</summary>
    public void Hide() => Visible = false;

    /// <summary>把一段文字染成 [c/RRGGBB:...] 标签（游戏自己认得这个标签）。</summary>
    public static string ColorTag(string text, Color color) =>
        "[c/" + $"{color.R:X2}{color.G:X2}{color.B:X2}" + ":" + text + "]";

    public override string ToString() => FullName + ": " + Text;
}
