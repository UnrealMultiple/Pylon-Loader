using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Chat;
using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>
/// 命令管线：聊天里以 <c>/</c> 开头且名字命中 mod 命令时由我们跑，命中即吃掉这条消息。
/// /help 会把 mod 命令补在原版列表之后；Console / Item 两种 CommandType 本版不接，见 docs/pipeline/content-command.md。
/// </summary>
internal sealed class CommandPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static CommandPipeline Instance { get; } = new();

    public string Name => "command";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Chat.ChatCommandProcessor.ProcessIncomingMessage += Process;

        Log.Info("[command] 已挂（聊天命令分派 / /help 补 mod 命令 / 权限与别名告警）");
        return true;
    }

    public void Refresh() => CommandTable.Rebuild();

    /// <summary>安装期一次：收表 + 静态默认值。</summary>
    internal void Build()
    {
        CommandTable.Rebuild();

        foreach (var command in CommandTable.All) command.SetStaticDefaults();

        if (CommandTable.Count > 0) Log.Info($"[command] 安装：{CommandTable.Count} 条命令");
    }

    // ===================== 分派 =====================

    private static void Process(On.Terraria.Chat.ChatCommandProcessor.orig_ProcessIncomingMessage orig,
                                ChatCommandProcessor self, ChatMessage message, int clientId)
    {
        var text = message?.Text;

        if (CommandTable.Count == 0 || string.IsNullOrEmpty(text) || text[0] != '/')
        {
            orig(self, message!, clientId);
            return;
        }

        var body = text.Substring(1);
        var space = body.IndexOf(' ');
        var name = space < 0 ? body : body[..space];
        var rest = space < 0 ? "" : body[(space + 1)..];

        if (CommandTable.Find(name) is not { } command)
        {
            orig(self, message!, clientId);

            if (IsHelp(name)) SendHelp(clientId, rest);
            return;
        }

        var caller = new ModCommand.CommandCaller
        {
            Player = PlayerOf(clientId),
            CommandType = CommandType.Chat,
            Reply = (reply, color) => ReplyTo(clientId, reply, color),
        };

        var args = rest.Length == 0 ? [] : rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (!command.AllowedBy(caller.Player))
        {
            ReplyTo(clientId, $"你没有权限用 /{command.Command}", null);
            return;
        }

        try
        {
            command.Action(caller, rest, args);
        }
        catch (UsageException e)
        {
            ReplyTo(clientId, e.Message, null);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[command] {command.Name} 异常：{Safe.Describe(e)}");
        }
    }

    /// <summary>/help 或它的别名 ?。</summary>
    private static bool IsHelp(string name) =>
        string.Equals(name, "help", StringComparison.OrdinalIgnoreCase) || name == "?";

    /// <summary>原版 /help 跑完之后，把 mod 命令补在后面（只在第一页补，避免每页都刷一遍）。</summary>
    private static void SendHelp(int clientId, string rest)
    {
        if (CommandTable.Count == 0) return;

        var page = rest.Trim();
        if (page.Length > 0 && page != "1" && !string.Equals(page, "all", StringComparison.OrdinalIgnoreCase)) return;

        ReplyTo(clientId, $"mod 命令（{CommandTable.Count} 条）：", null);

        for (var index = 0; index < CommandTable.Count; index++)
        {
            var command = CommandTable.At(index);

            string description;
            try
            {
                description = command.Description;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[command] {command.Name}.Description 异常：{Safe.Describe(e)}");
                description = "";
            }

            var line = string.IsNullOrEmpty(description)
                ? command.Usage
                : $"{command.Usage} — {description}";

            ReplyTo(clientId, line, null);
        }
    }

    private static Player PlayerOf(int clientId)
    {
        var players = Main.player;
        if (players is not null && clientId >= 0 && clientId < players.Length && players[clientId] is { active: true } player)
            return player;

        return Main.LocalPlayer;
    }

    private static void ReplyTo(int clientId, string text, Color? color)
    {
        if (string.IsNullOrEmpty(text)) return;

        var use = color ?? ModCommand.ResponseColor;

        if (clientId < 0 || clientId >= 255)
        {
            Main.NewText(text, use);
            return;
        }

        ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral(text), use, clientId);
    }
}
