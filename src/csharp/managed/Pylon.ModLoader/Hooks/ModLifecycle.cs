using System.Diagnostics;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;
using Terraria.IO;

namespace Pylon.ModLoader;

/// <summary>
/// Mod 的世界 / 每帧生命周期：挂在游戏钩子上，只分发给确实覆写了该方法的 Mod。
/// 订阅表由 <see cref="HookBus"/> 在注册期建好，分派路径上没有字符串与字典。
/// </summary>
internal static class ModLifecycle
{
    private static bool _installed;
    private static bool _unloaded;

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
        var mods = Of(HookId.CanWorldBePlayed);
        for (var i = 0; i < mods.Length; i++)
        {
            try
            {
                if (mods[i].CanWorldBePlayed(player, world)) continue;
                var message = mods[i].WorldCanBePlayedRejectionMessage(player, world);
                if (!string.IsNullOrWhiteSpace(message)) return message;
            }
            catch (Exception e)
            {
                Safe.Report(mods[i].Name + ".CanWorldBePlayed", e);
            }
        }

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
        Each(HookId.ClearWorld, static mod => mod.ClearWorld());
        SystemLoader.ClearWorld();
        SystemLoader.ResetNearbyTileEffects();
        if (Main.netMode == 1)
        {
            Each(HookId.OnWorldLoad, static mod => mod.OnWorldLoad());
            SystemLoader.OnWorldLoad();
        }
    }

    private static void LoadWorld(On.Terraria.IO.WorldFile.orig_LoadWorld orig, bool canCheckFileState)
    {
        var player = Main.ActivePlayerFileData;
        var world = Main.ActiveWorldFileData;
        var rejection = Rejection(player, world);
        if (rejection is not null)
        {
            LoaderHost.Current.Log.Error($"[world] {rejection}");
            return;
        }

        orig(canCheckFileState);

        Each(HookId.OnWorldLoad, static mod => mod.OnWorldLoad());
        SystemLoader.OnWorldLoad();
        LoadWorldData();
        Each(HookId.PostWorldLoad, static mod => mod.PostWorldLoad());
    }

    private static void LoadWorldData()
    {
        var all = WorldSidecar.Read();
        if (all.Count == 0) return;

        foreach (var mod in Of(HookId.LoadWorldData))
        {
            if (!all.TryGetValue(mod.Name, out var value)) continue;
            try
            {
                mod.LoadWorldData(TagCompound.From(value));
            }
            catch (Exception e)
            {
                Safe.Report(mod.Name + ".LoadWorldData", e);
            }
        }

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
        foreach (var mod in Of(HookId.SaveWorldData))
        {
            var tag = new TagCompound();
            try
            {
                mod.SaveWorldData(tag);
            }
            catch (Exception e)
            {
                Safe.Report(mod.Name + ".SaveWorldData", e);
            }

            if (tag.Count > 0) all[mod.Name] = tag;
            else all.Remove(mod.Name);
        }

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
        Each(HookId.PreSaveAndQuit, static mod => mod.PreSaveAndQuit());
        SystemLoader.PreSaveAndQuit();
        SystemLoader.ResetNearbyTileEffects();
        orig();
    }

    private static void WorldUnloadPostfix(On.Terraria.WorldGen.orig_SaveAndQuitCallBack orig,
                                           Il2CppSystem.Object threadContext)
    {
        orig(threadContext);
        Each(HookId.OnWorldUnload, static mod => mod.OnWorldUnload());
        SystemLoader.OnWorldUnload();
    }

    private static void SaveWorldHeaders(TagCompound root)
    {
        var mods = Of(HookId.SaveWorldHeader);
        var systems = SystemLoader.With(HookId.SaveWorldHeader);
        if (mods.IsEmpty && systems.Length == 0) return;

        var headers = root.GetCompound(WorldSidecar.HeadersSection);
        foreach (var mod in mods)
        {
            var tag = new TagCompound();
            try
            {
                mod.SaveWorldHeader(tag);
            }
            catch (Exception e)
            {
                Safe.Report(mod.Name + ".SaveWorldHeader", e);
            }

            if (tag.Count > 0) headers[mod.Name] = tag;
            else headers.Remove(mod.Name);
        }

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
        Each(HookId.PreUpdateWorld, static mod => mod.PreUpdateWorld());
        SystemLoader.PreUpdateWorld();
        orig();
        Each(HookId.PostUpdateWorld, static mod => mod.PostUpdateWorld());
        SystemLoader.PostUpdateWorld();
    }

    private static void UpdateTime(On.Terraria.Main.orig_UpdateTime orig)
    {
        Each(HookId.PreUpdateTime, static mod => mod.PreUpdateTime());
        SystemLoader.PreUpdateTime();
        orig();
        Each(HookId.PostUpdateTime, static mod => mod.PostUpdateTime());
        SystemLoader.PostUpdateTime();
    }

    private static void PreEntitiesPostEverything(On.Terraria.Main.orig_DoUpdateInWorld orig, Main self)
    {
        Each(HookId.PreUpdateEntities, static mod => mod.PreUpdateEntities());
        SystemLoader.PreUpdateEntities();
        orig(self);
        Each(HookId.PostUpdateEverything, static mod => mod.PostUpdateEverything());
        SystemLoader.PostUpdateEverything();
    }

    private static void UpdateDust(On.Terraria.Dust.orig_UpdateDust orig)
    {
        Each(HookId.PreUpdateDusts, static mod => mod.PreUpdateDusts());
        SystemLoader.PreUpdateDusts();
        orig();
        Each(HookId.PostUpdateDusts, static mod => mod.PostUpdateDusts());
        SystemLoader.PostUpdateDusts();
    }

    private static void UpdateInvasions(On.Terraria.Main.orig_UpdateInvasion orig)
    {
        Each(HookId.PreUpdateInvasions, static mod => mod.PreUpdateInvasions());
        SystemLoader.PreUpdateInvasions();
        orig();
        Each(HookId.PostUpdateInvasions, static mod => mod.PostUpdateInvasions());
        SystemLoader.PostUpdateInvasions();
    }

    private static void UpdateUI(On.Terraria.Main.orig_DoUpdate orig, Main self, GameTime gameTime)
    {
        orig(self, gameTime);
        if (Main.gameMenu) return;
        Each(HookId.UpdateUI, mod => mod.UpdateUI(gameTime));
        SystemLoader.UpdateUI(gameTime);
    }

    private static void PostInput(On.Terraria.Main.orig_DoUpdate_HandleInput orig, Main self)
    {
        orig(self);
        Each(HookId.PostUpdateInput, static mod => mod.PostUpdateInput());
        SystemLoader.PostUpdateInput();
    }

    private static void MetricsReset(On.Terraria.SceneMetrics.orig_Reset orig, SceneMetrics self)
    {
        orig(self);
        if (Main.gameMenu || Main.PlayerSceneMetrics is null || self.Pointer != Main.PlayerSceneMetrics.Pointer) return;
        Each(HookId.ResetNearbyTileEffects, static mod => mod.ResetNearbyTileEffects());
        SystemLoader.ResetNearbyTileEffects();
    }

    private static void MetricsCounts(On.Terraria.SceneMetrics.orig_AggregateTileCounts orig, SceneMetrics self)
    {
        orig(self);
        if (Main.gameMenu || Main.PlayerSceneMetrics is null || self.Pointer != Main.PlayerSceneMetrics.Pointer) return;

        var counts = self._tileCounts;
        foreach (var mod in Of(HookId.TileCountsAvailable))
        {
            try
            {
                mod.TileCountsAvailable(counts);
            }
            catch (Exception e)
            {
                Safe.Report(mod.Name + ".TileCountsAvailable", e);
            }
        }

        SystemLoader.TileCountsAvailable(counts.AsSpan());
    }

    private static void Quit(On.Terraria.Main.orig_QuitGame orig, Main self)
    {
        Unload();
        orig(self);
    }
}
