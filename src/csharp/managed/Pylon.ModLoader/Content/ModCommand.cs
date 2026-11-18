using Il2CppMicrosoft.Xna.Framework.Graphics;

namespace Pylon.ModLoader;

/// <summary>
/// 一条聊天命令："／名字 参数"；Command 与 Type 由作者声明，Action 是命令本体。
/// 抛 <see cref="UsageException"/> 回用法，其余异常记一次后回 Usage。
/// </summary>
public abstract class ModCommand : ModType
{
    public abstract string Command { get; }

    public new abstract CommandType Type { get; }

    public virtual string Usage => "/" + Command;

    public virtual string Description => "";

    public virtual bool IsCaseSensitive => false;

    public override void SetStaticDefaults() { }

    public abstract void Action(CommandCaller caller, string input, string[] args);
}
