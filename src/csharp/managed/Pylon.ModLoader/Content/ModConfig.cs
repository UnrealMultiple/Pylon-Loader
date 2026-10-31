using System.Text.RegularExpressions;

namespace Pylon.ModLoader;

/// <summary>一个 Mod 的一份配置。作者派生它、写静态 <c>Options</c>，其余交给加载器。</summary>
public abstract class ModConfig : ModType
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
    private IReadOnlyList<ModConfigOption>? _options;

    /// <summary>false：世界里不显示这份配置（世界生成那种只在进世界前有用的选项）。</summary>
    public virtual bool VisibleInWorld => true;

    /// <summary>当前值（参考里是实例属性 inst[key]；这里集中存放，读写都走校验，改请用 SetOption）。</summary>
    public IReadOnlyDictionary<string, object> Values => _values;

    /// <summary>本配置的选项声明（注册时按 Options 建好，和加载器手里的是同一份）。</summary>
    public IReadOnlyList<ModConfigOption> OptionList => _options ?? [];

    internal void __SetOptions(List<ModConfigOption> options) => _options = options;

    /// <summary>值刚从文件读出来时（Mod 加载期）。</summary>
    public virtual void OnLoaded() { }

    /// <summary>某个选项变了（屏幕上按 Aplicar 时；屏幕外 SetOption 立刻）。</summary>
    public virtual void OnChanged(string key) { }

    /// <summary>玩家按了"应用"，且这份配置确实变了；在自己那些 OnChanged 之后。</summary>
    public virtual void OnApply() { }

    /// <summary>把这份配置恢复成默认值（并落盘）。</summary>
    public void ResetToDefaults() => ModConfigLoader.Reset(this);

    /// <summary>改一个选项：校验 → Snap → 落盘 → OnChanged。</summary>
    public void SetOption(string key, object value) => ModConfigLoader.SetOption(this, key, value);

    // 读值：参考里是 config.Power 这种属性，这里少一层类型转换。

    public object GetValue(string key) => _values.TryGetValue(key, out var value) ? value : null!;

    public bool GetBool(string key, bool fallback = false) => ValueConverter.Convert(GetValue(key), fallback);
    public double GetNumber(string key, double fallback = 0) => ValueConverter.Convert(GetValue(key), fallback);
    public int GetInt(string key, int fallback = 0) => (int)Math.Round(ValueConverter.Convert(GetValue(key), (double)fallback));
    public string GetString(string key, string fallback = "") => ValueConverter.Convert(GetValue(key), fallback);

    /// <summary>参考的 `config[key]`；键不存在时抛（跟 JS 里读 undefined 的语义不同，早报错更好）。</summary>
    public object this[string key] =>
        _values.TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"{GetType().Name}: 没有 '{key}' 这个选项");

    // 声明用的工厂。

    /// <summary>段落标题，下面一条线。color 用 '#RRGGBB'，align 默认居中。</summary>
    public static OptionBuilder Header(string? label = null, string? color = null, HeaderAlign align = HeaderAlign.Center) =>
        new OptionBuilder(OptionKind.Header).Label(label).Header(color, align);

    /// <summary>游戏自己的 Off / On 开关。</summary>
    public static OptionBuilder Toggle(bool defaultValue = false) =>
        new OptionBuilder(OptionKind.Toggle).Default(defaultValue);

    /// <summary>游戏自己的滑条；值一定落在 step 的格子上，suffix 跟在数字后面。</summary>
    public static OptionBuilder Range(double defaultValue, double min = 0, double max = 100, double step = 1, string suffix = "") =>
        new OptionBuilder(OptionKind.Range).Default(defaultValue).Bounds(min, max, step).Suffix(suffix);

    /// <summary>每个选择一个按钮，并排；选中的那个是绿的。</summary>
    public static OptionBuilder Radio(string defaultValue, params string[] choices) =>
        new OptionBuilder(OptionKind.Radio).Choices(defaultValue, choices);

    /// <summary>游戏自己的下拉（最多 12 个选择）。</summary>
    public static OptionBuilder Dropdown(string defaultValue, params string[] choices) =>
        new OptionBuilder(OptionKind.Dropdown).Choices(defaultValue, choices);

    /// <summary>右边一个 `&lt; 选择 &gt;`，点一次换下一个。</summary>
    public static OptionBuilder Cycle(string defaultValue, params string[] choices) =>
        new OptionBuilder(OptionKind.Cycle).Choices(defaultValue, choices);

    /// <summary>色块 + 游戏自己的色相/饱和度/亮度滑条；值 '#RRGGBB'。</summary>
    public static OptionBuilder Color(string defaultValue = "#FFFFFF") =>
        new OptionBuilder(OptionKind.Color).Default((defaultValue ?? "#FFFFFF").ToUpperInvariant());

    /// <summary>右边一个按钮，点了调配置里的方法（或给的委托）。</summary>
    public static OptionBuilder Button(string method) =>
        new OptionBuilder(OptionKind.Button).Action(method);

    /// <summary>右边一个按钮，点了用浏览器打开地址。</summary>
    public static OptionBuilder Link(string url) =>
        new OptionBuilder(OptionKind.Link).Url(url);

    /// <summary>本配置所属的 Mod（注册时由管线给）。</summary>
    public Mod OwnerMod => Mod;

    internal void __SetValueRaw(string key, object value) => _values[key] = value;

    internal Dictionary<string, object> __Values => _values;

    /// <summary>键名拆成词：BossHealth → "Boss Health"。</summary>
    internal static string SplitWords(string key) =>
        string.IsNullOrEmpty(key)
            ? key
            : Regex.Replace(key, "([a-z0-9])([A-Z])", "$1 $2", RegexOptions.CultureInvariant);
}
