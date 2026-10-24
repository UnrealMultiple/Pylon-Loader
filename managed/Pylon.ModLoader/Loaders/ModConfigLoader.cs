// 层：框架 / 配置加载器 —— 翻译自 ConfigLoader.js。
// 分工：ModConfig.js = 作者用的声明工厂；ConfigLoader = 校验、读盘、草稿（draft）、落盘、回调；这里就是后者。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Pylon.ModLoader
{
    /// <summary>一份配置的注册记录。</summary>
    public sealed class ModConfigEntry
    {
        internal ModConfigEntry(ModConfig instance, string name, IReadOnlyList<ModConfigOption> options)
        {
            Instance = instance;
            Name = name;
            Options = options;
        }

        /// <summary>作者那份配置实例（真值都在它身上）。</summary>
        public ModConfig Instance { get; }

        /// <summary>配置类的名字（也是存盘文件名与本地化键的一段）。</summary>
        public string Name { get; }

        public Mod Mod => Instance.Mod;

        public IReadOnlyList<ModConfigOption> Options { get; }

        /// <summary>存盘路径；拿不到 Mod 数据目录时为 null（不落盘）。</summary>
        internal string File { get; set; }

        /// <summary>屏幕开着时的草稿（克隆一份，屏幕只碰它）。</summary>
        internal ModConfig Draft { get; set; }

        /// <summary>世界里显不显示这份配置。</summary>
        public bool VisibleInWorld => Instance.VisibleInWorld;

        public override string ToString() => Name;
    }

    /// <summary>配置的注册表与生命周期：校验声明 → 读盘 → 草稿 → 应用 → 落盘。</summary>
    public static class ModConfigLoader
    {
        private static readonly List<ModConfigEntry> Entries = new();
        private static readonly HashSet<ModConfigEntry> Dirty = new();
        private static readonly OptionKind[] ValueKinds =
        {
            OptionKind.Toggle, OptionKind.Range, OptionKind.Radio,
            OptionKind.Dropdown, OptionKind.Cycle, OptionKind.Color,
        };
        private const int MaxDropdown = 12;

        /// <summary>落盘失败之类的错误往哪写（由 Mod 管线接上日志）。</summary>
        public static Action<string> ModConfigLog { get; set; }

        /// <summary>加载过程的说明往哪写。</summary>
        public static Action<string> ModConfigTrace { get; set; }

        /// <summary>全部已注册的配置（按注册顺序）。</summary>
        public static IReadOnlyList<ModConfigEntry> List => Entries;

        /// <summary>注册一份配置：校验声明 → 读盘 → 写初值 → OnLoaded。</summary>
        public static void Add(ModConfig instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));

            var name = instance.GetType().Name;
            var options = BuildOptions(instance);
            if (options.Count == 0)
                throw new InvalidOperationException($"{name}: 请用静态 `Options` 声明选项（ModConfig.Toggle/Range/...）");
            instance.__SetOptions(options);   // 和 entry 手里的是同一份，别再重建一遍

            // enabledWhen 必须指向本配置里存在的选项
            foreach (var option in options)
            {
                if (option.EnabledWhenKey == null) continue;
                var found = false;
                foreach (var other in options)
                {
                    if (other.Key == option.EnabledWhenKey) { found = true; break; }
                }
                if (!found)
                    throw new InvalidOperationException(
                        $"{name}.{option.Key}: enabledWhen '{option.EnabledWhenKey}' 不是这份配置里的选项");
            }

            var entry = new ModConfigEntry(instance, name, options);

            // 存盘位置：<Mod 数据目录>/<类名>.json
            var mod = instance.Mod;
            var directory = mod?.DataDirectory;
            if (!string.IsNullOrEmpty(directory)) entry.File = Path.Combine(directory, name + ".json");

            // 读盘 → 不合法（或没有）就用默认值
            var saved = entry.File != null ? Read(entry.File) : new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var option in options)
            {
                if (!HasValue(option)) continue;
                object value;
                if (!saved.TryGetValue(option.Key, out value) || !Valid(option, value)) value = option.Default;
                instance.__SetValueRaw(option.Key, value);
            }

            Entries.Add(entry);
            try { instance.OnLoaded(); }
            catch (Exception e) { ModConfigLog?.Invoke($"{name}.OnLoaded 出错：{Flatten(e)}"); }
        }

        /// <summary>这种选项带不带值（Header/Button/Link 不带）。</summary>
        public static bool HasValue(ModConfigOption option) => Array.IndexOf(ValueKinds, option.Kind) >= 0;

        // 把静态 Options 里的一条声明变成校验过的选项
        internal static List<ModConfigOption> BuildOptions(ModConfig instance)
        {
            var declared = instance.GetType().GetField("Options",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.FlattenHierarchy);
            var raw = declared?.GetValue(null);
            var builder = new List<ModConfigOption>();

            if (raw == null)
            {
                // 也许是实例上放的（或者干脆没声明）
                var own = instance.GetType().GetField("Options",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                raw = own?.GetValue(instance);
            }
            if (raw == null) return builder;

            object declaredObj = raw;
            var owner = instance.GetType().Name;

            if (declaredObj is System.Collections.IDictionary dictionary)
            {
                foreach (System.Collections.DictionaryEntry pair in dictionary)
                {
                    var key = System.Convert.ToString(pair.Key, CultureInfo.InvariantCulture);
                    builder.Add(Option(owner, key, pair.Value));
                }
            }
            else if (declaredObj is System.Collections.IEnumerable sequence)
            {
                // 也接受 KeyValuePair 的序列（有序、可重复键名检查交给作者）
                foreach (var item in sequence)
                {
                    if (item == null) continue;
                    var itemType = item.GetType();
                    var keyProperty = itemType.GetProperty("Key");
                    var valueProperty = itemType.GetProperty("Value");
                    if (keyProperty == null || valueProperty == null) continue;
                    var key = System.Convert.ToString(keyProperty.GetValue(item), CultureInfo.InvariantCulture);
                    builder.Add(Option(owner, key, valueProperty.GetValue(item)));
                }
            }

            return builder;
        }

        private static ModConfigOption Option(string owner, string key, object declaration)
        {
            var where = owner + "." + key;
            if (declaration == null)
                throw new InvalidOperationException($"{where}: 请用 ModConfig.Toggle、Range、Radio、Dropdown... 声明");

            // Options 是静态字段时 Toggle(true) 在类型初始化跑、拿不到类名，所以键名在这里补、校验也在这里做。
            ModConfigOption option;
            if (declaration is ModConfigOption declaredOption) option = declaredOption;
            else if (declaration is OptionBuilder declaredBuilder) option = declaredBuilder.Build(key);
            else
                throw new InvalidOperationException(
                    $"{where}: 选项声明要写成 ModConfig.Toggle(...) 这种（拿到的是 {declaration.GetType().Name}）");

            option.Key = key;

            switch (option.Kind)
            {
                case OptionKind.Header:
                    if (option.Color != null)
                    {
                        option.Color = option.Color.ToUpperInvariant();
                        if (!IsHex(option.Color))
                            throw new InvalidOperationException($"{where}: Header 的 color 要写成 '#RRGGBB'");
                    }
                    break;

                case OptionKind.Toggle:
                    option.Default = API.Convert(option.Default, false);
                    break;

                case OptionKind.Range:
                    var def = API.Convert(option.Default, double.NaN);
                    if (double.IsNaN(def) || double.IsNaN(option.Min) || double.IsNaN(option.Max) || double.IsNaN(option.Step))
                        throw new InvalidOperationException($"{where}: Range 的 min/max/step/default 都要是数字");
                    if (!(option.Max > option.Min) || !(option.Step > 0))
                        throw new InvalidOperationException($"{where}: Range 要 min < max 且 step > 0");
                    option.Default = Snap(option, def);
                    break;

                case OptionKind.Radio:
                case OptionKind.Dropdown:
                case OptionKind.Cycle:
                    if (option.Choices == null || option.Choices.Length < 2)
                        throw new InvalidOperationException($"{where}: 至少要两个选择");
                    if (option.Kind == OptionKind.Dropdown && option.Choices.Length > MaxDropdown)
                        throw new InvalidOperationException($"{where}: Dropdown 最多 {MaxDropdown} 个选择");
                    var defaultValue = System.Convert.ToString(option.Default, CultureInfo.InvariantCulture);
                    if (Array.IndexOf(option.Choices, defaultValue) < 0)
                        throw new InvalidOperationException($"{where}: 默认值 '{defaultValue}' 不在选择里");
                    option.Default = defaultValue;
                    break;

                case OptionKind.Color:
                    var hex = System.Convert.ToString(option.Default, CultureInfo.InvariantCulture);
                    if (!IsHex(hex))
                        throw new InvalidOperationException($"{where}: Color 的默认值要写成 '#RRGGBB'");
                    option.Default = hex;
                    break;

                case OptionKind.Button:
                    if (option.ActionDelegate == null && string.IsNullOrEmpty(option.Action))
                        throw new InvalidOperationException($"{where}: Button 要一个方法名（或 Action 委托）");
                    break;

                case OptionKind.Link:
                    if (string.IsNullOrEmpty(option.Url) ||
                        !(option.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                          option.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException($"{where}: Link 要一个 http(s) 地址");
                    break;

                default:
                    throw new InvalidOperationException($"{where}: 不认识的类型 '{option.Kind}'");
            }

            // 本地化：Configs.<类>.<键>.Label / .<选择> / .Text
            option.Label = Local(owner, key, "Label", option.Label ?? ModConfig.SplitWords(key));
            if (option.Kind == OptionKind.Radio || option.Kind == OptionKind.Dropdown || option.Kind == OptionKind.Cycle)
            {
                var labels = new string[option.Choices.Length];
                for (var i = 0; i < option.Choices.Length; i++)
                {
                    var declared = option.ChoiceLabels != null && i < option.ChoiceLabels.Length
                        ? option.ChoiceLabels[i]
                        : null;
                    labels[i] = Local(owner, key, option.Choices[i], declared ?? option.Choices[i]);
                }
                option.ChoiceLabels = labels;
            }
            if (option.Kind == OptionKind.Button || option.Kind == OptionKind.Link)
            {
                var fallback = option.Text ?? (option.Kind == OptionKind.Link ? OpenText() : "OK");
                option.Text = Local(owner, key, "Text", fallback);
            }

            return option;
        }

        private static string Local(string owner, string key, string path, string fallback)
        {
            var name = "Configs." + owner + "." + key + "." + path;
            if (!ModLocalization.Exists(name)) return fallback;
            var text = ModLocalization.GetTextValue(name);
            return string.IsNullOrEmpty(text) ? fallback : text;
        }

        private static string OpenText() =>
            (ModLocalization.ActiveCultureName ?? "").StartsWith("pt", StringComparison.OrdinalIgnoreCase)
                ? "Abrir"
                : "Open";

        /// <summary>把值对到 step 的格子上，并夹在 [min, max] 里。</summary>
        public static double Snap(ModConfigOption option, double value)
        {
            var steps = Math.Round((value - option.Min) / option.Step, MidpointRounding.AwayFromZero);
            var snapped = option.Min + steps * option.Step;
            return Math.Min(option.Max, Math.Max(option.Min, Math.Round(snapped, 6, MidpointRounding.AwayFromZero)));
        }

        private static bool Valid(ModConfigOption option, object value)
        {
            if (value == null) return false;
            switch (option.Kind)
            {
                case OptionKind.Toggle:
                    return value is bool;
                case OptionKind.Range:
                {
                    var number = API.Convert(value, double.NaN);
                    return !double.IsNaN(number) && number >= option.Min && number <= option.Max;
                }
                case OptionKind.Radio:
                case OptionKind.Dropdown:
                case OptionKind.Cycle:
                {
                    var text = System.Convert.ToString(value, CultureInfo.InvariantCulture);
                    return text != null && Array.IndexOf(option.Choices, text) >= 0;
                }
                case OptionKind.Color:
                {
                    var text = System.Convert.ToString(value, CultureInfo.InvariantCulture);
                    return IsHex(text);
                }
                default:
                    return false;
            }
        }

        private static bool IsHex(string value) =>
            value != null && value.Length == 7 && value[0] == '#' &&
            System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9A-F]{6}$");

        // Begin：屏幕开着时每份配置一个草稿
        /// <summary>开屏：给每份配置克隆一个草稿，屏幕只读写草稿。</summary>
        public static void Begin()
        {
            foreach (var entry in Entries)
            {
                var draft = entry.Instance.CloneForDraft();
                entry.Draft = draft;
            }
        }

        /// <summary>屏幕该读哪个实例：草稿在就用草稿。</summary>
        public static ModConfig View(ModConfigEntry entry) => entry?.Draft ?? entry?.Instance;

        /// <summary>草稿里有没有跟生效值不一样的地方（决定"应用"要不要亮）。</summary>
        public static bool Pending() => Pending(Entries);

        /// <summary>（同上，限定几份配置）</summary>
        public static bool Pending(IEnumerable<ModConfigEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (entry.Draft == null) continue;
                foreach (var option in entry.Options)
                {
                    if (!HasValue(option)) continue;
                    if (!Same(View(entry).GetValue(option.Key), entry.Instance.GetValue(option.Key))) return true;
                }
            }
            return false;
        }

        /// <summary>这几份配置是不是都已经在默认值上（决定"默认"要不要灰）。</summary>
        public static bool AtDefaults(IEnumerable<ModConfigEntry> entries)
        {
            foreach (var entry in entries)
            {
                foreach (var option in entry.Options)
                {
                    if (!HasValue(option)) continue;
                    if (!Same(View(entry).GetValue(option.Key), option.Default)) return false;
                }
            }
            return true;
        }

        /// <summary>把这几份配置的选项都设回默认值（在草稿上，等"应用"）。</summary>
        public static void Defaults(IEnumerable<ModConfigEntry> entries)
        {
            foreach (var entry in entries)
            {
                foreach (var option in entry.Options)
                {
                    if (HasValue(option)) Set(entry, option, option.Default);
                }
            }
        }

        /// <summary>把草稿写进真值：每个变了的键一次 OnChanged，然后一次 OnApply，最后落盘。</summary>
        public static void Apply()
        {
            foreach (var entry in Entries)
            {
                var draft = entry.Draft;
                if (draft == null) continue;
                entry.Draft = null;

                var changed = new List<ModConfigOption>();
                foreach (var option in entry.Options)
                {
                    if (!HasValue(option)) continue;
                    if (!Same(draft.GetValue(option.Key), entry.Instance.GetValue(option.Key))) changed.Add(option);
                }
                if (changed.Count == 0) continue;

                foreach (var option in changed) entry.Instance.__SetValueRaw(option.Key, draft.GetValue(option.Key));
                Dirty.Add(entry);
                foreach (var option in changed)
                {
                    try { entry.Instance.OnChanged(option.Key); }
                    catch (Exception e) { ModConfigLog?.Invoke($"{entry.Name}.OnChanged 出错：{Flatten(e)}"); }
                }
                try { entry.Instance.OnApply(); }
                catch (Exception e) { ModConfigLog?.Invoke($"{entry.Name}.OnApply 出错：{Flatten(e)}"); }
            }
            Flush();
        }

        /// <summary>丢掉所有草稿（关屏没应用）。</summary>
        public static void Discard()
        {
            foreach (var entry in Entries) entry.Draft = null;
        }

        /// <summary>写一个值：Snap/大写 → 校验 → 写草稿或真值（写真值时立刻 OnChanged）。</summary>
        public static void Set(ModConfigEntry entry, ModConfigOption option, object value)
        {
            if (option.Kind == OptionKind.Range) value = Snap(option, API.Convert(value, option.Min));
            if (option.Kind == OptionKind.Color && value is string text) value = text.ToUpperInvariant();

            var target = View(entry);
            if (!Valid(option, value)) return;
            if (Same(target.GetValue(option.Key), value)) return;

            target.__SetValueRaw(option.Key, value);
            if (entry.Draft != null) return;

            Dirty.Add(entry);
            try { entry.Instance.OnChanged(option.Key); }
            catch (Exception e) { ModConfigLog?.Invoke($"{entry.Name}.OnChanged 出错：{Flatten(e)}"); }
        }

        /// <summary>这份配置回默认值（立刻落盘）。</summary>
        public static void Reset(ModConfig instance)
        {
            var entry = EntryOf(instance);
            if (entry == null) return;
            Defaults(new[] { entry });
            Flush();
        }

        /// <summary>按实例 + 键改一个值（屏幕外就是立即生效的那种）。</summary>
        public static void SetOption(ModConfig instance, string key, object value)
        {
            var entry = EntryOf(instance);
            var option = entry == null ? null : Find(entry, key);
            if (option == null || !HasValue(option))
                throw new InvalidOperationException(
                    $"{instance.GetType().Name}: '{key}' 不是带值的选项");
            Set(entry, option, value);
        }

        /// <summary>这一行现在可不可用（enabledWhen：键名或判断）。</summary>
        public static bool IsEnabled(ModConfigEntry entry, ModConfigOption option)
        {
            if (option.EnabledWhenKey == null && option.EnabledWhen == null) return true;
            var view = View(entry);
            if (option.EnabledWhenKey != null) return API.Convert(view.GetValue(option.EnabledWhenKey), false);
            try { return option.EnabledWhen(view); }
            catch (Exception e)
            {
                ModConfigLog?.Invoke($"{entry.Name}.{option.Key}.enabledWhen 出错：{Flatten(e)}");
                return false;
            }
        }

        // Run：按钮方法跑在草稿上（读到的就是屏幕上的值）
        /// <summary>按一下 Button 行：调配置里的方法（屏幕开着时 this 是草稿，改动也等"应用"）。</summary>
        public static void Run(ModConfigEntry entry, ModConfigOption option)
        {
            var view = View(entry);
            try
            {
                if (option.ActionDelegate != null) { option.ActionDelegate(view); return; }
                var method = view.GetType().GetMethod(option.Action,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);
                if (method == null || method.GetParameters().Length > 0)
                    throw new InvalidOperationException($"{entry.Name}.{option.Key}: '{option.Action}' 不是这份配置里的无参方法");
                method.Invoke(view, null);
            }
            catch (Exception e)
            {
                ModConfigLog?.Invoke($"{entry.Name}.{option.Key} 出错：{Flatten(e)}");
            }
        }

        /// <summary>把脏掉的配置写盘：只写跟默认值不同的项；全都等于默认值就删文件。</summary>
        public static void Flush()
        {
            foreach (var entry in Dirty)
            {
                if (entry.File == null) continue;
                var data = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var option in entry.Options)
                {
                    if (!HasValue(option)) continue;
                    var value = entry.Instance.GetValue(option.Key);
                    if (!Same(value, option.Default)) data[option.Key] = value;
                }

                try
                {
                    if (data.Count == 0)
                    {
                        if (File.Exists(entry.File)) File.Delete(entry.File);
                        continue;
                    }
                    var directory = Path.GetDirectoryName(entry.File);
                    if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                    File.WriteAllText(entry.File, Write(data));
                }
                catch (Exception e)
                {
                    ModConfigLog?.Invoke($"ModConfig: 写 {entry.File} 失败：{Flatten(e)}");
                }
            }
            Dirty.Clear();
        }

        /// <summary>某个 Mod 的全部配置。</summary>
        public static List<ModConfigEntry> Of(Mod mod)
        {
            var result = new List<ModConfigEntry>();
            foreach (var entry in Entries)
            {
                if (ReferenceEquals(entry.Mod, mod)) result.Add(entry);
            }
            return result;
        }

        /// <summary>按实例找记录（草稿也算）。</summary>
        public static ModConfigEntry EntryOf(ModConfig instance)
        {
            foreach (var entry in Entries)
            {
                if (ReferenceEquals(entry.Instance, instance) || ReferenceEquals(entry.Draft, instance)) return entry;
            }
            return null;
        }

        /// <summary>按实例 + 键找选项。</summary>
        public static ModConfigOption Find(ModConfigEntry entry, string key)
        {
            if (entry == null || key == null) return null;
            foreach (var option in entry.Options)
            {
                if (string.Equals(option.Key, key, StringComparison.Ordinal)) return option;
            }
            return null;
        }

        private static Dictionary<string, object> Read(string file)
        {
            var data = new Dictionary<string, object>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(file)) return data;
                var text = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(text)) return data;

                using var document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind != JsonValueKind.Object) return data;
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    data[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Number => property.Value.GetDouble(),
                        JsonValueKind.String => property.Value.GetString(),
                        _ => null,
                    };
                }
            }
            catch (Exception e)
            {
                ModConfigLog?.Invoke($"ModConfig: {file} 坏了（{e.Message}）；用默认值");
            }
            return data;
        }

        // 落盘：参考是 JSON.stringify(data, null, 2)，这里保持缩进一样（人看得懂、diff 干净）
        private static string Write(Dictionary<string, object> data)
        {
            var text = new StringBuilder();
            text.Append("{\n");
            var index = 0;
            foreach (var pair in data)
            {
                text.Append("  ").Append(JsonSerializer.Serialize(pair.Key)).Append(": ");
                text.Append(JsonSerializer.Serialize(pair.Value));
                if (++index < data.Count) text.Append(',');
                text.Append('\n');
            }
            text.Append("}\n");
            return text.ToString();
        }

        /// <summary>草稿克隆：新实例 + 声明好的值拷过去。</summary>
        private static ModConfig CloneForDraft(this ModConfig instance)
        {
            var draft = (ModConfig)Activator.CreateInstance(instance.GetType());
            draft.Mod = instance.Mod;
            foreach (var pair in instance.Values) draft.__SetValueRaw(pair.Key, pair.Value);
            return draft;
        }

        /// <summary>两个值算不算一样（数字按数值比，字符串按序数比）。</summary>
        private static bool Same(object a, object b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (IsNumber(a) && IsNumber(b))
                return Math.Abs(System.Convert.ToDouble(a, CultureInfo.InvariantCulture) -
                                System.Convert.ToDouble(b, CultureInfo.InvariantCulture)) < 1e-9;
            if (a is string sa && b is string sb) return string.Equals(sa, sb, StringComparison.Ordinal);
            return a.Equals(b);
        }

        private static bool IsNumber(object value) =>
            value is byte || value is sbyte || value is short || value is ushort ||
            value is int || value is uint || value is long || value is ulong ||
            value is float || value is double || value is decimal;

        /// <summary>异常压成一行（"谁 -> 谁"），日志里看得清。</summary>
        internal static string Flatten(Exception e)
        {
            var text = new StringBuilder();
            var current = e;
            for (var depth = 0; current != null && depth < 6; depth++, current = current.InnerException)
            {
                if (depth > 0) text.Append(" <- ");
                text.Append(current.GetType().Name).Append(": ").Append(current.Message);
            }
            return text.ToString();
        }
    }
}
