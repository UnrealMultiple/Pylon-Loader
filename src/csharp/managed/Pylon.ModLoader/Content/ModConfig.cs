using System.Text.RegularExpressions;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>
/// 一个 Mod 的一份配置：Mode 声明归属，选项在 DeclareOptions 里声明；
/// 值经 SetOption/Set 写入并落盘，OnChanged 在值真的变了之后调一次。
/// </summary>
public abstract class ModConfig : ModType
{
    private Dictionary<string, object> _values = new(StringComparer.Ordinal);
    private IReadOnlyList<ModConfigOption>? _options;

    /// <summary>多人同步的归属；作者必须声明（服务器说了算 = ServerSide）。</summary>
    public abstract ConfigScope Mode { get; }

    /// <summary>本地化键的分类（"Configs"）。</summary>
    public virtual string LocalizationCategory => "Configs";

    /// <summary>菜单里显示的名字；默认按类名拆词。</summary>
    public virtual LocalizedText DisplayName =>
        new($"Mods.{Mod?.Name}.{LocalizationCategory}.{Name}.DisplayName", SplitWords(Name));

    /// <summary>"Mod名/配置名"（落盘文件名与跨设备定位用）。</summary>
    public string FullName => $"{Mod?.Name}/{Name}";

    /// <summary>false：世界里不显示这份配置（世界生成那种只在进世界前有用的选项）。</summary>
    internal virtual bool VisibleInWorld => true;

    /// <summary>作者可以在这里拒绝自动加载（默认总是加载）。</summary>
    public virtual bool Autoload(ref string name) => true;

    public IReadOnlyDictionary<string, object> Values => _values;

    /// <summary>本配置的选项声明（注册时按 Options 建好，和加载器手里的是同一份）。</summary>
    public IReadOnlyList<ModConfigOption> OptionList => _options ?? [];

    internal void __SetOptions(List<ModConfigOption> options) => _options = options;

    internal void __DeclareOptions(ConfigOptions options) => DeclareOptions(options);

    /// <summary>声明本配置的选项（注册期调一次）；声明顺序 = 代码顺序。</summary>
    protected virtual void DeclareOptions(ConfigOptions options) { }

    /// <summary>值刚从文件读出来时（Mod 加载期）。</summary>
    public virtual void OnLoaded() { }

    /// <summary>某个值真的变了（应用/改值之后调一次）。</summary>
    public virtual void OnChanged() { }

    /// <summary>
    /// <see cref="ConfigScope.ServerSide"/> 时服务器对客户端改值请求的裁决：默认接受全部。
    /// 作者可改 pendingConfig 上的值（部分接受），返回 false 表示全部拒绝；抛异常按全拒绝处理。
    /// </summary>
    public virtual bool AcceptClientChanges(ModConfig pendingConfig, int whoAmI, ref NetworkText message) => true;

    /// <summary>服务器回信到达时（客户端侧）；默认什么都不做。</summary>
    public virtual void HandleAcceptClientChangesReply(bool success, int player, NetworkText message)
    {
    }

    /// <summary>复制一份（同类型、同值；不共享值字典）。</summary>
    public virtual ModConfig Clone()
    {
        var clone = (ModConfig)MemberwiseClone();
        clone._values = new Dictionary<string, object>(_values, StringComparer.Ordinal);
        clone._options = _options;
        clone.__Overlay = null;
        return clone;
    }

    /// <summary>这份改动要不要重载（默认不要；需要重载的内容自己覆写）。</summary>
    public virtual bool NeedsReload(ModConfig pendingConfig) => false;

    /// <summary>保存并应用改动；pendingConfig 省略时保存自己。</summary>
    public ConfigSaveResult SaveChanges(ModConfig? pendingConfig = null, Action<string, Color>? status = null,
                                        bool silent = true, bool broadcast = true) =>
        ConfigPipeline.Instance.SaveChanges(this, pendingConfig ?? this, status, silent, broadcast);

    /// <summary>打开这份配置的设置界面。</summary>
    public void Open(Action? onClose = null, string? scrollToOption = null, bool centerScrolledOption = true,
                     bool playSound = true) =>
        UI.ModConfigMenu.OpenConfig(this, onClose);

    /// <summary>把这份配置恢复成默认值（并落盘）。</summary>
    public void ResetToDefaults() => ModConfigLoader.Reset(this);

    /// <summary>改一个选项：校验 → Snap → 落盘 → OnChanged。</summary>
    public void SetOption(string key, object value) => ModConfigLoader.SetOption(this, key, value);

    /// <summary>这个键改了要不要重启（true = 拒改并保留旧值）；管线内部用，作者用 <see cref="NeedsReload"/>。</summary>
    internal virtual bool NeedsReloadKey(string key) => false;

    /// <summary>屏幕上的未保存改动（没开屏幕时为 null）；读取一律先看它。</summary>
    internal IReadOnlyDictionary<string, object>? __Overlay { get; set; }

    /// <summary>读真值（绕过 overlay）：草稿/应用阶段比对"有没有真的变了"用。</summary>
    internal object __StoredValue(string key) => _values.TryGetValue(key, out var value) ? value : null!;

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

    internal Dictionary<string, object> __CopyValues()
    {
        var copy = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var pair in _values) copy[pair.Key] = pair.Value;
        return copy;
    }

    internal void __ReplaceValues(Dictionary<string, object> values) => _values = values;

    /// <summary>键名拆成词：BossHealth → "Boss Health"。</summary>
    internal static string SplitWords(string key) =>
        string.IsNullOrEmpty(key)
            ? key
            : Regex.Replace(key, "([a-z0-9])([A-Z])", "$1 $2", RegexOptions.CultureInvariant);
}
