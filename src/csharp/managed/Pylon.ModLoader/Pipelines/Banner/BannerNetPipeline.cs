using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 旗子的多人同步：服务器是唯一权威。
/// 击杀数与可领取面数——新客户端连上时补全量（搭原版「世界数据」消息 7 的车），之后每杀一次推一条增量；
/// 领取是客户端发请求、服务器扣数并发物品（物品本身走原版同步），再推新的可领取面数。
/// 计数本身在 <see cref="BannerTable"/> 的侧表里，所以这里只搬数字，不碰游戏的静态数组。
/// </summary>
internal sealed class BannerNetPipeline : ILoaderModule
{
    private const byte ActionSync = 0, ActionKill = 1, ActionClaimRequest = 2, ActionClaimable = 3;

    /// <summary>原版「世界数据」消息号：新客户端连上时服务器会发它，我们搭这趟车补全量。</summary>
    private const int WorldData = 7;

    public static BannerNetPipeline Instance { get; } = new();

    public string Name => "bannernet";

    private static IModLog Log => LoaderHost.Current.Log;

    private NetPipeline _net = null!;
    private byte _kind;

    public bool Install()
    {
        _net = NetPipeline.Instance;
        _kind = _net.OpenChannel(Receive);

        On.Terraria.NetMessage.SendData += SendData;

        Log.Info("[bannernet] 已挂（服务器推击杀数 / 新客户端连上补全量）");
        return true;
    }

    /// <summary>服务器：某面 mod 旗子的击杀数（以及随之变化的可领取面数）变了，推给所有客户端。</summary>
    internal void PushKill(int banner, int kills)
    {
        if (Main.netMode != 2) return;

        _net.SendValues(_kind, [ActionKill, banner, kills, BannerTable.Claimable(banner)], -1, -1);
    }

    /// <summary>客户端：请服务器领 amount 面 mod 旗子。服务器扣数、发物品、再推新的可领取面数。</summary>
    internal void RequestClaim(int banner, int amount)
    {
        if (Main.netMode != 1) return;

        _net.SendValues(_kind, [ActionClaimRequest, banner, amount], -1, -1);
    }

    private void SendData(On.Terraria.NetMessage.orig_SendData orig, int msgType, int remoteClient, int ignoreClient,
                          Terraria.Localization.NetworkText text, int number, float number2, float number3,
                          float number4, int number5, int number6, int number7)
    {
        orig(msgType, remoteClient, ignoreClient, text, number, number2, number3, number4, number5, number6, number7);

        if (msgType != WorldData) return;
        if (Main.netMode != 2 || remoteClient < 0 || remoteClient >= 255) return;

        foreach (var (banner, kills, claimable) in BannerTable.NetworkState())
        {
            _net.SendValues(_kind, [ActionSync, banner, kills, claimable], remoteClient, -1);
        }
    }

    private void Receive(Il2CppSystem.IO.BinaryReader reader, int from)
    {
        var values = NetCodec.ReadValues(reader);
        if (values.Length < 3) return;

        var action = Convert.ToByte(values[0]);
        var banner = Convert.ToInt32(values[1]);
        var number = Convert.ToInt32(values[2]);

        if (!BannerTable.IsMod(banner)) return;

        switch (action)
        {
            case ActionSync:
            case ActionKill:
                BannerTable.SetKills(banner, number);

                // 老包可能没有第四个数：没有就按"击杀数变了，可领取也得跟着变"的本地算法走一次。
                if (values.Length >= 4) BannerTable.SetClaimable(banner, Convert.ToInt32(values[3]));
                break;

            case ActionClaimable:
                BannerTable.SetClaimable(banner, number);
                break;

            case ActionClaimRequest:
                GrantClaim(banner, number, from);
                break;
        }
    }

    /// <summary>服务器结算领取：扣服务器手上的可领取数，把物品塞进请求者的背包（客户端自己不改数）。</summary>
    private void GrantClaim(int banner, int amount, int who)
    {
        if (Main.netMode != 2) return;
        if ((uint)who >= (uint)Main.player.Length) return;

        var item = BannerTable.ItemForBanner(banner);
        if (item <= 0) return;

        // 先确认收件人还在，再扣数：否则请求者掉线时这一面就被白扣了。
        var player = Main.player[who];
        if (player is null || !player.active) return;

        var granted = BannerTable.Claim(banner, amount);
        if (granted <= 0) return;

        player.QuickSpawnItem(null, item, granted);
        BannerTable.SetFresh(banner, false);

        _net.SendValues(_kind, [ActionClaimable, banner, BannerTable.Claimable(banner)], -1, -1);
    }
}
