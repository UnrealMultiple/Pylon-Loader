using Terraria;
using Terraria.IO;
using Terraria.Map;

namespace Pylon.ModLoader;

/// <summary>
/// 系统的地图时机与头数据：PreDrawMapIconOverlay 里隐藏的层在本次绘制跳过、绘制完恢复，
/// PostDrawFullscreenMap 改写的鼠标文本写回原版；世界头旁车文件按 "<c>Mod.Uid/类型名</c>" 键读回。
/// </summary>
internal static class SystemMapHooks
{
    private static bool _installed;
    private static readonly HashSet<nint> HiddenLayers = [];

    internal static bool IsHidden(nint layer) => HiddenLayers.Contains(layer);

    internal static void SetHidden(nint layer, bool hidden)
    {
        if (hidden) HiddenLayers.Add(layer);
        else HiddenLayers.Remove(layer);
    }

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;

        if (SystemLoader.With(HookId.PreDrawMapIconOverlay).Length > 0 ||
            SystemLoader.With(HookId.PostDrawFullscreenMap).Length > 0)
            On.Terraria.Map.MapIconOverlay.Draw += DrawMapIcons;
    }

    private static void DrawMapIcons(On.Terraria.Map.MapIconOverlay.orig_Draw orig, MapIconOverlay self,
                                     Il2CppMicrosoft.Xna.Framework.Vector2 mapPosition,
                                     Il2CppMicrosoft.Xna.Framework.Vector2 mapOffset,
                                     Il2CppSystem.Nullable<Il2CppMicrosoft.Xna.Framework.Rectangle> clippingRect,
                                     float mapScale, float drawScale, int alpha, ref string text)
    {
        if (Main.gameMenu)
        {
            orig(self, mapPosition, mapOffset, clippingRect, mapScale, drawScale, alpha, ref text);
            return;
        }

        var list = self._layers;
        var layers = new List<IMapLayer>();
        var pre = SystemLoader.With(HookId.PreDrawMapIconOverlay).Length > 0;
        int firstHidden = -1;
        if (pre && list is { Count: > 0 })
        {
            HiddenLayers.Clear();
            for (var i = 0; i < list.Count; i++) layers.Add(list[i]);
            var context = new MapOverlayDrawContext(mapPosition, mapOffset, clippingRect, mapScale, drawScale, alpha / 255f);
            SystemLoader.PreDrawMapIconOverlay(layers, context);
            for (var i = 0; i < layers.Count && firstHidden < 0; i++)
            {
                if (HiddenLayers.Contains(layers[i].Pointer)) firstHidden = i;
            }
        }

        if (firstHidden < 0)
        {
            orig(self, mapPosition, mapOffset, clippingRect, mapScale, drawScale, alpha, ref text);
        }
        else
        {
            for (var i = list.Count - 1; i >= firstHidden; i--) list.RemoveAt(i);
            for (var i = firstHidden; i < layers.Count; i++)
            {
                if (HiddenLayers.Contains(layers[i].Pointer)) continue;
                list.Add(layers[i]);
            }
            try
            {
                orig(self, mapPosition, mapOffset, clippingRect, mapScale, drawScale, alpha, ref text);
            }
            finally
            {
                for (var i = list.Count - 1; i >= firstHidden; i--) list.RemoveAt(i);
                for (var i = firstHidden; i < layers.Count; i++) list.Add(layers[i]);
            }
        }

        if (SystemLoader.With(HookId.PostDrawFullscreenMap).Length == 0 || !Main.mapFullscreen) return;
        var mouseText = text ?? "";
        var before = mouseText;
        SystemLoader.PostDrawFullscreenMap(ref mouseText);
        if (mouseText != before) text = mouseText;
    }

    internal static bool TryGetHeaderData(WorldFileData world, ModSystem system, out TagCompound data)
    {
        data = new TagCompound();
        if (world is null || world.Pointer == nint.Zero || world.IsCloudSave ||
            world.Path is not { Length: > 0 } path) return false;

        var headers = WorldSidecar.Read(path).GetCompound(WorldSidecar.HeadersSection);
        if (!headers.TryGetValue(SystemLoader.KeyOf(system), out var value)) return false;
        data = TagCompound.From(value);
        return true;
    }
}
