using System.Text.RegularExpressions;

namespace Pylon.ModLoader;

/// <summary>配置在多人游戏里归谁管。</summary>
public enum ConfigScope
{
    /// <summary>只在本机生效，不同步（默认）。</summary>
    ClientSide,

    /// <summary>服务器说了算：客户端连上时收到服务器的值，改了要发请求、由服务器裁决。</summary>
    ServerSide,
}

/// <summary>一个 Mod 的一份配置。作者派生它、写静态 <c>Options</c>，其余交给加载器。</summary>
public abstract class ModConfig : ModType
{
    private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
    private IReadOnlyList<ModConfigOption>? _options;

    /// <summary>多人同步的归属；默认只在本机。</summary>
    public virtual ConfigScope Scope => ConfigScope.ClientSide;

    /// <summary>false：世界里不显示这份配置（世界生成那种只在进世界前有用的选项）。</summary>
    public virtual bool VisibleInWorld => true;

    /// <summary>当前值（参考里是实例属性 inst[key]；这里集中存放，读写都走校验，改请用 SetOption）。</summary>
    public IReadOnlyDictionary<string, object> Values => _values;

    /// <summary>本配置的选项声明（注册时按 Options 建好，和加载器手里的是同一份）。</summary>
    public IReadOnlyList<ModConfigOption> OptionList => _options ?? [];

    internal void __SetOptions(List<ModConfigOption> options) => _options = options;

    /// <summary>管线侧调一次声明（protected 不对外）。</summary>
    internal void __DeclareOptions(ConfigOptions options) => DeclareOptions(options);

    /// <summary>
    /// 声明本配置的选项（注册期调一次）。写成虚方法而不是静态字段：
    /// 声明顺序由代码顺序决定，且不需要任何反射。
    /// </summary>
    protected virtual void DeclareOptions(ConfigOptions options) { }

    /// <summary>值刚从文件读出来时（Mod 加载期）。</summary>
    public virtual void OnLoaded() { }

    /// <summary>某个选项变了（屏幕上按 Aplicar 时；屏幕外 SetOption 立刻）。</summary>
    public virtual void OnChanged(string key) { }

    /// <summary>玩家按了"应用"，且这份配置确实变了；在自己那些 OnChanged 之后。</summary>
    public virtual void OnApply() { }

    /// <summary>把这份配置恢复成默认值（并落盘）。</summary>
    public void ResetToDefaults() => ModConfigLoader.Reset(this);

    /// <summary>
    /// <see cref="ConfigScope.ServerSide"/> 时，服务器对客户端改值请求的裁决：默认接受全部键。
    /// 想锁住某些键就在这里否决（把不要的键从 <paramref name="requested"/> 里删掉 = 部分接受，
    /// 返回 false = 全部拒绝）。抛异常按"全部拒绝"处理。
    /// </summary>
    public virtual bool AcceptClientChanges(int fromPlayer, Dictionary<string, object> requested, ref string message) => true;

    /// <summary>改一个选项：校验 → Snap → 落盘 → OnChanged。</summary>
    public void SetOption(string key, object value) => ModConfigLoader.SetOption(this, key, value);

    /// <summary>
    /// 这个键改了要不要重启（ true = 拒改并保留旧值，屏幕上按应用也不落盘）。
    /// 与 <see cref="AcceptClientChanges"/> 同形，只是单人侧的拒绝点：改值要重生成内容/重载世界时用。
    /// </summary>
    public virtual bool NeedsReload(string key) => false;

    // 读值：参考里是 config.Power 这种属性，这里少一层类型转换。

    /// <summary>屏幕上的未保存改动（没开屏幕时为 null）；读取一律先看它。</summary>
    internal IReadOnlyDictionary<string, object>? __Overlay { get; set; }

    public object GetValue(string key)
    {
        var overlay = __Overlay;
        if (overlay is not null && overlay.TryGetValue(key, out var pending)) return pending;

        return _values.TryGetValue(key, out var value) ? value : null!;
    }

    public bool GetBool(string key, bool fallback = false) => ValueConverter.Convert(GetValue(key), fallback);
    public double GetNumber(string key, double fallback = 0) => ValueConverter.Convert(GetValue(key), fallback);
    public int GetInt(string key, int fallback = 0) => (int)Math.Round(ValueConverter.Convert(GetValue(key), (double)fallback));
    public string GetString(string key, string fallback = "") => ValueConverter.Convert(GetValue(key), fallback);

    /// <summary>参考的 `config[key]`；键不存在时抛（跟 JS 里读 undefined 的语义不同，早报错更好）。</summary>
    public object this[string key]
    {
        get
        {
            var value = GetValue(key);
            return value ?? throw new KeyNotFoundException($"{GetType().Name}: 没有 '{key}' 这个选项");
        }
    }

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

    /// <summary>右边一个按钮，点了调这个委托。</summary>
    public static OptionBuilder Button(Action<ModConfig> action) =>
        new OptionBuilder(OptionKind.Button).Action(action);

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