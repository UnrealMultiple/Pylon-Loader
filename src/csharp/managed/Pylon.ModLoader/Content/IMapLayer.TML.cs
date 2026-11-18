using Terraria.Map;

namespace Pylon.ModLoader;

/// <summary>地图层的作者面：基线 partial 成员 <c>Visible</c> 的扩展方法形状；隐藏的层在本次绘制里跳过、绘制完恢复。</summary>
public static class IMapLayerTml
{
    public static bool GetVisible(this IMapLayer layer) => !SystemMapHooks.IsHidden(layer.Pointer);

    public static void SetVisible(this IMapLayer layer, bool value) => SystemMapHooks.SetHidden(layer.Pointer, !value);
}
