using System.Globalization;

namespace Pylon.ModLoader;

/// <summary>选项声明的链式构造器：让 `Toggle(true).Label("Fast mode").EnabledWhen("On")` 读起来像参考的对象字面量。</summary>
public sealed class OptionBuilder
{
    private readonly ModConfigOption _option;

    internal OptionBuilder(OptionKind kind) => _option = new ModConfigOption(null!, kind);

    internal OptionBuilder Default(object value)
    {
        _option.Default = value;
        return this;
    }

    internal OptionBuilder Label(string? label)
    {
        _option.Label = label!;
        return this;
    }

    internal OptionBuilder Bounds(double min, double max, double step)
    {
        _option.Min = min;
        _option.Max = max;
        _option.Step = step;
        return this;
    }

    internal OptionBuilder Suffix(string suffix)
    {
        _option.Suffix = suffix;
        return this;
    }

    internal OptionBuilder Choices(string defaultValue, string[] choices)
    {
        _option.Default = defaultValue;
        _option.Choices = choices ?? [];
        return this;
    }

    internal OptionBuilder Header(string? color, HeaderAlign align)
    {
        _option.Color = color!;
        _option.Align = align;
        return this;
    }

    internal OptionBuilder Action(string method)
    {
        if (string.IsNullOrEmpty(method)) throw new ArgumentException("Button 需要方法名（或调用 Action(Action<ModConfig>)）");
        _option.Action = method;
        return this;
    }

    internal OptionBuilder Url(string url)
    {
        _option.Url = url;
        return this;
    }

    /// <summary>每个选择的显示文本（参考的 labels: [...]）。</summary>
    public OptionBuilder Labels(params string[] labels)
    {
        _option.ChoiceLabels = labels ?? [];
        return this;
    }

    /// <summary>按钮上的字（参考的 text）。</summary>
    public OptionBuilder Text(string text)
    {
        _option.Text = text;
        return this;
    }

    /// <summary>另一个选项的键：那个开着，这一行才可用。</summary>
    public OptionBuilder EnabledWhen(string otherKey)
    {
        _option.EnabledWhenKey = otherKey;
        return this;
    }

    /// <summary>给个判断：返回 true 这一行才可用。</summary>
    public OptionBuilder EnabledWhen(Func<ModConfig, bool> predicate)
    {
        _option.EnabledWhen = predicate;
        return this;
    }

    /// <summary>Button 也可以直接给委托（参考接受函数）。</summary>
    public OptionBuilder Action(Action<ModConfig> action)
    {
        _option.ActionDelegate = action;
        return this;
    }

    /// <summary>声明完成：给上游一份成品选项（键名由加载器按 Options 里的键补）。</summary>
    internal ModConfigOption Build(string key)
    {
        _option.Key = key;
        return _option;
    }

    internal OptionKind Kind => _option.Kind;

    internal string DefaultText => Convert.ToString(_option.Default, CultureInfo.InvariantCulture)!;

    internal string[] ChoicesValue => _option.Choices;

    internal ModConfigOption Raw => _option;
}
