using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Tables;
using Pylon.ModLoader.Textures;
using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>云管线：addCloud 之后按权重换编号接管生成；覆写 Draw 的云整段交给作者。</summary>
internal sealed class CloudPipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static CloudPipeline Instance { get; } = new();

    public string Name => "cloud";


    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Cloud.addCloud += AddCloud;
        On.Terraria.Cloud.RollRareCloud += RollRareCloud;
        On.Terraria.GameContent.Drawing.NextHorizonRenderer.DrawCloud += DrawCloud;

        Log.Info("[cloud] 已挂（普通云 / 稀有云按权重换编号；覆写 Draw 的云逐云接管绘制）");
        return true;
    }

    public void Refresh() => CloudTable.Assign();

    internal void Build()
    {
        var total = CloudTable.Total;
        if (total > CloudTable.Base && total > 0)
        {
            var clouds = Terraria.GameContent.TextureAssets.Cloud;
            if (ArrayGrow.Grow(ref clouds, total))
                Terraria.GameContent.TextureAssets.Cloud = clouds;
        }

        foreach (var cloud in ModContent.GetContent<ModCloud>())
        {
            if (!cloud.Registered) continue;
            cloud.SetStaticDefaults();
        }

        GrowMasks(total);

        if (CloudTable.Count > 0) Log.Info($"[cloud] 安装：{CloudTable.Count} 朵云");
    }

    private static void GrowMasks(int total)
    {
        try
        {
            var masks = global::TextureMaskManager.CloudMasks;
            Log.Info($"[cloud] CloudMasks 表：{(masks is null ? "null（游戏还没建）" : $"长 {masks.Length}")}，目标 {total}");
            if (masks is null) return;
            if (ArrayGrow.Grow(ref masks, total))
                global::TextureMaskManager.CloudMasks = masks;

            foreach (var cloud in ModContent.GetContent<ModCloud>())
            {
                if (!cloud.Registered) continue;
                if (masks is null || (uint)cloud.Type >= (uint)masks.Length) continue;
                var existing = masks[cloud.Type];
                if (existing is not null && existing.Pointer != IntPtr.Zero) continue;

                var mask = new global::TextureMask();
                if (cloud.Texture?.Value is { } texture)
                {
                    mask.srcWidth = texture.Width;
                    mask.srcHeight = texture.Height;
                }
                masks[cloud.Type] = mask;
            }
        }
        catch (Exception e)
        {
            Log.Warn("[cloud] 遮罩表扩容失败（游戏侧没有 TextureMaskManager 时画云走老路径）：" + Safe.Describe(e));
        }
    }

    private static int RollRareCloud(On.Terraria.Cloud.orig_RollRareCloud orig)
    {
        var vanilla = orig();

        if (CloudTable.Count == 0) return vanilla;

        var type = CloudTable.Roll(CloudTable.Base, true);
        if (type == 0) return vanilla;

        if (CloudTable.Of(type)?.Texture?.Value is not { } texture) return vanilla;

        return type;
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

        if (picked.Texture?.Value is not { } texture) return;

        cloud.type = type;
        cloud.width = (int)(texture.Width * cloud.scale);
        cloud.height = (int)(texture.Height * cloud.scale);

        try
        {
            picked.OnSpawn(cloud);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[cloud] {picked.Name}.OnSpawn 异常：{Safe.Describe(e)}");
        }
    }

    private static readonly List<DrawData> DrawCache = new();

    private static void DrawCloud(On.Terraria.GameContent.Drawing.NextHorizonRenderer.orig_DrawCloud orig,
        Terraria.GameContent.Drawing.NextHorizonRenderer self, float globalCloudAlpha, Cloud theCloud, int cloudPass, float cY)
    {
        var mod = CloudTable.Of(theCloud.type);
        if (mod is null || !mod.HookEnabled(HookId.Draw))
        {
            orig(self, globalCloudAlpha, theCloud, cloudPass, cY);
            return;
        }

        bool vanilla;
        try
        {
            vanilla = AuthorVanilla(mod, globalCloudAlpha, theCloud, cloudPass, cY);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"[cloud] {mod.Name}.Draw 异常：{Safe.Describe(e)}");
            vanilla = true;
        }

        if (vanilla) orig(self, globalCloudAlpha, theCloud, cloudPass, cY);
    }

    private static bool AuthorVanilla(ModCloud mod, float globalCloudAlpha, Cloud cloud, int cloudPass, float cY)
    {
        var texture = Terraria.GameContent.TextureAssets.Cloud[cloud.type].Value;
        if (texture is null) return true;

        var size = new Vector2(texture.Width, texture.Height);
        var color = cloud.cloudColor(Main.ColorOfTheSkies);
        if (cloudPass == 1)
        {
            float red = cloud.scale * 0.8f, green = (cloud.scale + 1f) / 2f * 0.9f;
            color.R = (byte)(color.R * red);
            color.G = (byte)(color.G * green);
        }
        if (Main.atmo < 1f) color *= Main.atmo;
        color *= globalCloudAlpha;

        var position = new Vector2(cloud.position.X, cY) + size / 2f;
        var sourceRect = new Rectangle(0, 0, texture.Width, texture.Height);
        var origin = size / 2f;
        var rotation = cloud.rotation;
        var effect = cloud.spriteDir;
        var scale = new Vector2(cloud.scale);

        var original = new DrawData(texture, position, sourceRect, color, rotation, origin, scale, effect, 0);
        var drawData = new DrawData(texture, position, sourceRect, color, rotation, origin, scale, effect, 0);

        DrawCache.Clear();
        bool keep = mod.Draw(DrawCache, cloud, IndexOf(cloud), ref drawData);
        foreach (var extra in DrawCache) Blit(extra);
        if (!keep) return false;
        if (Same(drawData, original)) return true;
        Blit(drawData);
        return false;
    }

    private static bool Same(DrawData a, DrawData b) =>
        a.texture.Pointer == b.texture.Pointer && a.position == b.position && a.sourceRect.Equals(b.sourceRect) &&
        a.color.Equals(b.color) && a.rotation == b.rotation && a.origin == b.origin &&
        a.scale == b.scale && a.effect == b.effect;

    private static void Blit(DrawData d)
    {
        var position = d.position;
        var sourceRect = d.sourceRect;
        var color = d.color;
        Main.cloudBatch.Draw(d.texture, ref position, ref sourceRect, ref color, d.rotation, d.origin, d.scale, d.effect);
    }

    private static int IndexOf(Cloud cloud)
    {
        var clouds = Main.cloud;
        for (var i = 0; i < clouds.Length; i++)
        {
            if (clouds[i] is { } candidate && candidate.Pointer == cloud.Pointer) return i;
        }
        return -1;
    }
}
