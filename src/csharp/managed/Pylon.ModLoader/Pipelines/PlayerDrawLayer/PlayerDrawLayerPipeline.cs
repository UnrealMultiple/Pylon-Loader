namespace Pylon.ModLoader;

/// <summary>
/// 玩家绘制层管线：作者层的层位、父子关系与可见性由 <see cref="PlayerDrawLayerLoader"/> 统一排程，
/// 真正落笔仍由原版逐层钩子（<see cref="HideLayerHooks"/>）在两次原版调用之间插进来。
/// </summary>
internal sealed class PlayerDrawLayerPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static PlayerDrawLayerPipeline Instance { get; } = new();

    public string Name => "playerdrawlayer";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        Log.Info("[playerdrawlayer] 已挂（层位排序 / 父子层 / 可见性 / 原版逐层槽位）");
        return true;
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
