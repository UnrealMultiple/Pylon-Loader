using System.Collections.Generic;
using Pylon.ModLoader;
using PipelineTest.Content;
using Terraria;
using Terraria.Chat;
using Pylon.ModLoader.Content;

namespace PipelineTest.Tests;

/// <summary>
/// 命令：Ready 验证作者 API 形状、默认值与类型匹配；
/// World 用原版聊天处理入口分派（与真机打字同一入口），命中时消息被吃掉。
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
            c.Eq(new TestCommandNormal().Command, "ptcmd", "命令名");
            c.Eq(new TestCommandNormal().Usage, "/ptcmd", "默认 Usage = /名字");
            c.Eq(new TestCommandCase().Usage, "/PTCase", "Usage 用原样命令名");
            c.True(!new TestCommandNormal().IsCaseSensitive, "默认大小写不敏感");
            c.True(new TestCommandCase().IsCaseSensitive, "IsCaseSensitive 可开");
            c.Eq(new TestCommandNormal().Description, "管线测试命令", "作者描述");
            c.Eq(new TestCommandCase().Description, "", "描述默认空串");
        });

        PT.Case("CMD-R2", c =>
        {
            c.Eq(new TestCommandNormal().Type, CommandType.Chat, "作者的 Type");
            c.Eq((int)CommandType.World, 8, "CommandType.World 值");
            c.True(CommandLoader.Matches(CommandType.World, CommandType.Chat) || Main.netMode != 0,
                "World 在单人映射到 Chat");
            c.True(CommandLoader.Matches(CommandType.Chat, CommandType.Chat), "Chat 匹配 Chat");
            c.True(!CommandLoader.Matches(CommandType.Server, CommandType.Chat), "Server 不匹配 Chat");
        });

        PT.Case("CMD-R3", c =>
        {
            var help = CommandLoader.GetHelp(CommandType.Chat);
            var names = new HashSet<string>();
            foreach (var item in help) names.Add(item.Item1);
            c.True(names.Contains("ptcmd") && names.Contains("PTCase") && names.Contains("ptusage") && names.Contains("ptbare"),
                $"GetHelp 含 mod 命令（{names.Count} 条）");
            c.True(CommandLoader.GetHelp(CommandType.Console).Count == 0, "Console 下没有这些聊天命令");
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
        TestCommandCase.Calls = 0;
        TestCommandWorld.Calls = 0;
        TestCommandUsage.Calls = 0;
        TestCommandUsageBare.Calls = 0;

        PT.Later("CMD-W1", 5, c =>
        {
            Dispatch("/ptcmd 42 hello");
            c.True(TestCommandNormal.Calls > 0, $"/ptcmd 被分派（{TestCommandNormal.Calls} 次）");
            c.Eq(TestCommandNormal.LastArgs, "42,hello", "参数解析（空格切分）");
            c.Eq(TestCommandNormal.LastInput, "/ptcmd 42 hello", "input 是整段原文（含命令名）");
        });

        PT.Later("CMD-W2", 15, c =>
        {
            var before = TestCommandCase.Calls;
            Dispatch("/ptcase");
            c.True(TestCommandCase.Calls > before, "匹配始终不分大小写（基线形状）");
            Dispatch("/PTCase");
            c.True(TestCommandCase.Calls > before + 1, "原样大小写也命中");
        });

        PT.Later("CMD-W3", 25, c =>
        {
            var before = TestCommandNormal.Calls;
            Dispatch("/PTCMD");
            c.True(TestCommandNormal.Calls > before, "默认大小写不敏感（大写也命中）");
        });

        PT.Later("CMD-W4", 35, c =>
        {
            var before = TestCommandWorld.Calls;
            Dispatch("/ptworld");
            c.True(TestCommandWorld.Calls > before, "World 类型在单人聊天里可调用");
        });

        PT.Later("CMD-W5", 45, c =>
        {
            var before = TestCommandUsage.Calls;
            Dispatch("/ptusage");
            c.True(TestCommandUsage.Calls > before, "UsageException 前的 Action 已跑");

            var bareBefore = TestCommandUsageBare.Calls;
            Dispatch("/ptbare");
            c.True(TestCommandUsageBare.Calls > bareBefore, "无消息的 UsageException 也不崩");
        });

        PT.Later("CMD-W6", 55, c =>
        {
            var msg = new ChatMessage("/ptcmd 7");
            var processor = new ChatCommandProcessor();
            processor.ProcessIncomingMessage(msg, Main.myPlayer);
            c.True(msg.IsConsumed, "命中 mod 命令的消息被吃掉");
            c.Eq(TestCommandNormal.LastArgs, "7", "被吃掉前命令已执行");
        });

        PT.Later("CMD-W7", 65, c =>
        {
            var msg = new ChatMessage("/definitelynotacommand");
            var processor = new ChatCommandProcessor();
            processor.ProcessIncomingMessage(msg, Main.myPlayer);
            c.True(!msg.IsConsumed, "未命中消息不被吃");
        });
    }

    private static void Dispatch(string text)
    {
        var message = new ChatMessage(text);
        var processor = new ChatCommandProcessor();
        processor.ProcessIncomingMessage(message, Main.myPlayer);
    }
}
