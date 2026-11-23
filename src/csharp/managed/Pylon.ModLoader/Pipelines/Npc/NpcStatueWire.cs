using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Globals;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>王座/后座雕像的电线信号：识别雕像格 → SkipWire → CheckMech 冷却 → 收集候选并随机传送到雕像脚下 → 派发 OnGoToStatue。</summary>
internal static class NpcStatueWire
{
    private const int StatueTile = 105;
    private const int KingStatue = 40;
    private const int QueenStatue = 41;

    private static readonly int[] KingStatueTypes =
        [17, 19, 22, 38, 54, 107, 108, 142, 160, 207, 209, 227, 228, 229, 368, 369, 550, 441, 588];

    private static readonly int[] QueenStatueTypes = [18, 20, 124, 178, 208, 353, 633, 663];

    internal static bool TryTrigger(int i, int j)
    {
        var tile = Main.tile[i, j];
        if (!tile.active() || tile.type != StatueTile) return false;

        int left = i - tile.frameX % 36 / 18;
        int top = j - tile.frameY % 54 / 18;
        int style = tile.frameX / 36 + tile.frameY / 54 % 3 * 55;
        if (style != KingStatue && style != QueenStatue) return false;

        for (var x = 0; x < 2; x++)
            for (var y = 0; y < 3; y++)
                Wiring.SkipWire(left + x, top + y);

        if (!Wiring.CheckMech(left, top, 300)) return true;

        bool king = style == KingStatue;
        var vanilla = king ? KingStatueTypes : QueenStatueTypes;

        List<int> candidates = [];
        for (var n = 0; n < 200; n++)
        {
            var npc = Main.npc[n];
            if (npc is null || !npc.active) continue;

            bool? decision = null;
            var mod = NpcPipeline.Instance.InstanceOf(npc);
            if (mod is not null && mod.HookEnabled(HookId.CanGoToStatue))
            {
                try { decision = mod.CanGoToStatue(king); }
                catch (Exception e) { Report(mod, e); }
            }
            foreach (var global in GlobalNpcRegistry.For(npc, HookId.CanGoToStatue))
            {
                try
                {
                    if (global.CanGoToStatue(npc, king) is bool canGo)
                        decision = canGo;
                }
                catch (Exception e) { Report(global, e); }
            }
            if (decision ?? Array.IndexOf(vanilla, npc.type) >= 0) candidates.Add(n);
        }

        if (candidates.Count == 0) return true;

        var chosen = Main.npc[candidates[Main.rand.Next(candidates.Count)]];
        int x0 = left * 16 + 16, y0 = (top + 3) * 16;
        chosen.Teleport(new Vector2(x0 - chosen.width / 2, y0 - chosen.height - 1), 14);

        var owner = NpcPipeline.Instance.InstanceOf(chosen);
        if (owner is not null && owner.HookEnabled(HookId.OnGoToStatue))
        {
            try { owner.OnGoToStatue(king); }
            catch (Exception e) { Report(owner, e); }
        }
        foreach (var global in GlobalNpcRegistry.For(chosen, HookId.OnGoToStatue))
        {
            try { global.OnGoToStatue(chosen, king); }
            catch (Exception e) { Report(global, e); }
        }

        return true;
    }

    private static readonly HashSet<string> _reported = [];

    private static void Report(ModNpc mod, Exception exception)
    {
        if (!_reported.Add(mod.Type + "/" + exception.GetType().Name)) return;
        ContentErrors.Line($"[statue] {mod.Name} 雕像钩子异常：{Safe.Dump(exception)}");
    }

    private static void Report(GlobalNpc global, Exception exception)
    {
        if (!_reported.Add(global.Name + "/" + exception.GetType().Name)) return;
        ContentErrors.Line($"[statue] {global.Name} 雕像钩子异常：{Safe.Dump(exception)}");
    }
}
