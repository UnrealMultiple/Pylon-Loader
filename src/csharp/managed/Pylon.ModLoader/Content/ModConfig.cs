// 层：框架 / Mod 配置
//
// 参考里作者写 `static Options = { ... }`、值靠 `inst[key]` 动态读写；C# 没有动态属性，本版：
//   声明还是静态 Options（有序字典），工厂方法同名同义（Header/Toggle/Range/Radio/Dropdown/Cycle/Color/Button/Link）；
//   值放实例的 Values（读 GetXxx/GetValue、写 SetOption，想写成属性也行），校验、Snap、落盘、OnChanged 都在框架这一层。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Pylon.ModLoader
{
    /// <summary>控件的种类（参考里就是选项对象上的 type 字符串）。</summary>
    public enum OptionKind
    {
        Header, Toggle, Range, Radio, Dropdown, Cycle, Color, Button, Link,
    }

    /// <summary>Header 的文字对齐（参考的 align：left/center/right）。</summary>
    public enum HeaderAlign
    {
        Center, Left, Right,
    }

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
        public object Default { get; internal set; }

        // Range
        public double Min { get; internal set; }
        public double Max { get; internal set; } = 100;
        public double Step { get; internal set; } = 1;
        public string Suffix { get; internal set; } = "";

        // Radio / Dropdown / Cycle
        public string[] Choices { get; internal set; } = Array.Empty<string>();

        /// <summary>每个选择的显示文本（对应参考的 choiceLabels）。</summary>
        public string[] ChoiceLabels { get; internal set; } = Array.Empty<string>();

        // Header
        /// <summary>#RRGGBB；null = 白色。</summary>
        public string Color { get; internal set; }
        public HeaderAlign Align { get; internal set; } = HeaderAlign.Center;

        // Button / Link
        /// <summary>Button：配置里的方法名（参考接受函数或方法名，C# 里委托也接受）。</summary>
        public string Action { get; internal set; }
        /// <summary>Button：直接给的委托（可选）。</summary>
        public Action<ModConfig> ActionDelegate { get; internal set; }
        /// <summary>Link：要打开的地址。</summary>
        public string Url { get; internal set; }
        /// <summary>Button / Link 的按钮文字（默认 OK / 打开）。</summary>
        public string Text { get; internal set; }

        // 通用
        /// <summary>行标题（声明里给的，或从键名拆出来的）。</summary>
        public string Label { get; internal set; }
        /// <summary>另一个选项的键：那个开着这一行才可用（对应 enabledWhen 的字符串形式）。</summary>
        public string EnabledWhenKey { get; internal set; }
        /// <summary>或者给一个判断（对应 enabledWhen 的函数形式）。</summary>
        public Func<ModConfig, bool> EnabledWhen { get; internal set; }

        /// <summary>这个选项有没有值（Header/Button/Link 没有）。</summary>
        public bool HasValue => Kind != OptionKind.Header && Kind != OptionKind.Button && Kind != OptionKind.Link;

        public override string ToString() => Key + " (" + Kind + ")";
    }

    /// <summary>
    /// 一个 Mod 的一份配置。作者派生它、写静态 <see cref="Options"/>，其余交给加载器。
    /// 对照参考 ModConfig.js + ConfigLoader.js（校验、Snap、草稿、落盘、回调都在那里）。
    /// </summary>
    public abstract class ModConfig : ModType
    {
        private readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);
        private List<ModConfigOption> _options;

        /// <summary>false：世界里不显示这份配置（世界生成那种只在进世界前有用的选项）。</summary>
        public virtual bool VisibleInWorld => true;

        /// <summary>当前值（参考里是实例属性 inst[key]；这里集中存放，读写都走校验，改请用 SetOption）。</summary>
        public IReadOnlyDictionary<string, object> Values => _values;

        /// <summary>本配置的选项声明（注册时按 Options 建好，和加载器手里的是同一份）。</summary>
        public IReadOnlyList<ModConfigOption> OptionList => _options ?? (IReadOnlyList<ModConfigOption>)Array.Empty<ModConfigOption>();

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

        // ---- 读值：参考里是 config.Power 这种属性，这里少一层类型转换 ----

        public object GetValue(string key) =>
            _values.TryGetValue(key, out var value) ? value : null;

        public bool GetBool(string key, bool fallback = false) => API.Convert(GetValue(key), fallback);
        public double GetNumber(string key, double fallback = 0) => API.Convert(GetValue(key), fallback);
        public int GetInt(string key, int fallback = 0) => (int)Math.Round(API.Convert(GetValue(key), (double)fallback));
        public string GetString(string key, string fallback = "") => API.Convert(GetValue(key), fallback);

        /// <summary>参考的 `config[key]`；键不存在时抛（跟 JS 里读 undefined 的语义不同，早报错更好）。</summary>
        public object this[string key] =>
            _values.TryGetValue(key, out var value)
                ? value
                : throw new KeyNotFoundException($"{GetType().Name}: 没有 '{key}' 这个选项");

        // ---- 声明用的工厂 ----

        /// <summary>段落标题，下面一条线。color 用 '#RRGGBB'，align 默认居中。</summary>
        public static OptionBuilder Header(string label = null, string color = null, HeaderAlign align = HeaderAlign.Center) =>
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

    /// <summary>选项声明的链式构造器：让 `Toggle(true).Label("Fast mode").EnabledWhen("On")` 读起来像参考的对象字面量。</summary>
    public sealed class OptionBuilder
    {
        private readonly ModConfigOption _option;

        internal OptionBuilder(OptionKind kind) => _option = new ModConfigOption(null, kind);

        internal OptionBuilder Default(object value) { _option.Default = value; return this; }
        internal OptionBuilder Label(string label) { _option.Label = label; return this; }
        internal OptionBuilder Bounds(double min, double max, double step)
        {
            _option.Min = min;
            _option.Max = max;
            _option.Step = step;
            return this;
        }
        internal OptionBuilder Suffix(string suffix) { _option.Suffix = suffix; return this; }
        internal OptionBuilder Choices(string defaultValue, string[] choices)
        {
            _option.Default = defaultValue;
            _option.Choices = choices ?? Array.Empty<string>();
            return this;
        }
        internal OptionBuilder Header(string color, HeaderAlign align)
        {
            _option.Color = color;
            _option.Align = align;
            return this;
        }
        internal OptionBuilder Action(string method)
        {
            if (string.IsNullOrEmpty(method)) throw new ArgumentException("Button 需要方法名（或调用 Action(Action<ModConfig>)）");
            _option.Action = method;
            return this;
        }
        internal OptionBuilder Url(string url) { _option.Url = url; return this; }

        /// <summary>每个选择的显示文本（参考的 labels: [...]）。</summary>
        public OptionBuilder Labels(params string[] labels)
        {
            _option.ChoiceLabels = labels ?? Array.Empty<string>();
            return this;
        }

        /// <summary>按钮上的字（参考的 text）。</summary>
        public OptionBuilder Text(string text) { _option.Text = text; return this; }

        /// <summary>另一个选项的键：那个开着，这一行才可用。</summary>
        public OptionBuilder EnabledWhen(string otherKey) { _option.EnabledWhenKey = otherKey; return this; }

        /// <summary>给个判断：返回 true 这一行才可用。</summary>
        public OptionBuilder EnabledWhen(Func<ModConfig, bool> predicate) { _option.EnabledWhen = predicate; return this; }

        /// <summary>Button 也可以直接给委托（参考接受函数）。</summary>
        public OptionBuilder Action(Action<ModConfig> action) { _option.ActionDelegate = action; return this; }

        /// <summary>声明完成：给上游一份成品选项（键名由加载器按 Options 里的键补）。</summary>
        internal ModConfigOption Build(string key)
        {
            _option.Key = key;
            return _option;
        }

        internal OptionKind Kind => _option.Kind;
        internal string DefaultText => System.Convert.ToString(_option.Default, CultureInfo.InvariantCulture);
        internal string[] ChoicesValue => _option.Choices;
        internal ModConfigOption Raw => _option;
    }
}
