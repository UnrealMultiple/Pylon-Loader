namespace Pylon.ModLoader;

/// <summary>
/// 玩家绘制层的表：给了 <see cref="ModPlayerDrawLayer.Position"/> 的层按位置插进原版链（见
/// <see cref="PlayerDrawLayerChain"/>），其余按 <c>DrawOverPlayer</c> 分两组、组内按注册序。
/// </summary>
internal static class PlayerDrawLayerTable
{
    private static readonly List<ModPlayerDrawLayer> Behind = [];
    private static readonly List<ModPlayerDrawLayer> Front = [];
    private static readonly List<ModPlayerDrawLayer> Positioned = [];

    private static int _chained;

    internal static int Count => Behind.Count + Front.Count + Positioned.Count;

    internal static IReadOnlyList<ModPlayerDrawLayer> BehindLayers => Behind;

    internal static IReadOnlyList<ModPlayerDrawLayer> FrontLayers => Front;

    /// <summary>插进链里的层数（0 = 没人用位置，链上不多一次分派）。</summary>
    internal static int ChainedCount => _chained;

    /// <summary>收表；幂等。</summary>
    internal static void Rebuild()
    {
        Behind.Clear();
        Front.Clear();
        Positioned.Clear();

        foreach (var layer in ModContent.GetContent<ModPlayerDrawLayer>())
        {
            LayerPosition? position;
            try
            {
                position = layer.Position;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playerdrawlayer] {layer.Name}.Position 异常：{Safe.Describe(e)}");
                position = null;
            }

            if (position is not null)
            {
                Positioned.Add(layer);
                continue;
            }

            bool over;
            try
            {
                over = layer.DrawOverPlayer;
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playerdrawlayer] {layer.Name}.DrawOverPlayer 异常：{Safe.Describe(e)}");
                over = false;
            }

            (over ? Front : Behind).Add(layer);
        }

        _chained = PlayerDrawLayerChain.Build(Positioned);
    }
}
