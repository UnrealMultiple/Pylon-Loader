using System;
using System.Collections.Generic;
using System.IO;
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

            Install(mod.Logger);
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

        // ===================== 装 hook（On.* 订阅） =====================

        private static void Install(ModLogger log)
        {
            if (_installed) return;
            _installed = true;

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

        // ===================== 世界生命周期 =====================

        private static void ClearWorldPostfix(On.Terraria.WorldGen.orig_clearWorld orig)
        {
            orig();
            Each(nameof(Mod.ClearWorld), mod => mod.ClearWorld());
            if (Main.netMode == 1) Each(nameof(Mod.OnWorldLoad), mod => mod.OnWorldLoad());
        }

        private static void LoadWorld(On.Terraria.IO.WorldFile.orig_LoadWorld orig, bool canCheckFileState)
        {
            var player = Main.ActivePlayerFileData;
            var world = Main.ActiveWorldFileData;
            if (!Any(nameof(Mod.CanWorldBePlayed), mod => mod.CanWorldBePlayed(player, world)))
            {
                orig(canCheckFileState);
            }
            else
            {
                Safe.Once("mod.world.rejected", "世界被 Mod 的 CanWorldBePlayed 拒绝了这次进入", true);
                return;   // 被拒：跳过原版加载
            }


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

        private static void SaveWorldPostfix(On.Terraria.IO.WorldFile.orig_InternalSaveWorld orig,
                                             bool useCloudSaving, bool resetTime)
        {
            orig(useCloudSaving, resetTime);
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

        private static void PreSaveAndQuitPrefix(On.Terraria.WorldGen.orig_SaveAndQuit orig)
        {
            Each(nameof(Mod.PreSaveAndQuit), mod => mod.PreSaveAndQuit());
            orig();
        }

        private static void WorldUnloadPostfix(On.Terraria.WorldGen.orig_SaveAndQuitCallBack orig,
                                               Il2CppSystem.Object threadContext)
        {
            orig(threadContext);
            Each(nameof(Mod.OnWorldUnload), mod => mod.OnWorldUnload());
        }

        private static void UpdateWorld(On.Terraria.WorldGen.orig_UpdateWorld orig)
        {
            Each(nameof(Mod.PreUpdateWorld), mod => mod.PreUpdateWorld());
            orig();
            Each(nameof(Mod.PostUpdateWorld), mod => mod.PostUpdateWorld());
        }

        private static void UpdateTime(On.Terraria.Main.orig_UpdateTime orig)
        {
            Each(nameof(Mod.PreUpdateTime), mod => mod.PreUpdateTime());
            orig();
            Each(nameof(Mod.PostUpdateTime), mod => mod.PostUpdateTime());
        }

        private static void PreEntitiesPostEverything(On.Terraria.Main.orig_DoUpdateInWorld orig, Main self)
        {
            Each(nameof(Mod.PreUpdateEntities), mod => mod.PreUpdateEntities());
            orig(self);
            Each(nameof(Mod.PostUpdateEverything), mod => mod.PostUpdateEverything());
        }

        private static void UpdateDust(On.Terraria.Dust.orig_UpdateDust orig)
        {
            Each(nameof(Mod.PreUpdateDusts), mod => mod.PreUpdateDusts());
            orig();
            Each(nameof(Mod.PostUpdateDusts), mod => mod.PostUpdateDusts());
        }

        private static void UpdateInvasions(On.Terraria.Main.orig_UpdateInvasion orig)
        {
            Each(nameof(Mod.PreUpdateInvasions), mod => mod.PreUpdateInvasions());
            orig();
            Each(nameof(Mod.PostUpdateInvasions), mod => mod.PostUpdateInvasions());
        }

        private static void UpdateUI(On.Terraria.Main.orig_UpdateUIStates orig, GameTime gameTime)
        {
            orig(gameTime);
            if (Main.gameMenu) return;
            Each(nameof(Mod.UpdateUI), mod => mod.UpdateUI(gameTime));
        }

        private static void PostInput(On.Terraria.Main.orig_DoUpdate_HandleInput orig, Main self)
        {
            orig(self);
            Each(nameof(Mod.PostUpdateInput), mod => mod.PostUpdateInput());
        }

        private static void MetricsReset(On.Terraria.SceneMetrics.orig_Reset orig, SceneMetrics self)
        {
            orig(self);
            if (Main.gameMenu || self != Main.PlayerSceneMetrics) return;
            Each(nameof(Mod.ResetNearbyTileEffects), mod => mod.ResetNearbyTileEffects());
        }

        private static void MetricsCounts(On.Terraria.SceneMetrics.orig_AggregateTileCounts orig, SceneMetrics self)
        {
            orig(self);
            if (Main.gameMenu || self != Main.PlayerSceneMetrics) return;
            var counts = self._tileCounts;
            Each(nameof(Mod.TileCountsAvailable), mod => mod.TileCountsAvailable(counts));
        }

        private static void Quit(On.Terraria.Main.orig_QuitGame orig, Main self)
        {
            Unload();
            orig(self);
        }

        private static string WorldDataFile()
        {
            var path = Main.worldPathName;
            return string.IsNullOrEmpty(path) ? null : path + ".wld.bl.json";
        }
    }
}