#nullable enable
using Pylon.ModLoader;
using Pylon.ModLoader.Content;

namespace PipelineTest.Content;

/// <summary>
/// 命令探针：覆盖正常分派 / 大小写敏感 / 用法异常。
/// Action 里计静态计数并记参数，分派是否走通看计数；回显走 caller.Reply（进聊天框，肉眼可见）。
/// </summary>
public sealed class TestCommandNormal : ModCommand
{
    public static int Calls;
    public static string LastInput = "";
    public static string LastArgs = "";

    public override string Command => "ptcmd";

    public override CommandType Type => CommandType.Chat;

    public override string Description => "管线测试命令";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        Calls++;
        LastInput = input;
        LastArgs = string.Join(",", args);
        caller.Reply($"ptcmd 收到：input={input} args={LastArgs}");
    }
}

public sealed class TestCommandCase : ModCommand
{
    public static int Calls;

    public override string Command => "PTCase";

    public override CommandType Type => CommandType.Chat;

    public override bool IsCaseSensitive => true;

    public override void Action(CommandCaller caller, string input, string[] args) => Calls++;
}

public sealed class TestCommandWorld : ModCommand
{
    public static int Calls;

    public override string Command => "ptworld";

    public override CommandType Type => CommandType.World;

    public override void Action(CommandCaller caller, string input, string[] args) => Calls++;
}

public sealed class TestCommandUsage : ModCommand
{
    public static int Calls;

    public override string Command => "ptusage";

    public override CommandType Type => CommandType.Chat;

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        Calls++;
        throw new UsageException("参数不对，要三个数字");
    }
}

public sealed class TestCommandUsageBare : ModCommand
{
    public static int Calls;

    public override string Command => "ptbare";

    public override CommandType Type => CommandType.Chat;

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        Calls++;
        throw new UsageException();
    }
}
