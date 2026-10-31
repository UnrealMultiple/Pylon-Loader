namespace Pylon.ModLoader;

/// <summary>一个选项的声明（参考里 ConfigLoader.#Option 产出的那个对象）。</summary>
public sealed class ModConfigOption
{
    internal ModConfigOption(string key, OptionKind kind)
    {
        Key = key;
        Kind = kind;
    }

    /// <summary>选项在 Options 里的键，也是值、标签、落盘用的键。</summary>
    public string Key { get; internal set; }

    public OptionKind Kind { get; internal set; }

    /// <summary>没有存盘 / 存盘值不合法时用的值（Header 没有值）。</summary>
    public object Default { get; internal set; } = null!;

    /// <summary>Range 的下限。</summary>
    public double Min { get; internal set; }

    /// <summary>Range 的上限（默认 100）。</summary>
    public double Max { get; internal set; } = 100;

    /// <summary>Range 的步长（默认 1）。</summary>
    public double Step { get; internal set; } = 1;

    /// <summary>Range 的单位（跟在数字后面）。</summary>
    public string Suffix { get; internal set; } = "";

    /// <summary>Radio / Dropdown / Cycle 的可选值。</summary>
    public string[] Choices { get; internal set; } = [];

    /// <summary>每个选择的显示文本（对应参考的 choiceLabels）。</summary>
    public string[] ChoiceLabels { get; internal set; } = [];

    /// <summary>Header 的颜色：#RRGGBB；null = 白色。</summary>
    public string Color { get; internal set; } = null!;

    public HeaderAlign Align { get; internal set; } = HeaderAlign.Center;

    /// <summary>Button：配置里的方法名（参考接受函数或方法名，C# 里委托也接受）。</summary>
    public string Action { get; internal set; } = null!;

    /// <summary>Button：直接给的委托（可选）。</summary>
    public Action<ModConfig> ActionDelegate { get; internal set; } = null!;

    /// <summary>Link：要打开的地址。</summary>
    public string Url { get; internal set; } = null!;

    /// <summary>Button / Link 的按钮文字（默认 OK / 打开）。</summary>
    public string Text { get; internal set; } = null!;

    /// <summary>行标题（声明里给的，或从键名拆出来的）。</summary>
    public string Label { get; internal set; } = null!;

    /// <summary>另一个选项的键：那个开着这一行才可用（对应 enabledWhen 的字符串形式）。</summary>
    public string EnabledWhenKey { get; internal set; } = null!;

    /// <summary>或者给一个判断（对应 enabledWhen 的函数形式）。</summary>
    public Func<ModConfig, bool> EnabledWhen { get; internal set; } = null!;

    /// <summary>这个选项有没有值（Header/Button/Link 没有）。</summary>
    public bool HasValue => Kind != OptionKind.Header && Kind != OptionKind.Button && Kind != OptionKind.Link;

    public override string ToString() => Key + " (" + Kind + ")";
}
