using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>玩家客户端状态通道：进世界 SyncPlayer、每帧 clientClone + SendClientChanges、断开 SyncDisconnectedPlayer；补上"我看别人"的那份 mod 状态。</summary>
internal sealed class PlayerNetPipeline : ILoaderModule
{
    public static PlayerNetPipeline Instance { get; } = new();

    private Player _baseline = null!;
    private nint _baselineSource;

    public string Name => "playernet";

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>playernet 管线的安装门：条件与订阅同居一门，由 GateHub 自动发现与派发。</summary>
    internal static class Gates
    {
        internal struct Sync : IGate
        {
            public static bool Wanted() => PlayerPipeline.Instance.Binding.Count > 0;

            public static void Install()
            {
                On.Terraria.Player.clientClone_Player += ClientClone;
                On.Terraria.Player.clientClone_ += ClientCloneSelf;
                On.Terraria.Main.UpdateClient += Instance.UpdateClient;
                On.Terraria.NetMessage.SyncOnePlayer += Instance.SyncOnePlayer;
                On.Terraria.NetMessage.SyncDisconnectedPlayer += Instance.SyncDisconnectedPlayer;
                On.Terraria.Player.Hooks.PlayerConnect += Instance.Connected;
                On.Terraria.Player.Hooks.PlayerDisconnect += Instance.Disconnected;
                On.Terraria.Player.Hooks.EnterWorld += Instance.EnteredWorld;
                Log.Info("[playernet] 已挂（同步 / 客户端状态比对 / 断线）");
            }
        }
    }

    public bool Install() => GateHub<PlayerNetPipeline>.Run();

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
                ContentErrors.Line($"[playernet] {source[i].GetType().Name}.CopyClientState 异常：{Safe.Dump(e)}");
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
        if (Main.gameMenu || (Main.netMode & 1) == 0)
        {
            Forget();
            orig();
            return;
        }

        var me = Main.player[Main.myPlayer];
        if (me is null)
        {
            orig();
            return;
        }

        if (_baselineSource == me.Pointer && _baseline is not null && _baseline.Pointer != 0)
            SendClientChanges(me, _baseline);

        orig();

        try
        {
            if (_baselineSource != me.Pointer || _baseline is null || _baseline.Pointer == 0)
            {
                _baseline = me.clientClone();
                _baselineSource = me.Pointer;
            }
            else me.clientClone(_baseline);
        }
        catch (Exception e)
        {
            ContentErrors.Line("[playernet] 客户端状态克隆异常：" + Safe.Dump(e));
        }
    }

    private static void SendClientChanges(Player player, Player baseline)
    {
        if (!Wants(HookId.SendClientChanges)) return;

        var current = PlayerPipeline.Instance.ModsOf(player);
        var previous = PlayerPipeline.Instance.ModsOf(baseline);
        var count = Math.Min(current.Length, previous.Length);
        for (var i = 0; i < count; i++)
        {
            if (!current[i].HookEnabled(HookId.SendClientChanges)) continue;

            try
            {
                current[i].SendClientChanges(previous[i]);
            }
            catch (Exception e)
            {
                ContentErrors.Line($"[playernet] {current[i].GetType().Name}.SendClientChanges 异常：{Safe.Dump(e)}");
            }
        }
    }

    private void Forget()
    {
        _baseline = null!;
        _baselineSource = 0;
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
                ContentErrors.Line($"[playernet] {mods[i].GetType().Name}.SyncPlayer 异常：{Safe.Dump(e)}");
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
                ContentErrors.Line($"[playernet] {mods[i].GetType().Name}.PlayerConnect 异常：{Safe.Dump(e)}");
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
                        ContentErrors.Line($"[playernet] {mods[i].GetType().Name}.PlayerDisconnect(Hooks) 异常：{Safe.Dump(e)}");
                    }
                }
            }
        }

        orig(playerIndex);

        if (playerIndex == Main.myPlayer) Forget();
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
                    ContentErrors.Line($"[playernet] {mods[i].GetType().Name}.SyncPlayer(EnterWorld) 异常：{Safe.Dump(e)}");
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
                ContentErrors.Line($"[playernet] {list[i].GetType().Name}.OnEnterWorld 异常：{Safe.Dump(e)}");
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
                        ContentErrors.Line($"[playernet] {mods[i].GetType().Name}.PlayerDisconnect 异常：{Safe.Dump(e)}");
                    }
                }
            }
        }

        orig(plr);

        if (plr == Main.myPlayer) Forget();
    }
}
