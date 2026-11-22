using System.Diagnostics;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.SaveData;
using Terraria;
using Terraria.IO;

namespace Pylon.ModLoader.Hooks;
/// <summary>
/// Mod 的世界 / 每帧生命周期：挂在游戏钩子上，只分发给确实覆写了该方法的 Mod。
/// 订阅表由 <see cref="HookBus"/> 在注册期建好，分派路径上没有字符串与字典。
/// 退出 / 清空的四个时机另有第二通道：观察退出状态位（Main.Quitting）与世界生成、读档入口，
/// 与 WorldGen 三入口的原钩并用；同一事件两路都命中时按幂等门只派一次。
/// </summary>
internal static class ModLifecycle
{
    private static bool _installed;
    private static bool _unloaded;
    private static bool _worldSession;
    private static bool _clearWorldDone;
    private static bool _preSaveDone;
    private static bool _unloadDone;

    private static ModChannel Of(HookId hook) => LoaderHost.Current.Hooks.Channel(hook);

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        ModEnvironmentHooks.Install();
        ModWorldGenHooks.Install();

        On.Terraria.WorldGen.clearWorld += ClearWorldPostfix;
        On.Terraria.IO.WorldFile.LoadWorld += LoadWorld;
        On.Terraria.IO.WorldFile.InternalSaveWorld += SaveWorldPostfix;
        On.Terraria.WorldGen.SaveAndQuit += PreSaveAndQuitPrefix;
        On.Terraria.WorldGen.SaveAndQuitCallBack += WorldUnloadPostfix;
        On.Terraria.WorldGen.Reset += GenerationCleared;
        On.Terraria.Main.set_Quitting += QuittingChanged;
        On.Terraria.WorldGen.UpdateWorld += UpdateWorld;
        On.Terraria.Main.UpdateTime += UpdateTime;
        On.Terraria.Main.DoUpdateInWorld += PreEntitiesPostEverything;
        On.Terraria.Dust.UpdateDust += UpdateDust;
        On.Terraria.Main.UpdateInvasion += UpdateInvasions;
        On.Terraria.Main.DoUpdate += UpdateUI;
        On.Terraria.Main.DoUpdate_HandleInput += PostInput;
        On.Terraria.SceneMetrics.Reset += MetricsReset;
        On.Terraria.SceneMetrics.AggregateTileCounts += MetricsCounts;
        On.Terraria.Main.QuitGame += Quit;
    }

    internal static void Unload()
    {
        if (_unloaded) return;
        _unloaded = true;
        Each(HookId.OnModUnload, static mod => mod.OnModUnload());
        SystemLoader.OnModUnload();
    }

    private static void Each(HookId hook, Action<Mod> call)
    {
        var mods = Of(hook);
        var timed = HookProfile.Enabled;
        for (var i = 0; i < mods.Length; i++)
        {
            var start = timed ? Stopwatch.GetTimestamp() : 0;
            try
            {
                call(mods[i]);
            }
            catch (Exception e)
            {
                Safe.Report(mods[i].Name + "." + hook, e);
            }
            if (timed) HookProfile.Record((int)hook, mods[i].Index, HookProfile.TicksToNs(Stopwatch.GetTimestamp() - start));
        }
    }

    private static string? Rejection(PlayerFileData player, WorldFileData world)
    {
        if (SystemLoader.CanWorldBePlayed(player, world, out var rejector) || rejector is null) return null;
        try
        {
            var message = rejector.WorldCanBePlayedRejectionMessage(player, world);
            if (!string.IsNullOrWhiteSpace(message)) return message;
        }
        catch (Exception e)
        {
            Safe.Report(rejector.FullKey + ".WorldCanBePlayedRejectionMessage", e);
        }
        return rejector.Mod.DisplayName + " does not allow this character in this world";
    }

    private static void ClearWorldPostfix(On.Terraria.WorldGen.orig_clearWorld orig)
    {
        orig();
        var first = !_clearWorldDone;
        if (first)
        {
            _clearWorldDone = true;
            SystemLoader.ClearWorld();
        }
        if (Main.netMode == 1) WorldLoaded();
        if (first) SystemLoader.ResetNearbyTileEffects();
    }

    private static void LoadWorld(On.Terraria.IO.WorldFile.orig_LoadWorld orig, bool canCheckFileState)
    {
        var player = Main.ActivePlayerFileData;
        var world = Main.ActiveWorldFileData;
        var rejection = Rejection(player, world);
        WorldCleared();
        if (rejection is not null)
        {
            _clearWorldDone = false;
            LoaderHost.Current.Log.Error($"[world] {rejection}");
            return;
        }

        orig(canCheckFileState);

        WorldLoaded();
        LoadWorldData();
    }

    private static void GenerationCleared(On.Terraria.WorldGen.orig_Reset orig)
    {
        orig();
        WorldCleared();
    }

    private static void LoadWorldData()
    {
        var all = WorldSidecar.Read();
        if (all.Count == 0) return;

        var systems = SystemLoader.With(HookId.LoadWorldData);
        for (var i = 0; i < systems.Length; i++)
        {
            if (!all.TryGetValue(SystemLoader.KeyOf(systems[i]), out var value)) continue;
            SystemLoader.LoadWorldData(systems[i], TagCompound.From(value));
        }
    }

    private static void SaveWorldPostfix(On.Terraria.IO.WorldFile.orig_InternalSaveWorld orig,
                                         bool useCloudSaving, bool resetTime)
    {
        orig(useCloudSaving, resetTime);
        if (useCloudSaving || Main.worldPathName is null) return;

        var all = WorldSidecar.Read();
        var systems = SystemLoader.With(HookId.SaveWorldData);
        for (var i = 0; i < systems.Length; i++)
        {
            var tag = new TagCompound();
            SystemLoader.SaveWorldData(systems[i], tag);
            var key = SystemLoader.KeyOf(systems[i]);
            if (tag.Count > 0) all[key] = tag;
            else all.Remove(key);
        }

        SaveWorldHeaders(all);
        WorldSidecar.Write(all);
    }

    private static void PreSaveAndQuitPrefix(On.Terraria.WorldGen.orig_SaveAndQuit orig)
    {
        if (!_preSaveDone)
        {
            _preSaveDone = true;
            SystemLoader.ResetNearbyTileEffects();
            SystemLoader.PreSaveAndQuit();
        }
        orig();
    }

    private static void WorldUnloadPostfix(On.Terraria.WorldGen.orig_SaveAndQuitCallBack orig,
                                           Il2CppSystem.Object threadContext)
    {
        orig(threadContext);
        if (_unloadDone) return;
        _unloadDone = true;
        _worldSession = false;
        SystemLoader.OnWorldUnload();
    }

    private static void QuittingChanged(On.Terraria.Main.orig_set_Quitting orig, bool value)
    {
        orig(value);
        if (value)
        {
            if (!_worldSession || _preSaveDone) return;
            _preSaveDone = true;
            SystemLoader.ResetNearbyTileEffects();
            SystemLoader.PreSaveAndQuit();
            return;
        }
        if (!_worldSession || _unloadDone) return;
        _unloadDone = true;
        _worldSession = false;
        SystemLoader.OnWorldUnload();
    }

    private static void WorldCleared()
    {
        if (_clearWorldDone) return;
        _clearWorldDone = true;
        SystemLoader.ClearWorld();
        SystemLoader.ResetNearbyTileEffects();
    }

    private static void WorldLoaded()
    {
        _worldSession = true;
        _clearWorldDone = false;
        _preSaveDone = false;
        _unloadDone = false;
        SystemLoader.OnWorldLoad();
    }

    private static void SaveWorldHeaders(TagCompound root)
    {
        var systems = SystemLoader.With(HookId.SaveWorldHeader);
        if (systems.Length == 0) return;

        var headers = root.GetCompound(WorldSidecar.HeadersSection);
        for (var i = 0; i < systems.Length; i++)
        {
            var tag = new TagCompound();
            SystemLoader.SaveWorldHeader(systems[i], tag);
            var key = SystemLoader.KeyOf(systems[i]);
            if (tag.Count > 0) headers[key] = tag;
            else headers.Remove(key);
        }

        root[WorldSidecar.HeadersSection] = headers;
    }

    private static void UpdateWorld(On.Terraria.WorldGen.orig_UpdateWorld orig)
    {
        SystemLoader.PreUpdateWorld();
        orig();
        SystemLoader.PostUpdateWorld();
    }

    private static void UpdateTime(On.Terraria.Main.orig_UpdateTime orig)
    {
        SystemLoader.PreUpdateTime();
        orig();
        SystemLoader.PostUpdateTime();
    }

    private static void PreEntitiesPostEverything(On.Terraria.Main.orig_DoUpdateInWorld orig, Main self)
    {
        SystemLoader.PreUpdateEntities();
        orig(self);
        SystemLoader.PostUpdateEverything();
    }

    private static void UpdateDust(On.Terraria.Dust.orig_UpdateDust orig)
    {
        SystemLoader.PreUpdateDusts();
        orig();
        SystemLoader.PostUpdateDusts();
    }

    private static void UpdateInvasions(On.Terraria.Main.orig_UpdateInvasion orig)
    {
        SystemLoader.PreUpdateInvasions();
        orig();
        SystemLoader.PostUpdateInvasions();
    }

    private static void UpdateUI(On.Terraria.Main.orig_DoUpdate orig, Main self, GameTime gameTime)
    {
        orig(self, gameTime);
        if (Main.gameMenu) return;
        SystemLoader.UpdateUI(gameTime);
    }

    private static void PostInput(On.Terraria.Main.orig_DoUpdate_HandleInput orig, Main self)
    {
        orig(self);
        SystemLoader.PostUpdateInput();
    }

    private static void MetricsReset(On.Terraria.SceneMetrics.orig_Reset orig, SceneMetrics self)
    {
        orig(self);
        if (Main.gameMenu || Main.PlayerSceneMetrics is null || self.Pointer != Main.PlayerSceneMetrics.Pointer) return;
        SystemLoader.ResetNearbyTileEffects();
    }

    private static void MetricsCounts(On.Terraria.SceneMetrics.orig_AggregateTileCounts orig, SceneMetrics self)
    {
        orig(self);
        if (Main.gameMenu || Main.PlayerSceneMetrics is null || self.Pointer != Main.PlayerSceneMetrics.Pointer) return;

        SystemLoader.TileCountsAvailable(self._tileCounts.AsSpan());
    }

    private static void Quit(On.Terraria.Main.orig_QuitGame orig, Main self)
    {
        Unload();
        orig(self);
    }
}
