namespace Pylon.ModLoader;

/// <summary>资源覆盖层的有序表：按注册序逐条分派；位集用来跳过"没覆写单颗绘制"的覆盖层。</summary>
internal static class ResourceOverlayTable
{
    private static readonly List<ModResourceOverlay> Overlays = [];
    private static readonly List<HookMask> Masks = [];

    internal static int Count => Overlays.Count;

    internal static IReadOnlyList<ModResourceOverlay> All => Overlays;

    internal static ModResourceOverlay At(int index) => Overlays[index];

    internal static HookMask MaskAt(int index) => Masks[index];

    /// <summary>有没有覆盖层要看单颗的心 / 魔力星。</summary>
    internal static bool AnyResourceDraw()
    {
        for (var i = 0; i < Masks.Count; i++)
        {
            var mask = Masks[i];
            if (mask.Has(HookId.PreDrawResource) || mask.Has(HookId.PostDrawResource)) return true;
        }
        return false;
    }

    /// <summary>收表；幂等。</summary>
    internal static void Rebuild()
    {
        Overlays.Clear();
        Masks.Clear();

        foreach (var overlay in ModContent.GetContent<ModResourceOverlay>())
        {
            var mask = HookScan.Of(overlay.GetType(), typeof(ModResourceOverlay));
            overlay.Plan = mask;
            Overlays.Add(overlay);
            Masks.Add(mask);
        }
    }
}
