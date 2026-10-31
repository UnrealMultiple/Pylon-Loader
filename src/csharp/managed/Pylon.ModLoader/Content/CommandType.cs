namespace Pylon.ModLoader;

/// <summary>命令在哪儿跑（位标志，像 tModLoader 的 CommandType）：Chat 打字者机器、Server 服务器、Console 手机没有、World 单人聊天/多人服务器；用作 Type 的默认值。</summary>
public enum CommandType
{
    Chat = 1,
    Server = 2,
    Console = 4,
    World = 8,
}
