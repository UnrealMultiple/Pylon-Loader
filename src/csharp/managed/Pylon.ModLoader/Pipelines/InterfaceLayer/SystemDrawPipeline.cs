using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Pipelines;
/// <summary>系统绘制时机：方块与投影画完、整块界面画完。</summary>
internal sealed class SystemDrawPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static SystemDrawPipeline Instance { get; } = new();

    public string Name => "systemdraw";

    private static IModLog Log => LoaderHost.Current.Log;

    private static bool _tiles;
    private static bool _interface;

    /// <summary>systemdraw 管线的安装门：按 ModSystem 的绘制时机钩子分装。</summary>
    internal static class Gates
    {
        internal struct Core : IGate
        {
            public static bool Wanted() => true;

            public static void Install() => Log.Info("[systemdraw] 已就位（PostDrawTiles / PostDrawInterface 按 ModSystem 时机分装）");
        }

        internal struct Tiles : IGate
        {
            public static bool Wanted() => SystemTable.AnyPostDrawTiles;

            public static void Install() => On.Terraria.Main.DrawSuperSpecialProjectiles += DrawProjectiles;
        }

        internal struct Interface : IGate
        {
            public static bool Wanted() => SystemTable.AnyPostDrawInterface;

            public static void Install() => On.Terraria.Main.DrawInterface_33_MouseText += DrawMouseText;
        }
    }

    public bool Install() => GateHub<SystemDrawPipeline>.Run();

    public void Refresh()
    {
        InterfaceLayerPipeline.Reset();

        _tiles = SystemTable.AnyPostDrawTiles;
        _interface = SystemTable.AnyPostDrawInterface;
    }

    private static void DrawProjectiles(On.Terraria.Main.orig_DrawSuperSpecialProjectiles orig, Main self,
                                        Il2CppSystem.Collections.Generic.List<int> projCache, bool startSpriteBatch)
    {
        if (_tiles && !Main.gameMenu) SystemLoader.PostDrawTiles();

        orig(self, projCache, startSpriteBatch);
    }

    private static void DrawMouseText(On.Terraria.Main.orig_DrawInterface_33_MouseText orig, Main self)
    {
        if (_interface && !Main.gameMenu) SystemLoader.PostDrawInterface(Main.spriteBatch);

        orig(self);
    }
}
