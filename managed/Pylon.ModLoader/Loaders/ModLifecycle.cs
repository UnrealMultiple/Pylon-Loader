using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;
using Terraria.IO;

namespace Pylon.ModLoader
{
    /// <summary>Mod 的世界/每帧生命周期（原 ModSystem 职责，现挂在 Mod 上）：只在覆写对应方法时分派；世界存档 &lt;世界&gt;.wld.bl.json。</summary>
    internal static class ModLifecycle
    {
        private static readonly Dictionary<Type, OverridePlan> Plans = new();
        private static bool _installed;
        private static bool _unloaded;

        private static OverridePlan PlanOf(Mod mod)
        {
            var type = mod.GetType();
            if (Plans.TryGetValue(type, out var plan)) return plan;
            plan = OverridePlan.Build(type, typeof(Mod));
            Plans[type] = plan;
            return plan;
        }

        private static bool Has(Mod mod, string method) => PlanOf(mod).Has(method);

        /// <summary>每个 Mod 加载时调：装 hook（一次）并把它覆写的就绪任务排进 Ready。</summary>
        internal static void Register(Mod mod)
        {
            if (mod == null) return;
            var plan = PlanOf(mod);

            if (plan.Has(nameof(Mod.ResizeArrays))) Ready.Add(mod.ResizeArrays, ReadyPhase.Resize);
            if (plan.Has(nameof(Mod.AddRecipeGroups))) Ready.Add(mod.AddRecipeGroups, ReadyPhase.Groups);
            if (plan.Has(nameof(Mod.AddRecipes))) Ready.Add(mod.AddRecipes, ReadyPhase.Recipes);
            if (plan.Has(nameof(Mod.PostAddRecipes))) Ready.Add(mod.PostAddRecipes, ReadyPhase.PostRecipes);
            if (plan.Has(nameof(Mod.PostSetupRecipes))) Ready.Add(mod.PostSetupRecipes, ReadyPhase.Finish);

            Install(mod.Harmony, mod.Logger);
        }

        private static void Each(string method, Action<Mod> call)
        {
            var mods = ModLoader.Mods;
            for (var i = 0; i < mods.Count; i++)
            {
                var mod = mods[i];
                if (!Has(mod, method)) continue;
                try { call(mod); }
                catch (Exception e) { Safe.Report(mod.Name + "." + method, e); }
            }
        }

        private static bool Any(string method, Func<Mod, bool> call)
        {
            var mods = ModLoader.Mods;
            var result = false;
            for (var i = 0; i < mods.Count; i++)
            {
                var mod = mods[i];
                if (!Has(mod, method)) continue;
                try { if (!call(mod)) result = true; }
                catch (Exception e) { Safe.Report(mod.Name + "." + method, e); }
            }
            return result;
        }

        internal static void Unload()
        {
            if (_unloaded) return;
            _unloaded = true;
            Each(nameof(Mod.OnModUnload), mod => mod.OnModUnload());
        }

        // ===================== 装 hook =====================

        private static void Install(Harmony harmony, ModLogger log)
        {
            if (_installed || harmony == null) return;
            _installed = true;

            void Patch(Type type, string name, Type[] args, string prefix = null, string postfix = null) =>
                GameHooks.Patch(harmony, log, typeof(ModLifecycle), type, name, args, prefix, postfix);

            Patch(typeof(WorldGen), "clearWorld", Type.EmptyTypes, postfix: nameof(ClearWorldPostfix));
            Patch(typeof(WorldFile), "LoadWorld", new[] { typeof(bool) }, nameof(LoadWorldPrefix), nameof(LoadWorldPostfix));
            Patch(typeof(WorldFile), "InternalSaveWorld", new[] { typeof(bool), typeof(bool) }, postfix: nameof(SaveWorldPostfix));
            Patch(typeof(WorldGen), "SaveAndQuit", Type.EmptyTypes, nameof(PreSaveAndQuitPrefix));
            Patch(typeof(WorldGen), "SaveAndQuitCallBack", new[] { typeof(Il2CppSystem.Object) }, postfix: nameof(WorldUnloadPostfix));
            Patch(typeof(WorldGen), "UpdateWorld", Type.EmptyTypes, nameof(UpdateWorldPrefix), nameof(UpdateWorldPostfix));
            Patch(typeof(Main), "UpdateTime", Type.EmptyTypes, nameof(UpdateTimePrefix), nameof(UpdateTimePostfix));
            Patch(typeof(Main), "DoUpdateInWorld", Type.EmptyTypes, nameof(PreEntitiesPrefix), nameof(PostEverythingPostfix));
            Patch(typeof(Dust), "UpdateDust", Type.EmptyTypes, nameof(DustPrefix), nameof(DustPostfix));
            Patch(typeof(Main), "UpdateInvasion", Type.EmptyTypes, nameof(InvasionPrefix), nameof(InvasionPostfix));
            Patch(typeof(Main), "UpdateUIStates", new[] { typeof(GameTime) }, postfix: nameof(UpdateUIPostfix));
            Patch(typeof(Main), "DoUpdate_HandleInput", Type.EmptyTypes, postfix: nameof(PostInputPostfix));
            Patch(typeof(SceneMetrics), "Reset", Type.EmptyTypes, postfix: nameof(MetricsResetPostfix));
            Patch(typeof(SceneMetrics), "AggregateTileCounts", Type.EmptyTypes, postfix: nameof(MetricsCountsPostfix));
            Patch(typeof(Main), "QuitGame", Type.EmptyTypes, nameof(QuitPrefix));
        }

        // ===================== 世界生命周期 =====================

        private static void ClearWorldPostfix()
        {
            Each(nameof(Mod.ClearWorld), mod => mod.ClearWorld());
            if (Main.netMode == 1) Each(nameof(Mod.OnWorldLoad), mod => mod.OnWorldLoad());
        }

        private static bool LoadWorldPrefix(bool canCheckFileState)
        {
            var player = Main.ActivePlayerFileData;
            var world = Main.ActiveWorldFileData;
            if (!Any(nameof(Mod.CanWorldBePlayed), mod => mod.CanWorldBePlayed(player, world))) return true;

            Safe.Once("mod.world.rejected", "世界被 Mod 的 CanWorldBePlayed 拒绝了这次进入", true);
            return false;
        }

        private static void LoadWorldPostfix()
        {
            Each(nameof(Mod.OnWorldLoad), mod => mod.OnWorldLoad());

            var file = WorldDataFile();
            if (file != null && File.Exists(file))
            {
                var all = TagJson.Read(file);
                foreach (var mod in ModLoader.Mods)
                {
                    if (!Has(mod, nameof(Mod.LoadWorldData))) continue;
                    if (!all.TryGetValue(mod.Name, out var value)) continue;
                    try { mod.LoadWorldData(TagCompound.From(value)); }
                    catch (Exception e) { Safe.Report(mod.Name + ".LoadWorldData", e); }
                }
            }
            Each(nameof(Mod.PostWorldLoad), mod => mod.PostWorldLoad());
        }

        private static void SaveWorldPostfix(bool useCloudSaving)
        {
            if (useCloudSaving || Main.worldPathName == null) return;
            var file = WorldDataFile();
            if (file == null) return;

            var all = TagJson.Read(file);
            foreach (var mod in ModLoader.Mods)
            {
                if (!Has(mod, nameof(Mod.SaveWorldData))) continue;
                var tag = new TagCompound();
                try { mod.SaveWorldData(tag); }
                catch (Exception e) { Safe.Report(mod.Name + ".SaveWorldData", e); }
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
        }

        private static void PreSaveAndQuitPrefix() => Each(nameof(Mod.PreSaveAndQuit), mod => mod.PreSaveAndQuit());
        private static void WorldUnloadPostfix() => Each(nameof(Mod.OnWorldUnload), mod => mod.OnWorldUnload());

        private static void UpdateWorldPrefix() => Each(nameof(Mod.PreUpdateWorld), mod => mod.PreUpdateWorld());
        private static void UpdateWorldPostfix() => Each(nameof(Mod.PostUpdateWorld), mod => mod.PostUpdateWorld());
        private static void UpdateTimePrefix() => Each(nameof(Mod.PreUpdateTime), mod => mod.PreUpdateTime());
        private static void UpdateTimePostfix() => Each(nameof(Mod.PostUpdateTime), mod => mod.PostUpdateTime());
        private static void PreEntitiesPrefix() => Each(nameof(Mod.PreUpdateEntities), mod => mod.PreUpdateEntities());
        private static void PostEverythingPostfix() => Each(nameof(Mod.PostUpdateEverything), mod => mod.PostUpdateEverything());
        private static void DustPrefix() => Each(nameof(Mod.PreUpdateDusts), mod => mod.PreUpdateDusts());
        private static void DustPostfix() => Each(nameof(Mod.PostUpdateDusts), mod => mod.PostUpdateDusts());
        private static void InvasionPrefix() => Each(nameof(Mod.PreUpdateInvasions), mod => mod.PreUpdateInvasions());
        private static void InvasionPostfix() => Each(nameof(Mod.PostUpdateInvasions), mod => mod.PostUpdateInvasions());

        private static void UpdateUIPostfix(GameTime gameTime)
        {
            if (Main.gameMenu) return;
            Each(nameof(Mod.UpdateUI), mod => mod.UpdateUI(gameTime));
        }

        private static void PostInputPostfix() => Each(nameof(Mod.PostUpdateInput), mod => mod.PostUpdateInput());

        private static void MetricsResetPostfix(SceneMetrics __instance)
        {
            if (Main.gameMenu || __instance != Main.PlayerSceneMetrics) return;
            Each(nameof(Mod.ResetNearbyTileEffects), mod => mod.ResetNearbyTileEffects());
        }

        private static void MetricsCountsPostfix(SceneMetrics __instance)
        {
            if (Main.gameMenu || __instance != Main.PlayerSceneMetrics) return;
            var counts = __instance._tileCounts;
            Each(nameof(Mod.TileCountsAvailable), mod => mod.TileCountsAvailable(counts));
        }

        private static void QuitPrefix() => Unload();

        private static string WorldDataFile()
        {
            var path = Main.worldPathName;
            return string.IsNullOrEmpty(path) ? null : path + ".wld.bl.json";
        }
    }
}
