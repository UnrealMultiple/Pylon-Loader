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

    private static ReadOnlySpan<Mod> Of(HookId hook) => LoaderHost.Current.Hooks.Channel(hook);

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
        On.Terraria.Main.UpdateUIStates += UpdateUI;
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
    }

    private static void Each(HookId hook, Action<Mod> call)
    {
        var mods = Of(hook);
        for (var i = 0; i < mods.Length; i++)
        {
            try
            {
                call(mods[i]);
            }
            catch (Exception e)
            {
                Safe.Report(mods[i].Name + "." + hook, e);
            }
        }
    }

    /// <summary>只要有 Mod 拒绝就返回 false（LoadWorld 用来决定是否放行）。</summary>
    private static bool All(HookId hook, ModPredicate predicate)
    {
        var mods = Of(hook);
        var allowed = true;
        for (var i = 0; i < mods.Length; i++)
        {
            try
            {
                if (!predicate(mods[i])) allowed = false;
            }
            catch (Exception e)
            {
                Safe.Report(mods[i].Name + "." + hook, e);
            }
        }
        return allowed;
    }

    private delegate bool ModPredicate(Mod mod);

    // ===================== 世界 =====================

    private static void ClearWorldPostfix(On.Terraria.WorldGen.orig_clearWorld orig)
    {
        orig();
        Each(HookId.ClearWorld, static mod => mod.ClearWorld());
        if (Main.netMode == 1) Each(HookId.OnWorldLoad, static mod => mod.OnWorldLoad());
    }

    private static void LoadWorld(On.Terraria.IO.WorldFile.orig_LoadWorld orig, bool canCheckFileState)
    {
        var player = Main.ActivePlayerFileData;
        var world = Main.ActiveWorldFileData;
        if (!All(HookId.CanWorldBePlayed, mod => mod.CanWorldBePlayed(player, world)))
        {
            Safe.Once("mod.world.rejected", "世界被 Mod 的 CanWorldBePlayed 拒绝了这次进入", true);
            return;
        }

        orig(canCheckFileState);

        Each(HookId.OnWorldLoad, static mod => mod.OnWorldLoad());
        LoadWorldData();
        Each(HookId.PostWorldLoad, static mod => mod.PostWorldLoad());
    }

    private static void LoadWorldData()
    {
        var file = WorldDataFile();
        if (file is null || !File.Exists(file)) return;

        var all = TagJson.Read(file);
        var mods = Of(HookId.LoadWorldData);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
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
    }

    private static void SaveWorldPostfix(On.Terraria.IO.WorldFile.orig_InternalSaveWorld orig,
                                         bool useCloudSaving, bool resetTime)
    {
        orig(useCloudSaving, resetTime);
        if (useCloudSaving || Main.worldPathName is null) return;

        var file = WorldDataFile();
        if (file is null) return;

        var all = TagJson.Read(file);
        var mods = Of(HookId.SaveWorldData);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
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

        try
        {
            if (all.Count > 0) TagJson.Write(file, all);
            else if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception e)
        {
            Safe.Report("Mod 世界存档", e);
        }

        SaveWorldHeaders();
    }

    private static void PreSaveAndQuitPrefix(On.Terraria.WorldGen.orig_SaveAndQuit orig)
    {
        Each(HookId.PreSaveAndQuit, static mod => mod.PreSaveAndQuit());
        orig();
    }

    private static void WorldUnloadPostfix(On.Terraria.WorldGen.orig_SaveAndQuitCallBack orig,
                                           Il2CppSystem.Object threadContext)
    {
        orig(threadContext);
        Each(HookId.OnWorldUnload, static mod => mod.OnWorldUnload());
    }

    private static void SaveWorldHeaders()
    {
        var mods = Of(HookId.SaveWorldHeader);
        if (mods.IsEmpty || string.IsNullOrEmpty(Main.worldPathName)) return;

        var file = Main.worldPathName + ".wld.header.json";
        var all = TagJson.Read(file);
        for (var i = 0; i < mods.Length; i++)
        {
            var mod = mods[i];
            var tag = new TagCompound();
            try
            {
                mod.SaveWorldHeader(tag);
            }
            catch (Exception e)
            {
                Safe.Report(mod.Name + ".SaveWorldHeader", e);
            }

            if (tag.Count > 0) all[mod.Name] = tag;
            else all.Remove(mod.Name);
        }

        try
        {
            if (all.Count > 0) TagJson.Write(file, all);
            else if (File.Exists(file)) File.Delete(file);
        }
        catch (Exception e)
        {
            Safe.Report("Mod 世界头存档", e);
        }
    }

    // ===================== 每帧 =====================

    private static void UpdateWorld(On.Terraria.WorldGen.orig_UpdateWorld orig)
    {
        Each(HookId.PreUpdateWorld, static mod => mod.PreUpdateWorld());
        orig();
        Each(HookId.PostUpdateWorld, static mod => mod.PostUpdateWorld());
    }

    private static void UpdateTime(On.Terraria.Main.orig_UpdateTime orig)
    {
        Each(HookId.PreUpdateTime, static mod => mod.PreUpdateTime());
        orig();
        Each(HookId.PostUpdateTime, static mod => mod.PostUpdateTime());
    }

    private static void PreEntitiesPostEverything(On.Terraria.Main.orig_DoUpdateInWorld orig, Main self)
    {
        Each(HookId.PreUpdateEntities, static mod => mod.PreUpdateEntities());
        orig(self);
        Each(HookId.PostUpdateEverything, static mod => mod.PostUpdateEverything());
    }

    private static void UpdateDust(On.Terraria.Dust.orig_UpdateDust orig)
    {
        Each(HookId.PreUpdateDusts, static mod => mod.PreUpdateDusts());
        orig();
        Each(HookId.PostUpdateDusts, static mod => mod.PostUpdateDusts());
    }

    private static void UpdateInvasions(On.Terraria.Main.orig_UpdateInvasion orig)
    {
        Each(HookId.PreUpdateInvasions, static mod => mod.PreUpdateInvasions());
        orig();
        Each(HookId.PostUpdateInvasions, static mod => mod.PostUpdateInvasions());
    }

    private static void UpdateUI(On.Terraria.Main.orig_UpdateUIStates orig, GameTime gameTime)
    {
        orig(gameTime);
        if (Main.gameMenu) return;
        Each(HookId.UpdateUI, mod => mod.UpdateUI(gameTime));
    }

    private static void PostInput(On.Terraria.Main.orig_DoUpdate_HandleInput orig, Main self)
    {
        orig(self);
        Each(HookId.PostUpdateInput, static mod => mod.PostUpdateInput());
    }

    private static void MetricsReset(On.Terraria.SceneMetrics.orig_Reset orig, SceneMetrics self)
    {
        orig(self);
        if (Main.gameMenu || self != Main.PlayerSceneMetrics) return;
        Each(HookId.ResetNearbyTileEffects, static mod => mod.ResetNearbyTileEffects());
    }

    private static void MetricsCounts(On.Terraria.SceneMetrics.orig_AggregateTileCounts orig, SceneMetrics self)
    {
        orig(self);
        if (Main.gameMenu || self != Main.PlayerSceneMetrics) return;

        var counts = self._tileCounts;
        var mods = Of(HookId.TileCountsAvailable);
        for (var i = 0; i < mods.Length; i++)
        {
            try
            {
                mods[i].TileCountsAvailable(counts);
            }
            catch (Exception e)
            {
                Safe.Report(mods[i].Name + ".TileCountsAvailable", e);
            }
        }
    }

    private static void Quit(On.Terraria.Main.orig_QuitGame orig, Main self)
    {
        Unload();
        orig(self);
    }

    private static string? WorldDataFile()
    {
        var path = Main.worldPathName;
        return string.IsNullOrEmpty(path) ? null : path + ".wld.bl.json";
    }
}
