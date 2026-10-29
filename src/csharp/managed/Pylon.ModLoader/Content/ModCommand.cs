using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>命令在哪儿跑（位标志，像 tModLoader 的 CommandType）：Chat 打字者机器、Server 服务器、Console 手机没有、World 单人聊天/多人服务器；用作 Type 的默认值。</summary>
    public enum CommandType
    {
        Chat = 1, Server = 2, Console = 4, World = 8,
    }

    /// <summary>回给玩家的那句错（消息 + 颜色）；不填消息就回 Usage。</summary>
    public class UsageException : Exception
    {
        /// <summary>消息可以没有；颜色 null = 管线用自己的红。</summary>
        public UsageException(string msg = null, Color? color = null)
            : base(msg ?? "")
        {
            Msg = msg;
            Color = color;
        }

        /// <summary>要回给玩家的文本（null = 没写，管线改成回 Usage）。</summary>
        public string Msg { get; }

        /// <summary>回这句话的颜色（null = 用管线的红）。</summary>
        public Color? Color { get; }
    }

    /// <summary>一条聊天命令，像 tModLoader 的 ModCommand："／名字 参数"；一个类一个实例（别名是 ExMod 那套）。</summary>
    public abstract class ModCommand : ModType
    {
        /// <summary>命令的调用方：谁打的这条命令，以及往哪儿回话。</summary>
        public sealed class CommandCaller
        {
            /// <summary>打这条命令的玩家。</summary>
            public Player Player { get; set; }

            /// <summary>这次调用来自哪儿（CommandLoader.Matches 就是拿它和 Type 对）。</summary>
            public CommandType CommandType { get; set; }

            /// <summary>回一句话；color 省略 = 游戏的白。</summary>
            public Action<string, Color?> Reply { get; set; }
        }

        /// <summary>命令名，不带斜杠（默认空 = 子类必须写）。</summary>
        public virtual string Command => "";

        /// <summary>在哪儿跑（默认聊天；基类 ModType.Type 是内容编号，命令不占号，这里显式遮蔽）。</summary>
        public new virtual CommandType Type => CommandType.Chat;

        /// <summary>同一个命令的其它名字（来自 ExMod；默认没有）。</summary>
        public virtual string[] Aliases => new string[0];

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
        public static Color ResponseColor => new Color(255, 240, 20);

        // #Text："Mods.<modid>.Commands.<类名>.<what>" 的本地化文本；Mod 未挂上或没这段文本时给 ""
        private string Text(string what)
        {
            var modId = Mod != null ? Mod.Name : null;
            if (string.IsNullOrEmpty(modId)) return "";

            return ModLocalization.TryTranslate("Mods." + modId + ".Commands." + GetType().Name + "." + what);
        }
    }
}
