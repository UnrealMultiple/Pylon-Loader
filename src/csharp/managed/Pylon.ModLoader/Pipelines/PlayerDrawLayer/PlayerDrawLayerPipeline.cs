using Il2CppMicrosoft.Xna.Framework;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Terraria;
using Terraria.Graphics;
using Terraria.Graphics.Renderers;

namespace Pylon.ModLoader;

/// <summary>
/// 玩家绘制层管线：没写 <see cref="ModPlayerDrawLayer.Position"/> 的层在 <c>LegacyPlayerRenderer.DrawPlayer</c> 前后各派一次；
/// 写了位置的层由 <see cref="PlayerDrawLayerChain"/> 插进原版逐层绘制链。
/// </summary>
internal sealed class PlayerDrawLayerPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static PlayerDrawLayerPipeline Instance { get; } = new();

    public string Name => "playerdrawlayer";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Graphics.Renderers.LegacyPlayerRenderer.DrawPlayer += DrawPlayer;

        Log.Info("[playerdrawlayer] 已挂（玩家身后 / 身前两层 + 原版逐层链上的定位层）");
        return true;
    }

    public void Refresh() => PlayerDrawLayerTable.Rebuild();

    /// <summary>安装期一次：收表 + 静态默认值。</summary>
    internal void Build()
    {
        PlayerDrawLayerTable.Rebuild();

        foreach (var layer in PlayerDrawLayerTable.BehindLayers) layer.SetStaticDefaults();
        foreach (var layer in PlayerDrawLayerTable.FrontLayers) layer.SetStaticDefaults();

        if (PlayerDrawLayerTable.Count > 0)
        {
            Log.Info($"[playerdrawlayer] 安装：{PlayerDrawLayerTable.Count} 个层，其中 {PlayerDrawLayerTable.ChainedCount} 个插进原版链");
        }
    }

    // ===================== 绘制 =====================

    private static void DrawPlayer(On.Terraria.Graphics.Renderers.LegacyPlayerRenderer.orig_DrawPlayer orig,
                                   LegacyPlayerRenderer self, Camera camera, Player drawPlayer, Vector2 position,
                                   float rotation, Vector2 rotationOrigin, float shadow, float scale,
                                   Il2CppStructArray<Vector2> positionalOffsets)
    {
        if (PlayerDrawLayerTable.Count == 0)
        {
            orig(self, camera, drawPlayer, position, rotation, rotationOrigin, shadow, scale, positionalOffsets);
            return;
        }

        var behind = PlayerDrawLayerTable.BehindLayers;
        for (var index = 0; index < behind.Count; index++)
            Draw(behind[index], drawPlayer, position, rotation, scale);

        orig(self, camera, drawPlayer, position, rotation, rotationOrigin, shadow, scale, positionalOffsets);

        var front = PlayerDrawLayerTable.FrontLayers;
        for (var index = 0; index < front.Count; index++)
            Draw(front[index], drawPlayer, position, rotation, scale);
    }

    private static void Draw(ModPlayerDrawLayer layer, Player player, Vector2 position, float rotation, float scale)
    {
        if (player is null) return;

        var spriteBatch = Main.spriteBatch;
        if (spriteBatch is null) return;

        try
        {
            layer.Draw(spriteBatch, player, position, rotation, scale);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[playerdrawlayer] {layer.Name}.Draw 异常：{Safe.Describe(e)}");
        }
    }
}
