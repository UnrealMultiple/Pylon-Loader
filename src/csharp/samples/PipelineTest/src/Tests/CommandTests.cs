using Pylon.ModLoader;
using PipelineTest.Content;
using Terraria;
using Terraria.Chat;

namespace PipelineTest.Tests;

/// <summary>
/// 管线：Command —— 真机验证。
/// Ready：五条测试命令的 API 面（名字/别名/大小写/用法/描述默认值）。
/// World：用真实聊天入口分派（ChatHelper 走原版客户端发送链，服务端钩子吃掉命令）；
/// MIUI 输入注入被拒，所以直接构造聊天消息调 ChatCommandProcessor.ProcessIncomingMessage。
/// </summary>
internal static class CommandTests
{
    public static void Register()
    {
        PT.Group("command", Phase.Ready, RunReady);
        PT.Group("command-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("CMD-R1", c =>
        {
            // API 面：名字、默认 Usage、描述默认回退。
            c.Eq(new TestCommandNormal().Command, "ptcmd", "命令名");
            c.Eq(new TestCommandNormal().Usage, "/ptcmd", "默认 Usage = /名字");
            c.True(!new TestCommandNormal().IsCaseSensitive, "默认大小写不敏感");
            c.True(new TestCommandCase().IsCaseSensitive, "IsCaseSensitive 可开");
            c.Eq(new TestCommandNormal().Description, "管线测试命令", "描述（本地化键缺失回退到类里的默认）");
        });

        PT.Case("CMD-R2", c =>
        {
            // 别名声明。
            var aliases = new TestCommandAlias().Aliases;
            c.True(aliases is { Length: 1 } && aliases[0] == "pta", "别名 pta 已声明");
            c.Eq(new TestCommandNormal().Type, CommandType.Chat, "默认 Type = Chat");
        });

        PT.Case("CMD-R3", c =>
        {
            // ResponseColor 是 /help 那个黄。
            var color = ModCommand.ResponseColor;
            c.True(color.R == 255 && color.G == 240 && color.B == 20, $"ResponseColor = (255,240,20)（实际 {color.R},{color.G},{color.B}）");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("CMD-W*", "玩家还没就绪");
            return;
        }

        TestCommandNormal.Calls = 0;
        TestCommandAlias.Calls = 0;
        TestCommandAlias.ViaAlias = 0;
        TestCommandCase.Calls = 0;
        TestCommandDenied.Calls = 0;
        TestCommandDenied.DenyChecks = 0;
        TestCommandUsage.Calls = 0;

        // W1：真分派 —— 构造聊天消息走原版处理链（与真机打字同一入口）。
        PT.Later("CMD-W1", 5, c =>
        {
            Dispatch("/ptcmd 42 hello");
            c.True(TestCommandNormal.Calls > 0,
                $"/ptcmd 被分派（{TestCommandNormal.Calls} 次）");
            c.Eq(TestCommandNormal.LastArgs, "42,hello", "参数解析（空格切分）");
        });

        // W2：别名命中。
        PT.Later("CMD-W2", 15, c =>
        {
            Dispatch("/pta");
            c.True(TestCommandAlias.Calls > 0, $"别名分派到同一命令（{TestCommandAlias.Calls} 次）");
        });

        // W3：大小写敏感命令：小写不命中（落到原版 → 无回显），原样大小写命中。
        PT.Later("CMD-W3", 25, c =>
        {
            var before = TestCommandCase.Calls;
            Dispatch("/ptcase"); // 全小写：不应命中
            c.Eq(TestCommandCase.Calls, before, "小写输入不命中大小写敏感命令");
            Dispatch("/PTCase");
            c.True(TestCommandCase.Calls > before, "原样大小写命中");
        });

        // W4：AllowedBy 拒绝 → Action 不跑，走权限拒绝回显。
        PT.Later("CMD-W4", 35, c =>
        {
            Dispatch("/ptdeny");
            c.True(TestCommandDenied.DenyChecks > 0, "AllowedBy 被问过");
            c.Eq(TestCommandDenied.Calls, 0, "被拒绝的命令 Action 不执行");
        });

        // W5：UsageException → Action 计数到了，异常被吃、回显错误文本（不崩）。
        PT.Later("CMD-W5", 45, c =>
        {
            Dispatch("/ptusage");
            c.True(TestCommandUsage.Calls > 0, "UsageException 前的 Action 已跑");
        });

        // W6：/help 补 mod 命令（回显计数在 Normal 命令里看不见；靠不崩 + 后续日志）。
        PT.Later("CMD-W6", 55, c =>
        {
            Dispatch("/help 1");
            PT.Info("已发 /help 1：聊天框应在原版列表后补 5 条 mod 命令（ptcmd/ptalias/PTCase/ptdeny/ptusage）");
            c.True(true, "/help 不崩（回显内容肉眼核对）");
        });

        // W7：收尾：重置计数。
        PT.Later("CMD-W7", 65, c =>
        {
            TestCommandNormal.Calls = 0;
            TestCommandAlias.Calls = 0;
            TestCommandCase.Calls = 0;
            TestCommandDenied.Calls = 0;
            TestCommandUsage.Calls = 0;
        });
    }

    /// <summary>
    /// 构造一条聊天消息走原版 ChatCommandProcessor：与真机打字同一个入口，
    /// 命令命中时会被管线的 On 钩子吃掉。
    /// </summary>
    private static void Dispatch(string text)
    {
        // ChatMessage 的托管构造在手机版代理上是最简单的文本 ctor；clientId 用本地玩家。
        var message = new ChatMessage(text);
        var processor = new ChatCommandProcessor();
        processor.ProcessIncomingMessage(message, Main.myPlayer);
    }
}
