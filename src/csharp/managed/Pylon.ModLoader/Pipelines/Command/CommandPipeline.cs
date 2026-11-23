using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Effects;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.Chat;
using Terraria.Localization;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 命令管线：聊天里以 <c>/</c> 开头且名字命中 mod 命令时由我们跑，命中即吃掉这条消息；
/// 客户端发送链在联机时也拦一次；/help 把 mod 命令补在原版列表之后。表格与分派归 <see cref="CommandLoader"/>。
/// </summary>
internal sealed class CommandPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static CommandPipeline Instance { get; } = new();

    public string Name => "command";

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>command 管线的安装门：仅在有 ModCommand 注册时接管聊天分派与 /help。</summary>
    internal static class Gates
    {
        internal struct Dispatch : IGate
        {
            // 框架自带 /fx（屏幕效果调试），所以即使没有 Mod 命令也要接管分派。
            public static bool Wanted() => true;

            public static void Install() => Instance.InstallDispatch();
        }
    }

    public bool Install() => GateHub<CommandPipeline>.Run();

    private void InstallDispatch()
    {
        On.Terraria.Chat.ChatCommandProcessor.ProcessIncomingMessage += Incoming;
        On.Terraria.Chat.ChatHelper.SendChatMessageFromClient += FromClient;
        On.Terraria.Chat.Commands.HelpCommand.ComposeMessage += Help;

        Log.Info("[command] 已挂（聊天命令分派 / 客户端发送链 / /help 补 mod 命令）");
    }

    public void Refresh() => CommandLoader.Clear();

    /// <summary>安装期一次：收表 + 静态默认值。</summary>
    internal void Build()
    {
        CommandLoader.Clear();

        var count = 0;
        foreach (var command in ModContent.GetContent<ModCommand>())
        {
            CommandLoader.Add(command);
            try
            {
                command.SetStaticDefaults();
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[command] {command.Name}.SetStaticDefaults 异常：{Safe.Dump(e)}");
            }
            count++;
        }

        CommandLoader.Add(new ScreenFxCommand());

        if (count > 0) Log.Info($"[command] 安装：{count} 条命令");
    }

    private static string? TextOf(ChatMessage message) => message is null ? null : message.Text;

    private static void Incoming(On.Terraria.Chat.ChatCommandProcessor.orig_ProcessIncomingMessage orig,
                                 ChatCommandProcessor self, ChatMessage message, int clientId)
    {
        string? text = TextOf(message);
        if (Main.netMode != 1 && text is not null && text.StartsWith('/') && Handle(text, clientId))
        {
            message.Consume();
            return;
        }

        orig(self, message, clientId);
    }

    private static bool Handle(string text, int clientId)
    {
        try
        {
            if ((Main.netMode & 2) == 0)
                return CommandLoader.HandleCommand(text, new ChatCommandCaller());
            var player = new PlayerCommandCaller(clientId);
            if (Main.netMode == 2 || clientId != Main.myPlayer)
                return CommandLoader.HandleCommand(text, player);
            return CommandLoader.HandleCommand(text, new ChatCommandCaller()) || CommandLoader.HandleCommand(text, player);
        }
        catch (Exception e)
        {
            ContentErrors.Callback("[command] 分派", e);
            return false;
        }
    }

    private static void FromClient(On.Terraria.Chat.ChatHelper.orig_SendChatMessageFromClient orig, ChatMessage message)
    {
        string? text = TextOf(message);
        if (Main.netMode == 1 && text is not null && text.StartsWith('/'))
        {
            try
            {
                if (CommandLoader.HandleCommand(text, new ChatCommandCaller()))
                    return;
            }
            catch (Exception e)
            {
                ContentErrors.Callback("[command] 客户端分派", e);
            }
        }

        orig(message);
    }

    private static NetworkText Help(On.Terraria.Chat.Commands.HelpCommand.orig_ComposeMessage orig,
                                    Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<LocalizedText>> aliases)
    {
        NetworkText text = orig(aliases);
        CommandType type = (Main.netMode & 2) != 0 ? CommandType.Chat | CommandType.Server : CommandType.Chat;
        string[] lines = CommandLoader.GetHelp(type)
            .Select(h => "/" + h.Item1 + (h.Item2.Length > 0 ? " " + h.Item2 : ""))
            .ToArray();
        return lines.Length == 0 ? text : NetworkText.FromLiteral(text.ToString() + "\n" + string.Join("\n", lines));
    }
}
