using Terraria;

namespace Pylon.ModLoader;

/// <summary>逐格周边效果：SceneMetrics.Scan 之后先扫 buff 区域（closer=false）再扫视觉区域（closer=true），逐格派发 ModTile → GlobalTile.NearbyEffects；只查 TileTable 密集数组，热路径不进字典。</summary>
internal static class TileEffectsHooks
{
    private static bool _installed;

    private static SceneMetrics? _scanning;

    internal static SceneMetrics ScanningMetrics => _scanning ?? Main.PlayerSceneMetrics;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;

        On.Terraria.SceneMetrics.Scan += Scan;
    }

    private static void Scan(On.Terraria.SceneMetrics.orig_Scan orig, SceneMetrics self,
                             SceneMetricsScanSettings settings)
    {
        orig(self, settings);

        var previous = _scanning;
        _scanning = self;
        try
        {
            var center = settings.BiomeScanCenterPositionInWorld;
            var x = (int)(center.X / 16f);
            var y = (int)(center.Y / 16f);
            var width = Main.buffScanAreaWidth;
            var height = Main.buffScanAreaHeight;
            NearArea(x - width / 2, y - height / 2, width, height, false);

            var visual = settings.VisualScanArea;
            if (visual is not null && visual.hasValue)
            {
                var area = visual.value;
                NearArea(area.X, area.Y, area.Width, area.Height, true);
            }
        }
        finally
        {
            _scanning = previous;
        }
    }

    private static void NearArea(int x, int y, int width, int height, bool closer)
    {
        if (Main.tile is null || width <= 0 || height <= 0) return;

        var lx = Math.Max(0, x);
        var ly = Math.Max(0, y);
        var hx = Math.Min(Main.maxTilesX - 1, x + width - 1);
        var hy = Math.Min(Main.maxTilesY - 1, y + height - 1);

        var globals = GlobalTileRegistry.For(HookId.NearbyEffects);
        var hasGlobals = globals.Count > 0;

        for (var i = lx; i <= hx; i++)
        for (var j = ly; j <= hy; j++)
        {
            var tile = Main.tile[i, j];
            if (!tile.active()) continue;

            var type = tile.type;
            var mod = TileTable.Of(type);
            var modHooked = mod is not null && mod.HookEnabled(HookId.NearbyEffects);

            if (!modHooked && !hasGlobals) continue;

            if (modHooked)
            {
                try
                {
                    mod!.NearbyEffects(i, j, closer);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[tile] {mod!.Name}.NearbyEffects 异常：{Safe.Describe(e)}");
                }
            }

            for (var k = 0; k < globals.Count; k++)
            {
                var global = globals[k];
                try
                {
                    global.NearbyEffects(i, j, type, closer);
                }
                catch (Exception e)
                {
                    ContentErrors.Report($"[tile] {global.FullKey}.NearbyEffects 异常：{Safe.Describe(e)}");
                }
            }
        }
    }
}
