using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Chat;
using Terraria.Localization;

namespace Pylon.ModLoader.Content;
/// <summary>命令的调用方：谁打的这条命令（来自哪儿），以及往哪儿回话。</summary>
public interface CommandCaller
{
    CommandType CommandType { get; }

    Player Player { get; }

    void Reply(string text, Color color = default);
}

/// <summary>本机聊天框里的调用方：回话直接进本地聊天。</summary>
internal sealed class ChatCommandCaller : CommandCaller
{
    public CommandType CommandType => CommandType.Chat;

    public Player Player => Main.player[Main.myPlayer];

    public void Reply(string text, Color color = default)
    {
        if (color.R == 0 && color.G == 0 && color.B == 0 && color.A == 0)
            color = new Color(255, 255, 255, 255);
        foreach (string line in text.Split('\n'))
            Main.NewText(line, color.R, color.G, color.B, false);
    }
}

/// <summary>服务器侧代某位玩家执行：回话走聊天包发回那位客户端。</summary>
internal sealed class PlayerCommandCaller : CommandCaller
{
    private readonly int player;

    public PlayerCommandCaller(int player)
    {
        this.player = player;
    }

    public CommandType CommandType => CommandType.Server;

    public Player Player => Main.player[player];

    public void Reply(string text, Color color = default)
    {
        if (color.R == 0 && color.G == 0 && color.B == 0 && color.A == 0)
            color = new Color(255, 255, 255, 255);
        foreach (string line in text.Split('\n'))
            ChatHelper.SendChatMessageToClient(NetworkText.FromLiteral(line), color, player);
    }
}
