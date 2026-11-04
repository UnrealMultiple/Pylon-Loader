using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 玩家客户端状态通道：群落状态机那类"只算自己"的数据要过网，得有人把"我看别人"的那份补上。
/// 原版走法是三段：进世界时 SyncPlayer、每帧 clientClone + SendClientChanges、断开时 SyncDisconnectedPlayer。
/// </summary>
internal sealed class PlayerNetPipeline : ILoaderModule
{
    public static PlayerNetPipeline Instance { get; } = new();

    // 基线：本地玩家上一份"客户端看到的样子"，每帧比对用；换人（重连 / 换槽）就换基线。
    private Player _baseline = null!;
    private ModPlayer[] _baselineMods = [];
    private bool _hasBaseline;

    public string Name => "playernet";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Player.clientClone += ClientClone;
        On.Terraria.Player.clientClone_1 += ClientCloneSelf;
        On.Terraria.Main.UpdateClient += UpdateClient;
        On.Terraria.NetMessage.SyncOnePlayer += SyncOnePlayer;
        On.Terraria.NetMessage.SyncDisconnectedPlayer += SyncDisconnectedPlayer;

        Log.Info("[playernet] 已挂（同步 / 客户端状态比对 / 断线）");
        return true;
    }

    private static bool Wants(HookId hook) => PlayerPipeline.Instance.ModsActive(hook);

    /// <summary>原版克隆玩家之后，把 mod 状态也复制一份（作者在 CopyClientState 里写哪些字段）。</summary>
    private static void CopyInto(Player from, Player to)
    {
        if (!Wants(HookId.CopyClientState)) return;

        var source = PlayerPipeline.Instance.ModsOf(from);
        var target = PlayerPipeline.Instance.ModsOf(to);
        var count = Math.Min(source.Length, target.Length);

        for (var i = 0; i < count; i++)
        {
            if (!source[i].HookEnabled(HookId.CopyClientState)) continue;

            try
            {
                source[i].CopyClientState(from, target[i]);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playernet] {source[i].GetType().Name}.CopyClientState 异常：{Safe.Describe(e)}");
            }
        }
    }

    private static Il2CppSystem.Object ClientClone(On.Terraria.Player.orig_clientClone orig, Player self,
                                                  Player clonePlayer)
    {
        var result = orig(self, clonePlayer);

        if (result is Player clone) CopyInto(self, clone);

        return result;
    }

    private static Player ClientCloneSelf(On.Terraria.Player.orig_clientClone_1 orig, Player self)
    {
        var clone = orig(self);
        CopyInto(self, clone);
        return clone;
    }

    /// <summary>客户端每帧：先和基线比对（SendClientChanges），原版跑完之后重打基线。</summary>
    private void UpdateClient(On.Terraria.Main.orig_UpdateClient orig)
    {
        if (Main.gameMenu || Main.netMode != 1 || Main.player[Main.myPlayer] is not { } me)
        {
            _hasBaseline = false;
            orig();
            return;
        }

        var mods = PlayerPipeline.Instance.ModsOf(me);

        if (_hasBaseline && ReferenceEquals(_baseline, me))
        {
            var count = Math.Min(_baselineMods.Length, mods.Length);
            for (var i = 0; i < count; i++)
            {
                if (!mods[i].HookEnabled(HookId.SendClientChanges)) continue;

                try
                {
                    mods[i].SendClientChanges(me, _baselineMods[i]);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[playernet] {mods[i].GetType().Name}.SendClientChanges 异常：{Safe.Describe(e)}");
                }
            }
        }
        else
        {
            _baselineMods = new ModPlayer[mods.Length];
            for (var i = 0; i < mods.Length; i++) _baselineMods[i] = mods[i];
        }

        orig();

        // 基线在原版同步之后重打：作者在 SendClientChanges 里发的字段下一帧才会被当成"已经发过了"。
        if (!_hasBaseline || !ReferenceEquals(_baseline, me))
        {
            _baseline = me;
            _hasBaseline = true;
        }

        if (Wants(HookId.CopyClientState))
        {
            var snapshot = _baselineMods;
            var current = PlayerPipeline.Instance.ModsOf(me);
            var count = Math.Min(snapshot.Length, current.Length);
            for (var i = 0; i < count; i++)
            {
                if (!current[i].HookEnabled(HookId.CopyClientState)) continue;

                try
                {
                    current[i].CopyClientState(me, snapshot[i]);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[playernet] {current[i].GetType().Name}.CopyClientState 异常：{Safe.Describe(e)}");
                }
            }
        }
    }

    /// <summary>原生玩家同步之后：作者在这里把自己那份数据发出去（通常借 ModPacket）。</summary>
    private void SyncOnePlayer(On.Terraria.NetMessage.orig_SyncOnePlayer orig, int plr, int toWho, int fromWho)
    {
        orig(plr, toWho, fromWho);

        if (!Wants(HookId.SyncPlayer) || plr < 0 || plr >= Main.player.Length) return;

        var player = Main.player[plr];
        if (player is null) return;

        var newPlayer = fromWho == -1;
        var mods = PlayerPipeline.Instance.ModsOf(player);
        for (var i = 0; i < mods.Length; i++)
        {
            if (!mods[i].HookEnabled(HookId.SyncPlayer)) continue;

            try
            {
                mods[i].SyncPlayer(player, toWho, fromWho, newPlayer);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playernet] {mods[i].GetType().Name}.SyncPlayer 异常：{Safe.Describe(e)}");
            }
        }
    }

    /// <summary>断线清场：先把"玩家还在"的最后机会给作者，再交给原版。</summary>
    private void SyncDisconnectedPlayer(On.Terraria.NetMessage.orig_SyncDisconnectedPlayer orig, int plr)
    {
        if (Wants(HookId.PlayerDisconnect) && plr >= 0 && plr < Main.player.Length)
        {
            var player = Main.player[plr];
            if (player is not null)
            {
                var mods = PlayerPipeline.Instance.ModsOf(player);
                for (var i = 0; i < mods.Length; i++)
                {
                    if (!mods[i].HookEnabled(HookId.PlayerDisconnect)) continue;

                    try
                    {
                        mods[i].PlayerDisconnect(player);
                    }
                    catch (Exception e)
                    {
                        ContentErrors.Report($"[playernet] {mods[i].GetType().Name}.PlayerDisconnect 异常：{Safe.Describe(e)}");
                    }
                }
            }
        }

        orig(plr);

        if (plr == Main.myPlayer) _hasBaseline = false;
    }
}
