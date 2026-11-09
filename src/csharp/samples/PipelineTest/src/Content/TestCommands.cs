#nullable enable
using System;
using Pylon.ModLoader;
using Terraria;
using Il2CppMicrosoft.Xna.Framework;

namespace PipelineTest.Content;

/// <summary>
/// 命令探针：三条命令覆盖正常分派/别名/大小写敏感。
/// Action 里计静态计数并记参数，分派是否走通看计数；回显走 caller.Reply（进聊天框，肉眼可见）。
/// </summary>
public sealed class TestCommandNormal : ModCommand
{
    public static int Calls;
    public static string LastInput = "";
    public static string LastArgs = "";
    public static int Replied;

    public override string Command => "ptcmd";

    public override string Description => "管线测试命令";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        Calls++;
        LastInput = input;
        LastArgs = string.Join(",", args);
        caller.Reply($"ptcmd 收到：input={input} args={LastArgs}", null);
    }
}

public sealed class TestCommandAlias : ModCommand
{
    public static int Calls;
    public static int ViaAlias;

    public override string Command => "ptalias";

    public override string[] Aliases => ["pta"];

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        Calls++;
        if (input.StartsWith("pta", StringComparison.Ordinal)) ViaAlias++;
        caller.Reply("ptalias 收到", null);
    }
}

public sealed class TestCommandCase : ModCommand
{
    public static int Calls;

    public override string Command => "PTCase";

    public override bool IsCaseSensitive => true;

    public override void Action(CommandCaller caller, string input, string[] args) => Calls++;
}

public sealed class TestCommandDenied : ModCommand
{
    public static int Calls;
    public static int DenyChecks;

    public override string Command => "ptdeny";

    public override bool AllowedBy(Player player)
    {
        DenyChecks++;
        return false; // 永远拒绝：验证权限语义
    }

    public override void Action(CommandCaller caller, string input, string[] args) => Calls++;
}

public sealed class TestCommandUsage : ModCommand
{
    public static int Calls;

    public override string Command => "ptusage";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        Calls++;
        throw new UsageException("参数不对，要三个数字");
    }
}
