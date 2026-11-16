using Terraria;

namespace Pylon.ModLoader;

/// <summary>玩家客户端状态通道：进世界 SyncPlayer、每帧 clientClone + SendClientChanges、断开 SyncDisconnectedPlayer；补上"我看别人"的那份 mod 状态。</summary>
internal sealed class PlayerNetPipeline : ILoaderModule
{
    public static PlayerNetPipeline Instance { get; } = new();

    private Player _baseline = null!;
    private ModPlayer[] _baselineMods = [];
    private bool _hasBaseline;

    public string Name => "playernet";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Player.clientClone_Player += ClientClone;
        On.Terraria.Player.clientClone_ += ClientCloneSelf;
        On.Terraria.Main.UpdateClient += UpdateClient;
        On.Terraria.NetMessage.SyncOnePlayer += SyncOnePlayer;
        On.Terraria.NetMessage.SyncDisconnectedPlayer += SyncDisconnectedPlayer;
        On.Terraria.Player.Hooks.PlayerConnect += Connected;
        On.Terraria.Player.Hooks.PlayerDisconnect += Disconnected;
        On.Terraria.Player.Hooks.EnterWorld += EnteredWorld;

        Log.Info("[playernet] 已挂（同步 / 客户端状态比对 / 断线）");
        return true;
    }

    private static bool Wants(HookId hook) => PlayerPipeline.Instance.ModsActive(hook);

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
                source[i].CopyClientState(target[i]);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playernet] {source[i].GetType().Name}.CopyClientState 异常：{Safe.Describe(e)}");
            }
        }
    }

    private static Il2CppSystem.Object ClientClone(On.Terraria.Player.orig_clientClone_Player orig, Player self,
                                                  Player clonePlayer)
    {
        var result = orig(self, clonePlayer);

        if (result is Player clone) CopyInto(self, clone);

        return result;
    }

    private static Player ClientCloneSelf(On.Terraria.Player.orig_clientClone_ orig, Player self)
    {
        var clone = orig(self);
        CopyInto(self, clone);
        return clone;
    }

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
                    mods[i].SendClientChanges(_baselineMods[i]);
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
                    current[i].CopyClientState(snapshot[i]);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[playernet] {current[i].GetType().Name}.CopyClientState 异常：{Safe.Describe(e)}");
                }
            }
        }
    }

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
                mods[i].SyncPlayer(toWho, fromWho, newPlayer);
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playernet] {mods[i].GetType().Name}.SyncPlayer 异常：{Safe.Describe(e)}");
            }
        }
    }

    private void Connected(On.Terraria.Player.Hooks.orig_PlayerConnect orig, int playerIndex)
    {
        orig(playerIndex);

        if (Main.netMode == 0 || playerIndex < 0 || playerIndex >= Main.player.Length) return;

        var player = Main.player[playerIndex];
        if (player is null || !player.active) return;

        var mods = PlayerPipeline.Instance.ModsOf(player);
        for (var i = 0; i < mods.Length; i++)
        {
            if (!mods[i].HookEnabled(HookId.PlayerConnect)) continue;

            try
            {
                mods[i].PlayerConnect();
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playernet] {mods[i].GetType().Name}.PlayerConnect 异常：{Safe.Describe(e)}");
            }
        }
    }

    private void Disconnected(On.Terraria.Player.Hooks.orig_PlayerDisconnect orig, int playerIndex)
    {
        if (Main.netMode != 0 && playerIndex >= 0 && playerIndex < Main.player.Length)
        {
            var player = Main.player[playerIndex];
            if (player is not null)
            {
                var mods = PlayerPipeline.Instance.ModsOf(player);
                for (var i = 0; i < mods.Length; i++)
                {
                    if (!mods[i].HookEnabled(HookId.PlayerDisconnect)) continue;

                    try
                    {
                        mods[i].PlayerDisconnect();
                    }
                    catch (Exception e)
                    {
                        ContentErrors.Report($"[playernet] {mods[i].GetType().Name}.PlayerDisconnect(Hooks) 异常：{Safe.Describe(e)}");
                    }
                }
            }
        }

        orig(playerIndex);

        if (playerIndex == Main.myPlayer) _hasBaseline = false;
    }

    private void EnteredWorld(On.Terraria.Player.Hooks.orig_EnterWorld orig, int playerIndex)
    {
        orig(playerIndex);

        if (playerIndex < 0 || playerIndex >= Main.player.Length) return;

        var player = Main.player[playerIndex];
        if (player is null) return;

        if (Main.netMode == 1 && playerIndex == Main.myPlayer)
        {
            var mods = PlayerPipeline.Instance.ModsOf(player);
            for (var i = 0; i < mods.Length; i++)
            {
                if (!mods[i].HookEnabled(HookId.SyncPlayer)) continue;

                try
                {
                    mods[i].SyncPlayer(-1, playerIndex, newPlayer: true);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[playernet] {mods[i].GetType().Name}.SyncPlayer(EnterWorld) 异常：{Safe.Describe(e)}");
                }
            }
        }

        if (!Wants(HookId.OnEnterWorld)) return;

        var list = PlayerPipeline.Instance.ModsOf(player);
        for (var i = 0; i < list.Length; i++)
        {
            if (!list[i].HookEnabled(HookId.OnEnterWorld)) continue;

            try
            {
                list[i].OnEnterWorld();
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playernet] {list[i].GetType().Name}.OnEnterWorld 异常：{Safe.Describe(e)}");
            }
        }
    }

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
                        mods[i].PlayerDisconnect();
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
