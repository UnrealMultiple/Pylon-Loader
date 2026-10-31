using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>一条聊天命令，像 tModLoader 的 ModCommand："／名字 参数"；一个类一个实例（别名是 ExMod 那套）。</summary>
public abstract class ModCommand : ModType
{
    /// <summary>命令的调用方：谁打的这条命令，以及往哪儿回话。</summary>
    public sealed class CommandCaller
    {
        /// <summary>打这条命令的玩家。</summary>
        public Player Player { get; set; } = null!;

        /// <summary>这次调用来自哪儿（CommandLoader.Matches 就是拿它和 Type 对）。</summary>
        public CommandType CommandType { get; set; }

        /// <summary>回一句话；color 省略 = 游戏的白。</summary>
        public Action<string, Color?> Reply { get; set; } = null!;
    }

    /// <summary>命令名，不带斜杠（默认空 = 子类必须写）。</summary>
    public virtual string Command => "";

    /// <summary>在哪儿跑（默认聊天；基类 ModType.Type 是内容编号，命令不占号，这里显式遮蔽）。</summary>
    public new virtual CommandType Type => CommandType.Chat;

    /// <summary>同一个命令的其它名字（来自 ExMod；默认没有）。</summary>
    public virtual string[] Aliases => [];

    /// <summary>回给玩家的用法；本地化没这段文本时给 "/" + Command。</summary>
    public virtual string Usage
    {
        get
        {
            var text = Text("Usage");
            return string.IsNullOrEmpty(text) ? "/" + Command : text;
        }
    }

    /// <summary>给 /help 看的说明；本地化没这段文本时给 ""。</summary>
    public virtual string Description => Text("Description");

    /// <summary>false = 名字大小写不敏感，传进来的文本已经是小写。</summary>
    public virtual bool IsCaseSensitive => false;

    /// <summary>默认什么都不做。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>命令本体：caller.Player、caller.CommandType、caller.Reply；抛 UsageException 就回那句错。</summary>
    public virtual void Action(CommandCaller caller, string input, string[] args) { }

    /// <summary>游戏回话的颜色（就是 /help 那个黄）。</summary>
    public static Color ResponseColor => new(255, 240, 20);

    // 本地化键是 "Mods.<modid>.Commands.<类名>.<what>"；Mod 未挂上或没这段文本时给 ""。
    private string Text(string what)
    {
        var modId = Mod != null ? Mod.Name : null;
        if (string.IsNullOrEmpty(modId)) return "";

        return ModLocalization.TryTranslate("Mods." + modId + ".Commands." + GetType().Name + "." + what);
    }
}
