using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria.GameContent.UI.ResourceSets;

namespace Pylon.ModLoader;

/// <summary>
/// 资源覆盖层的静态查询与整链分派：数量、按号反查，以及把一次绘制上下文/显示快照
/// 跑过全部覆盖层（返回值逐个取与，异常按覆盖层逐个吞掉并上报）。
/// </summary>
public static class ResourceOverlayLoader
{
    public static int OverlayCount => ResourceOverlayTable.Count;

    public static ModResourceOverlay? GetOverlay(int type) =>
        type >= 0 && type < ResourceOverlayTable.Count ? ResourceOverlayTable.At(type) : null;

    public static bool PreDrawResource(ResourceOverlayDrawContext context)
    {
        var result = true;
        foreach (var overlay in ResourceOverlayTable.All)
        {
            try
            {
                result &= overlay.PreDrawResource(context);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[resourceoverlay] " + overlay.Name + ".PreDrawResource：" + Safe.Describe(e));
            }
        }
        return result;
    }

    public static void PostDrawResource(ResourceOverlayDrawContext context)
    {
        foreach (var overlay in ResourceOverlayTable.All)
        {
            try
            {
                overlay.PostDrawResource(context);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[resourceoverlay] " + overlay.Name + ".PostDrawResource：" + Safe.Describe(e));
            }
        }
    }

    public static bool PreDrawResourceDisplay(PlayerStatsSnapshot snapshot, IPlayerResourcesDisplaySet displaySet,
                                              bool drawingLife, ref Color textColor, out bool drawText)
    {
        var result = true;
        drawText = true;
        foreach (var overlay in ResourceOverlayTable.All)
        {
            try
            {
                result &= overlay.PreDrawResourceDisplay(snapshot, displaySet, drawingLife, ref textColor, out bool draw);
                drawText &= draw;
            }
            catch (Exception e)
            {
                ContentErrors.Report("[resourceoverlay] " + overlay.Name + ".PreDrawResourceDisplay：" + Safe.Describe(e));
            }
        }
        return result;
    }

    public static void PostDrawResourceDisplay(PlayerStatsSnapshot snapshot, IPlayerResourcesDisplaySet displaySet,
                                               bool drawingLife, Color textColor, bool drawText)
    {
        foreach (var overlay in ResourceOverlayTable.All)
        {
            try
            {
                overlay.PostDrawResourceDisplay(snapshot, displaySet, drawingLife, textColor, drawText);
            }
            catch (Exception e)
            {
                ContentErrors.Report("[resourceoverlay] " + overlay.Name + ".PostDrawResourceDisplay：" + Safe.Describe(e));
            }
        }
    }

    public static bool DisplayHoverText(PlayerStatsSnapshot snapshot, IPlayerResourcesDisplaySet displaySet, bool drawingLife)
    {
        var result = true;
        foreach (var overlay in ResourceOverlayTable.All)
        {
            result &= overlay.DisplayHoverText(snapshot, displaySet, drawingLife);
        }
        return result;
    }
}
