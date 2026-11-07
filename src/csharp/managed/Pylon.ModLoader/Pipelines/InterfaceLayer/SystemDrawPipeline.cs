using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>系统绘制时机：方块与投影画完、整块界面画完。</summary>
internal sealed class SystemDrawPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static SystemDrawPipeline Instance { get; } = new();

    public string Name => "systemdraw";

    private static IModLog Log => LoaderHost.Current.Log;

    private static bool _tiles;
    private static bool _interface;

    public bool Install()
    {
        On.Terraria.Main.DrawSuperSpecialProjectiles += DrawProjectiles;
        On.Terraria.Main.DrawInterface_33_MouseText += DrawMouseText;

        Log.Info("[systemdraw] 已挂（PostDrawTiles / PostDrawInterface）");
        return true;
    }

    public void Refresh()
    {
        SystemTable.Rebuild();
        InterfaceLayerPipeline.Reset();

        _tiles = SystemTable.AnyPostDrawTiles;
        _interface = SystemTable.AnyPostDrawInterface;
    }

    private static void DrawProjectiles(On.Terraria.Main.orig_DrawSuperSpecialProjectiles orig, Main self,
                                        Il2CppSystem.Collections.Generic.List<int> projCache, bool startSpriteBatch)
    {
        if (_tiles && !Main.gameMenu) SystemTable.PostDrawTiles();

        orig(self, projCache, startSpriteBatch);
    }

    private static void DrawMouseText(On.Terraria.Main.orig_DrawInterface_33_MouseText orig, Main self)
    {
        if (_interface && !Main.gameMenu) SystemTable.PostDrawInterface(Main.spriteBatch);

        orig(self);
    }
}
