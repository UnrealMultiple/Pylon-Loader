using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 玩家绘制层管线：作者层的层位、父子关系与可见性由 <see cref="PlayerDrawLayerLoader"/> 统一排程，
/// 真正落笔仍由原版逐层钩子（<see cref="HideLayerHooks"/>）在两次原版调用之间插进来。
/// </summary>
internal sealed class PlayerDrawLayerPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static PlayerDrawLayerPipeline Instance { get; } = new();

    public string Name => "playerdrawlayer";

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>playerdrawlayer 管线的安装门：只有存在作者绘制层时才登记安装（原生逐层钩子归 playerdrawinfo 的 Anchors 门）。</summary>
    internal static class Gates
    {
        internal struct Layers : IGate
        {
            public static bool Wanted() => HasLayers();

            public static void Install() => Log.Info("[playerdrawlayer] 已挂（层位排序 / 父子层 / 可见性 / 原版逐层槽位）");
        }
    }

    public bool Install() => GateHub<PlayerDrawLayerPipeline>.Run();

    private static bool HasLayers()
    {
        foreach (var layer in ModContent.GetContent<PlayerDrawLayer>())
        {
            if (layer is not VanillaPlayerDrawLayer) return true;
        }

        return false;
    }

    public void Refresh() => PlayerDrawLayerLoader.Rebuild();

    /// <summary>安装期一次：收表、跑一次静态默认值、排程。</summary>
    internal void Build()
    {
        PlayerDrawLayerLoader.ResetModLayers();

        var layers = ModContent.GetContent<PlayerDrawLayer>();
        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            if (layer is VanillaPlayerDrawLayer) continue;

            try
            {
                layer.SetStaticDefaults();
            }
            catch (Exception e)
            {
                ContentErrors.Report($"[playerdrawlayer] {layer.Name}.SetStaticDefaults 异常：{Safe.Describe(e)}");
            }

            PlayerDrawLayerLoader.Add(layer);
        }

        PlayerDrawLayerLoader.Rebuild();

        if (PlayerDrawLayerLoader.ModCount > 0)
            Log.Info($"[playerdrawlayer] 安装：{PlayerDrawLayerLoader.ModCount} 个层");
    }
}
