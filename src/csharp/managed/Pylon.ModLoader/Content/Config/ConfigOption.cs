using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Localization;
using Terraria.Localization;

namespace Pylon.ModLoader.Content;

public enum ConfigOptionKind
{
    Toggle,
    Range,
    Choice,
    Color,
    Data
}

/// <summary>配置选项：一个 public 字段/属性 + 特性驱动；Get/Set 就是字段读写，值即真值。</summary>
public sealed class ConfigOption
{
    private readonly FieldInfo? field;

    private readonly PropertyInfo? property;

    internal ConfigOption(MemberInfo member)
    {
        field = member as FieldInfo;
        property = member as PropertyInfo;
        Member = member;
        Type = field?.FieldType ?? property!.PropertyType;
        ReloadRequired = member.GetCustomAttribute<ReloadRequiredAttribute>() != null;
        Header = member.GetCustomAttribute<HeaderAttribute>();
        LabelKey = member.GetCustomAttribute<LabelKeyAttribute>();
        LabelArgs = member.GetCustomAttribute<LabelArgsAttribute>();
        TooltipKey = member.GetCustomAttribute<TooltipKeyAttribute>();
        TooltipArgs = member.GetCustomAttribute<TooltipArgsAttribute>();
        Slider = member.GetCustomAttribute<SliderAttribute>() != null || Type.GetCustomAttribute<SliderAttribute>() != null;
        ColorNoAlpha = member.GetCustomAttribute<ColorNoAlphaAttribute>() != null;
        var hsl = member.GetCustomAttribute<ColorHSLSliderAttribute>();
        ShowSaturationAndLightness = hsl?.ShowSaturationAndLightness ?? true;
        Kind = KindOf(member);
        Dropdown = member.GetCustomAttribute<DropdownAttribute>() != null || Type.GetCustomAttribute<DropdownAttribute>() != null;
        Cycle = member.GetCustomAttribute<CycleAttribute>() != null || Type.GetCustomAttribute<CycleAttribute>() != null;
    }

    public bool Dropdown { get; }

    public bool Cycle { get; }

    public MemberInfo Member { get; }

    public string Name => Member.Name;

    public Type Type { get; }

    public ConfigOptionKind Kind { get; }

    public bool ReloadRequired { get; }

    public HeaderAttribute? Header { get; }

    public LabelKeyAttribute? LabelKey { get; }

    public LabelArgsAttribute? LabelArgs { get; }

    public TooltipKeyAttribute? TooltipKey { get; }

    public TooltipArgsAttribute? TooltipArgs { get; }

    public bool Slider { get; }

    public bool ColorNoAlpha { get; }

    public bool ShowSaturationAndLightness { get; }

    public double Min { get; private set; }

    public double Max { get; private set; }

    public double Step { get; private set; }

    public bool Integer { get; private set; }

    public object[] Choices { get; private set; } = [];

    public bool HasDefault { get; private set; }

    public object? Default { get; private set; }

    public object? Get(object owner) => field != null ? field.GetValue(owner) : property!.GetValue(owner);

    public void Set(object owner, object? value)
    {
        if (field != null)
            field.SetValue(owner, value);
        else
            property!.SetValue(owner, value);
    }

    private static bool IsInteger(Type type) =>
        type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) || type == typeof(short) || type == typeof(ushort) || type == typeof(byte) || type == typeof(sbyte);

    private ConfigOptionKind KindOf(MemberInfo member)
    {
        ReadDefault(member);
        if (Type == typeof(bool))
            return ConfigOptionKind.Toggle;
        if (Type == typeof(Color))
            return ConfigOptionKind.Color;
        if (Type.IsEnum)
        {
            Choices = Enum.GetValues(Type).Cast<object>().ToArray();
            return ConfigOptionKind.Choice;
        }

        if (Type == typeof(string) && member.GetCustomAttribute<OptionStringsAttribute>() is { } strings && strings.OptionLabels.Length > 1)
        {
            Choices = strings.OptionLabels.Cast<object>().ToArray();
            return ConfigOptionKind.Choice;
        }

        if (IsInteger(Type) || Type == typeof(float) || Type == typeof(double))
        {
            Integer = IsInteger(Type);
            var range = member.GetCustomAttribute<RangeAttribute>();
            var increment = member.GetCustomAttribute<IncrementAttribute>();
            Min = range != null ? Math.Round(Convert.ToDouble(range.Min, CultureInfo.InvariantCulture), 6) : 0;
            Max = range != null ? Math.Round(Convert.ToDouble(range.Max, CultureInfo.InvariantCulture), 6) : Integer ? 100 : 1;
            Step = increment != null ? Math.Round(Convert.ToDouble(increment.Increment, CultureInfo.InvariantCulture), 6) : Integer ? 1 : 0.01;
            if (Type == typeof(byte) && range == null)
                Max = 255;
            if (!(Max > Min) || !(Step > 0))
                return ConfigOptionKind.Data;
            return ConfigOptionKind.Range;
        }

        return ConfigOptionKind.Data;
    }

    private void ReadDefault(MemberInfo member)
    {
        foreach (CustomAttributeData data in member.GetCustomAttributesData())
        {
            if (data.AttributeType != typeof(DefaultValueAttribute))
                continue;
            HasDefault = true;
            var args = data.ConstructorArguments;
            if (args.Count == 2 && args[0].Value is Type target && args[1].Value is string text)
                Default = Parse(target, text);
            else if (args.Count == 1)
                Default = args[0].Value is System.Collections.ObjectModel.ReadOnlyCollection<CustomAttributeTypedArgument> list ? list.Select(a => a.Value).ToArray() : args[0].Value;
            return;
        }
    }

    public object? Coerce(object? value)
    {
        try
        {
            if (value == null)
                return null;
            if (Kind == ConfigOptionKind.Color)
                return value is Color color ? color : value is string text ? ParseColor(text) : null;
            if (Type.IsEnum)
                return value is string name ? Enum.Parse(Type, name, true) : Enum.ToObject(Type, value);
            if (Kind == ConfigOptionKind.Range)
            {
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return Convert.ChangeType(Snap(number), Type, CultureInfo.InvariantCulture);
            }

            if (Kind == ConfigOptionKind.Choice)
            {
                string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
                return Choices.Contains(text) ? text : null;
            }

            return Type.IsInstanceOfType(value) ? value : Convert.ChangeType(value, Type, CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    public double Snap(double value)
    {
        double steps = Math.Round((value - Min) / Step);
        double snapped = Math.Round(Min + steps * Step, 6);
        snapped = Math.Clamp(snapped, Min, Max);
        return Integer ? Math.Round(snapped) : snapped;
    }

    private static object? Parse(Type target, string text) => target == typeof(Color) ? ParseColor(text) : TypeDescriptor.GetConverter(target).ConvertFromInvariantString(text);

    public static Color? ParseColor(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith("#") && (trimmed.Length == 7 || trimmed.Length == 9) &&
            uint.TryParse(trimmed[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hex))
        {
            if (trimmed.Length == 7)
                return new Color((int)(hex >> 16) & 255, (int)(hex >> 8) & 255, (int)hex & 255, 255);
            return new Color((int)(hex >> 24) & 255, (int)(hex >> 16) & 255, (int)(hex >> 8) & 255, (int)hex & 255);
        }

        string[] parts = trimmed.Split(',');
        if (parts.Length is < 3 or > 4)
            return null;
        int[] values = [0, 0, 0, 255];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                return null;
        return new Color(values[0], values[1], values[2], values[3]);
    }

    public static string FormatColor(Color color) => $"{color.R}, {color.G}, {color.B}, {color.A}";

    public string Label(ModConfig config)
    {
        string key = LabelKey != null && !LabelKey.malformed ? LabelKey.key : $"Mods.{config.Mod.Name}.Configs.{config.Name}.{Name}.Label";
        string text = Register(key, () => Regex.Replace(Name, "([a-z0-9])([A-Z])", "$1 $2")).Value ?? Name;
        return LabelArgs != null && LabelArgs.args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, text, LabelArgs.args) : text;
    }

    public string? Tooltip(ModConfig config)
    {
        string key = TooltipKey != null && !TooltipKey.malformed ? TooltipKey.key : $"Mods.{config.Mod.Name}.Configs.{config.Name}.{Name}.Tooltip";
        string text = Register(key, () => "").Value ?? "";
        if (TooltipArgs != null && TooltipArgs.args.Length > 0)
            text = string.Format(CultureInfo.InvariantCulture, text, TooltipArgs.args);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    public string? HeaderText(ModConfig config)
    {
        if (Header == null || Header.malformed)
            return null;
        if (Header.key != null)
            return Language.GetTextValue(Header.key);
        string id = Header.identifier!;
        return Register($"Mods.{config.Mod.Name}.Configs.{config.Name}.Headers.{id}", () => Regex.Replace(id, "([a-z0-9])([A-Z])", "$1 $2")).Value;
    }

    public string ChoiceLabel(ModConfig config, int index)
    {
        object choice = Choices[index];
        if (!Type.IsEnum)
            return (string)choice;
        string name = choice.ToString() ?? "";
        FieldInfo? value = Type.GetField(name);
        var key = value?.GetCustomAttribute<LabelKeyAttribute>();
        string full = key != null && !key.malformed ? key.key : $"Mods.{config.Mod.Name}.Configs.{Type.Name}.{name}.Label";
        return Register(full, () => Regex.Replace(name, "([a-z0-9])([A-Z])", "$1 $2")).Value ?? name;
    }

    private static LocalizedText Register(string key, Func<string> fallback)
    {
        var text = new LocalizedText(key, fallback());
        LocalizationKeys.Register(key, text);
        return text;
    }

    public static bool Shown(MemberInfo member)
    {
        if (member.GetCustomAttribute<JsonIgnoreAttribute>() != null && member.GetCustomAttribute<ShowDespiteJsonIgnoreAttribute>() == null)
            return false;
        if (member is PropertyInfo property)
            return property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0 && property.GetMethod!.IsPublic && property.SetMethod!.IsPublic;
        return member is FieldInfo { IsInitOnly: false, IsLiteral: false };
    }
}


