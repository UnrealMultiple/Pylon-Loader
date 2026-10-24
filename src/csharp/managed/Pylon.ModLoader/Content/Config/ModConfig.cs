using System.Text.RegularExpressions;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Pipelines;
using Terraria.Localization;

namespace Pylon.ModLoader.Content;
/// <summary>
/// 一个 Mod 的一份配置：选项就是 public 字段/属性（特性驱动），字段值即真值；
/// OnChanged 在值真的变了之后调一次；多人归属由 Mode 声明。
/// </summary>
public abstract class ModConfig : ModType
{
    /// <summary>多人同步的归属；作者必须声明（服务器说了算 = ServerSide）。</summary>
    public abstract ConfigScope Mode { get; }

    /// <summary>本地化键的分类（"Configs"）。</summary>
    public virtual string LocalizationCategory => "Configs";

    /// <summary>菜单里显示的名字；默认按类名拆词。</summary>
    public virtual LocalizedText DisplayName =>
        new($"Mods.{Mod?.Name}.{LocalizationCategory}.{Name}.DisplayName", SplitWords(Name));

    /// <summary>"Mod名/配置名"（落盘文件名与跨设备定位用）。</summary>
    public string FullName => $"{Mod?.Name}/{Name}";

    /// <summary>作者可以在这里拒绝自动加载（默认总是加载）。</summary>
    public virtual bool Autoload(ref string name) => true;

    /// <summary>值刚从文件读出来时（Mod 加载期）。</summary>
    public virtual void OnLoaded() { }

    /// <summary>值真的变了（应用/改值之后调一次）。</summary>
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

    /// <summary>复制一份（MemberwiseClone；字段值跟着走）。</summary>
    public virtual ModConfig Clone() => (ModConfig)MemberwiseClone();

    /// <summary>ReloadRequired 的字段与 pending 不一样就要重载；默认不需要。</summary>
    public virtual bool NeedsReload(ModConfig pendingConfig)
    {
        foreach (var option in ConfigReflection.OptionsOf(GetType()))
            if (option.ReloadRequired && !Equals(option.Get(this), option.Get(pendingConfig)))
                return true;
        return false;
    }

    /// <summary>保存并应用改动；pendingConfig 省略时保存自己。</summary>
    public ConfigSaveResult SaveChanges(ModConfig? pendingConfig = null, Action<string, Color>? status = null,
                                        bool silent = true, bool broadcast = true) =>
        ConfigPipeline.Instance.SaveChanges(this, pendingConfig ?? this, status, silent, broadcast);

    /// <summary>打开这份配置的设置界面。</summary>
    public void Open(Action? onClose = null, string? scrollToOption = null, bool centerScrolledOption = true,
                     bool playSound = true) =>
        UI.ModConfigMenu.OpenConfig(this, onClose);

    internal static string SplitWords(string key) =>
        string.IsNullOrEmpty(key)
            ? key
            : Regex.Replace(key, "([a-z0-9])([A-Z])", "$1 $2", RegexOptions.CultureInvariant);
}
