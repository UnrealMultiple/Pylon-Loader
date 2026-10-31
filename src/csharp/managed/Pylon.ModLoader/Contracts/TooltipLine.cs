using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader;

/// <summary>tooltip 的一行；`new TooltipLine(Mod, 名字, 文字)` 也行（同 tModLoader）。游戏的行 Mod 是 'Terraria'，名字是 tModLoader 的（'ItemName'、'Damage'、'Tooltip0'…）。Hide() 把行拿掉。</summary>
public class TooltipLine
{
    /// <summary>Mod 留空（游戏的行就是 'Terraria'）；文字缺省空。</summary>
    public TooltipLine(string name, string text = "") : this("", name, text) { }

    /// <summary>mod 直接传名字（游戏的行传 'Terraria'）。</summary>
    public TooltipLine(string mod, string name, string text)
    {
        Mod = mod ?? "";
        Name = name ?? "";
        Text = text ?? "";
        OverrideColor = null;
        IsModifier = false;
        IsModifierBad = false;
        OneDropLogo = false;
        Visible = true;
    }

    /// <summary>mod 传 Mod 对象：取它的 Name。</summary>
    public TooltipLine(Mod mod, string name, string text) : this(mod?.Name ?? "", name, text) { }

    /// <summary>这行属于哪个 mod；游戏的行是 'Terraria'。</summary>
    public string Mod { get; set; }

    /// <summary>行名（tModLoader 的：'ItemName'、'Damage'、'Tooltip0'…）。</summary>
    public string Name { get; set; }

    /// <summary>行的文字。</summary>
    public string Text { get; set; }

    /// <summary>整行的颜色；null = 交给游戏按行角色定。</summary>
    public Color? OverrideColor { get; set; }

    /// <summary>是前缀的加成行（画成绿色）。</summary>
    public bool IsModifier { get; set; }

    /// <summary>是前缀的减益行（画成红色）。</summary>
    public bool IsModifierBad { get; set; }

    /// <summary>这一行就是 One Drop 的 logo。</summary>
    public bool OneDropLogo { get; set; }

    /// <summary>还画不画（Hide() 把它置 false，管线按它滤行）。</summary>
    public bool Visible { get; set; }

    /// <summary>这行的角色（画线颜色按它取，ModifyTooltips 之后再定色）：name/setBonus/research/shared/materials/price；null = 普通行（白色）。</summary>
    internal string Role { get; set; } = null!;

    /// <summary>把这行从 tooltip 里拿掉。</summary>
    public void Hide() => Visible = false;

    /// <summary>把一段文字染成 [c/RRGGBB:文字] 标签。</summary>
    public static string ColorTag(string text, Color color) => "[c/" + Hex(color) + ":" + text + "]";

    /// <summary>颜色转 6 位大写 RRGGBB（tooltip 标签只认 6 位）。</summary>
    private static string Hex(Color color) => $"{color.R:X2}{color.G:X2}{color.B:X2}";
}
