using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 云管线：游戏自己生成云，我们在 <c>addCloud</c> 之后按权重把刚生成那朵的编号换成 mod 云。
/// 绘制不挂钩子——贴图写进扩长后的 <c>TextureAssets.Cloud</c>，原版绘制自动生效。
/// </summary>
internal sealed class CloudPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static CloudPipeline Instance { get; } = new();

    public string Name => "cloud";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Cloud.addCloud += AddCloud;
        On.Terraria.Cloud.RollRareCloud += RollRareCloud;
        On.Terraria.Cloud.cloudColor += CloudColor;

        Log.Info("[cloud] 已挂（普通云 / 稀有云都按权重换编号 + 逐云着色）；逐云绘制本版不接");
        return true;
    }

    public void Refresh() => CloudTable.Assign();

    /// <summary>安装期一次：扩表 + 静态默认值。要在写贴图（Surface 阶段）之前跑。</summary>
    internal void Build()
    {
        var total = CloudTable.Total;
        if (total > 0) new TypeTables.TableGrower(TypeTables.TableKind.Cloud, total).NextBatch(total, Log.Info);

        foreach (var cloud in ModContent.GetContent<ModCloud>())
        {
            if (!cloud.Registered) continue;
            cloud.SetStaticDefaults();
        }

        if (CloudTable.Count > 0) Log.Info($"[cloud] 安装：{CloudTable.Count} 朵云");
    }

    // ===================== 生成 =====================

    /// <summary>稀有一签：mod 稀有云也参与（原版那批稀有云仍是权重 1 的对手）。</summary>
    private static int RollRareCloud(On.Terraria.Cloud.orig_RollRareCloud orig)
    {
        var vanilla = orig();

        if (CloudTable.Count == 0) return vanilla;

        var type = CloudTable.Roll(CloudTable.Base, true);
        if (type == 0) return vanilla;

        // 没贴图就别换：换了就是一朵画不出来的云。
        if (CloudTable.Of(type)?.Texture?.Value is not { } texture) return vanilla;

        return type;
    }

    /// <summary>
    /// 逐云着色：游戏每帧按天色/高度算好原色再交给这朵云，作者可以乘自己的颜色。
    /// 只对 mod 云问（原版云不进管线）。
    /// </summary>
    private static Color CloudColor(On.Terraria.Cloud.orig_cloudColor orig, Cloud self, Color color)
    {
        var result = orig(self, color);

        if (CloudTable.Count == 0 || self is null) return result;

        var cloud = CloudTable.Of(self.type);
        if (cloud is null) return result;

        try
        {
            return cloud.CloudColor(self, result);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[cloud] {cloud.Name}.CloudColor 异常：{Safe.Describe(e)}");
            return result;
        }
    }

    private static void AddCloud(On.Terraria.Cloud.orig_addCloud orig)
    {
        orig();

        if (CloudTable.Count == 0) return;

        var index = Main.numClouds - 1;
        var clouds = Main.cloud;
        if (clouds is null || index < 0 || (uint)index >= (uint)clouds.Length) return;
        if (clouds[index] is not { } cloud) return;

        var type = CloudTable.Roll(CloudTable.Base, CloudTable.IsRare(cloud.type));
        if (type == 0) return;

        if (CloudTable.Of(type) is not { } picked) return;

        // 没贴图就别换编号：换了就是画不出来的一朵云。
        if (picked.Texture?.Value is not { } texture) return;

        cloud.type = type;
        cloud.width = texture.Width;
        cloud.height = texture.Height;

        try
        {
            picked.OnSpawn(cloud);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[cloud] {picked.Name}.OnSpawn 异常：{Safe.Describe(e)}");
        }
    }
}
