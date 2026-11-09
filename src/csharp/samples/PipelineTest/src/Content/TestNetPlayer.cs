#nullable enable
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>
/// 玩家联机通道探针：计 SyncPlayer / CopyClientState / SendClientChanges / OnEnterWorld / PlayerConnect。
/// 这些钩子大多在进世界或联机时才派发，World 组开场先 Reset+开 Counting 太晚（会错过 EnterWorld），
/// 所以默认就计数，World 组只在断言前读值。
/// </summary>
public sealed class TestNetPlayer : ModPlayer
{
    public static int CtSyncPlayer;
    public static int CtCopyClientState;
    public static int CtSendClientChanges;
    public static int CtOnEnterWorld;
    public static int CtPlayerConnect;
    public static int CtPlayerDisconnect;
    /// <summary>最后一次 SyncPlayer 收到的参数（EnterWorld 那次应为 -1/whoAmI/true）。</summary>
    public static string LastSync = "";

    public static void Reset()
    {
        CtSyncPlayer = CtCopyClientState = CtSendClientChanges = 0;
        CtOnEnterWorld = CtPlayerConnect = CtPlayerDisconnect = 0;
        LastSync = "";
    }

    public override void SyncPlayer(Player player, int toWho, int fromWho, bool newPlayer)
    {
        CtSyncPlayer++;
        LastSync = $"{toWho}/{fromWho}/{newPlayer}";
    }

    public override void CopyClientState(Player player, ModPlayer targetCopy) => CtCopyClientState++;

    public override void SendClientChanges(Player player, ModPlayer clientPlayer) => CtSendClientChanges++;

    public override void OnEnterWorld(Player player) => CtOnEnterWorld++;

    public override void PlayerConnect(Player player) => CtPlayerConnect++;

    public override void PlayerDisconnect(Player player) => CtPlayerDisconnect++;
}
