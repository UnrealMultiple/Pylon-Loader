using Pylon.ModLoader.Content;
using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.GameContent.Generation;
using Terraria.ID;
using Terraria.IO;
using Terraria.WorldBuilding;

namespace Pylon.ModLoader.Hooks;
/// <summary>把作者任务修改写回原生列表,托管步骤仍由游戏的执行器依次调用。</summary>
internal static class ModWorldGenHooks
{
    private static bool _installed;
    [ThreadStatic] private static Context? _current;
    private sealed record Context(nint Generator, double Weight);
    private readonly record struct PassState(GenPass Pass, string Name, double Weight, bool Enabled);

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Terraria.WorldBuilding.WorldGenerator.GenerateWorld += Generate;
        On.Terraria.WorldBuilding.WorldGenerator.RunPass += RunPass;
        On.Terraria.WorldGen.initializeHardMode += Hardmode;
        On.Terraria.GameContent.Generation.PassLegacy.ApplyPass += ApplyPass;
    }

    private static bool Generate(On.Terraria.WorldBuilding.WorldGenerator.orig_GenerateWorld orig, WorldGenerator self)
    {
        var sysPre = SystemLoader.With(HookId.PreWorldGen);
        var sysEdit = SystemLoader.With(HookId.ModifyWorldGenTasks);
        var sysPost = SystemLoader.With(HookId.PostWorldGen);
        var protectChests = ModContent.GetContent<ModTile>().Count > 0;
        if (sysPre.Length == 0 && sysEdit.Length == 0 && sysPost.Length == 0 && !protectChests) return orig(self);

        for (var i = 0; i < sysPre.Length; i++)
        {
            var system = sysPre[i];
            Safe.Run($"{system.FullKey}.PreWorldGen", system.PreWorldGen);
        }
        if (sysEdit.Length == 0 && !protectChests)
        {
            var completed = orig(self);
            if (completed) Post(sysPost);
            return completed;
        }

        var native = self._passes;
        var tasks = new List<GenPass>(native.Count);
        for (var i = 0; i < native.Count; i++) tasks.Add(WorldGenPass.Retain(native[i]));
        Edit(tasks, sysEdit, false);
        if (protectChests) KeepModChests(tasks);
        var weight = WeightOf(tasks);
        native.Clear();
        foreach (var pass in tasks) native.Add(pass);
        var outer = _current;
        _current = new Context(self.Pointer, weight);
        try
        {
            var completed = orig(self);
            if (completed) Post(sysPost);
            return completed;
        }
        finally
        {
            _current = outer;
            GC.KeepAlive(tasks);
        }
    }

    private readonly record struct ChestTile(int X, int Y, ushort Type);

    private static bool IsBasicChest(ushort type)
    {
        var table = TileID.Sets.BasicChest;
        return table is not null && type < table.Length && table[type];
    }

    private static void KeepModChests(List<GenPass> tasks)
    {
        var index = tasks.FindIndex(static pass => pass.Name == "Tile Cleanup");
        if (index < 0 || WorldGenPass.IsManaged(tasks[index])) return;
        var cleanup = tasks[index];
        var replacement = WorldGenPass.Create(cleanup.Name, (progress, configuration) =>
        {
            var chests = new List<ChestTile>();
            var native = Main.chest;
            if (native is not null)
            {
                for (var i = 0; i < native.Length; i++)
                {
                    var chest = native[i];
                    if (chest is null) continue;
                    var tile = Main.tile.Safe(chest.x, chest.y);
                    if (tile.active() && tile.type >= TileID.Count && IsBasicChest(tile.type))
                        chests.Add(new ChestTile(chest.x, chest.y, tile.type));
                }
            }
            try { cleanup.Apply(progress, configuration); }
            finally
            {
                foreach (var chest in chests)
                {
                    for (var x = chest.X; x < chest.X + 2; x++)
                    {
                        for (var y = chest.Y; y < chest.Y + 2; y++)
                        {
                            if (x < 0 || x >= Main.maxTilesX || y < 0 || y >= Main.maxTilesY) continue;
                            var tile = Main.tile.Safe(x, y);
                            if (tile.active() && IsBasicChest(tile.type)) tile.type = chest.Type;
                        }
                    }
                }
            }
        }, cleanup.Weight);
        if (!cleanup.Enabled) replacement.Disable();
        tasks[index] = replacement;
    }

    private static void Post(ModSystem[] systems)
    {
        for (var i = 0; i < systems.Length; i++)
        {
            var system = systems[i];
            Safe.Run($"{system.FullKey}.PostWorldGen", system.PostWorldGen);
        }
    }

    private static void Edit(List<GenPass> tasks, ModSystem[] systems, bool hardmode)
    {
        for (var s = 0; s < systems.Length; s++)
        {
            var system = systems[s];
            var previous = tasks.Select(static p => new PassState(p, p.Name, p.Weight, p.Enabled)).ToArray();
            try
            {
                var totalWeight = WeightOf(tasks);
                if (hardmode) system.ModifyHardmodeTasks(tasks);
                else system.ModifyWorldGenTasks(tasks, ref totalWeight);
                WeightOf(tasks);
                for (var i = 0; i < tasks.Count; i++) tasks[i] = WorldGenPass.Retain(tasks[i]);
            }
            catch (Exception e)
            {
                tasks.Clear();
                foreach (var state in previous)
                {
                    state.Pass.Name = state.Name;
                    state.Pass.Weight = state.Weight;
                    if (state.Enabled) state.Pass.Enable(); else state.Pass.Disable();
                    tasks.Add(state.Pass);
                }
                ContentErrors.Report($"[worldgen] {system.FullKey}.{(hardmode ? nameof(ModSystem.ModifyHardmodeTasks) : nameof(ModSystem.ModifyWorldGenTasks))} 异常:{Safe.Describe(e)}");
            }
        }
    }

    internal static double WeightOf(List<GenPass> tasks)
    {
        double total = 0;
        foreach (var pass in tasks)
        {
            if (pass is null || !double.IsFinite(pass.Weight) || pass.Weight < 0)
                throw new ArgumentException("生成任务必须非空且权重为有限非负数");
            total += pass.Weight;
        }
        if (!double.IsFinite(total)) throw new ArgumentException("生成总权重必须有限");
        return total;
    }

    private static GenPassResult RunPass(On.Terraria.WorldBuilding.WorldGenerator.orig_RunPass orig,
                                         WorldGenerator self, GenPass pass)
    {
        if (_current is { } current && current.Generator == self.Pointer) self._progress.TotalWeight = current.Weight;
        return orig(self, pass);
    }

    private static void ApplyPass(On.Terraria.GameContent.Generation.PassLegacy.orig_ApplyPass orig,
                                   PassLegacy self, GenerationProgress progress, GameConfiguration configuration)
    {
        if (WorldGenPass.TryApply(self, progress, configuration)) return;
        if (self._method is null)
        {
            ContentErrors.Report($"[worldgen] {self.Name} 的托管回调已释放,请同时保存 WorldGenPass.Create 返回的步骤对象");
            return;
        }
        orig(self, progress, configuration);
    }

    private static void Hardmode(On.Terraria.WorldGen.orig_initializeHardMode orig)
    {
        var systems = SystemLoader.With(HookId.ModifyHardmodeTasks);
        if (systems.Length == 0) { orig(); return; }
        var conversion = WorldGenPass.Create("Hardmode Conversion", (_, _) => orig());
        var tasks = new List<GenPass> { conversion };
        Edit(tasks, systems, true);
        var progress = new GenerationProgress { TotalWeight = WeightOf(tasks) };
        try
        {
            foreach (var pass in tasks)
            {
                if (!pass.Enabled) continue;
                progress.Start(pass.Weight);
                try { pass.Apply(progress, null!); }
                finally { progress.End(); }
            }
        }
        finally { GC.KeepAlive(tasks); }
    }
}
