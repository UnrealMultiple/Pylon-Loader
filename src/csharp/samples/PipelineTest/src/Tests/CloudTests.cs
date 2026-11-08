using System;
using Pylon.ModLoader;
using Pylon.ModLoader.Textures;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 7：Cloud —— 功能验证。发号在扩表期（Base 运行期捕获），抽签走 Cloud.addCloud/RollRareCloud 钩子。
/// 真实云生成在菜单/世界背景帧驱动；这里验证注册面 + 贴图进表 + 抽签权重函数。
/// </summary>
internal static class CloudTests
{
    public static void Register()
    {
        PT.Group("cloud", Phase.Ready, Run);
        PT.Group("cloud-w", Phase.World, RunWorld);
        InstallProbe();
    }

    // ===== 绘制探针（不改绘制，只统计游戏真的画了什么） =====

    private static int _draws;
    private static int _totalDraws;
    private static int _lastWidth, _lastHeight;

    private static void InstallProbe()
    {
        try
        {
            On.Terraria.GameContent.Drawing.NextHorizonRenderer.DrawCloud += Probe;
        }
        catch (Exception e)
        {
            PT.Info("云绘制探针挂钩失败：" + e.Message);
        }
    }

    private static void Probe(On.Terraria.GameContent.Drawing.NextHorizonRenderer.orig_DrawCloud orig,
                              Terraria.GameContent.Drawing.NextHorizonRenderer self,
                              float globalCloudAlpha, Cloud cloud, int cloudPass, float cY)
    {
        _totalDraws++;

        var normal = ModContent.GetInstance<Content.TestCloud>().Type;
        var rare = ModContent.GetInstance<Content.TestRareCloud>().Type;

        if (cloud is not null && (cloud.type == normal || cloud.type == rare))
        {
            _draws++;

            var table = Terraria.GameContent.TextureAssets.Cloud;
            if (table is not null && cloud.type < table.Length)
            {
                var texture = table[cloud.type].Value;
                _lastWidth = texture.Width;
                _lastHeight = texture.Height;
            }
        }

        orig(self, globalCloudAlpha, cloud, cloudPass, cY);
    }

    /// <summary>
    /// 肉眼验证：把天上现有的云全部换成 mod 云（高级云贴图，186x65，比原版云大一圈），
    /// 换法：type + width/height = 贴图尺寸 × cloud.scale。
    /// 再由绘制探针确认游戏画的就是这张 186x65 的图。
    /// </summary>
    private static void RunWorld()
    {
        // 用稀有云（ExampleAdvancedCloud 186x65）：尺寸大、形状明显，肉眼能直接分辨。
        var cloud = ModContent.GetInstance<Content.TestRareCloud>();

        PT.Case("CLOUD-W1", c =>
        {
            var table = Terraria.GameContent.TextureAssets.Cloud;
            c.NotNull(table, "TextureAssets.Cloud");
            if (table is null || cloud.Type >= table.Length) { c.True(false, "云贴图槽越界"); return; }

            var texture = table[cloud.Type].Value;
            var width = texture.Width;
            var height = texture.Height;

            var clouds = Main.cloud;
            c.NotNull(clouds, "Main.cloud");
            if (clouds is null) return;

            var count = Math.Min(200, clouds.Length);
            var replaced = 0;
            for (var i = 0; i < count; i++)
            {
                if (clouds[i] is not { active: true } item) continue;

                item.type = cloud.Type;
                item.width = (int)(width * item.scale);
                item.height = (int)(height * item.scale);
                replaced++;
            }

            PT.Info($"把天上 {replaced} 朵云全换成 mod 稀有云 type={cloud.Type}（贴图 {width}x{height}）");

            PT.Later("CLOUD-W2", 60, d =>
            {
                PT.Info($"绘制探针：60 帧内 DrawCloud 共 {_totalDraws} 次，其中 mod 云 {_draws} 次，最后贴图 {_lastWidth}x{_lastHeight}");
                d.True(_totalDraws > 0, $"60 帧内 DrawCloud 被调 {_totalDraws} 次");
                d.True(_draws > 0, $"其中 mod 云被绘制 {_draws} 次");
                d.Eq(_lastWidth, width, "绘制用的贴图宽");
                d.Eq(_lastHeight, height, "绘制用的贴图高");
            });

            c.True(replaced > 0, $"成功换了 {replaced} 朵云");
        });
    }

    private static void Run()
    {
        var cloud = ModContent.GetInstance<Content.TestCloud>();
        var rare = ModContent.GetInstance<Content.TestRareCloud>();

        PT.Case("CLOUD-R1", c =>
        {
            c.NotNull(cloud, "TestCloud 实例");
            c.NotNull(rare, "TestRareCloud 实例");
            c.True(cloud.Type > 0, $"Type={cloud.Type}（扩表期发号）");
            c.True(rare.Type > 0, $"Rare Type={rare.Type}");
            c.True(cloud.Type != rare.Type, "两朵云不同号");
        });

        // T1：贴图进 TextureAssets.Cloud（LoadTextures + CloudPipeline.Build 扩表写槽）
        PT.Case("CLOUD-T1", c =>
        {
            var table = Terraria.GameContent.TextureAssets.Cloud;
            var len = table?.Length ?? 0;
            c.True(len > cloud.Type, $"云贴图表长={len}（应 > {cloud.Type}）");
            if (len > cloud.Type)
            {
                var asset = table![cloud.Type];
                c.NotNull(asset, $"[{cloud.Type}] 槽位为空");
            }
        });

        // H1：抽签权重函数直接验证（SpawnChance=3 参与，返回 mod 号或 0）
        PT.Case("CLOUD-H1", c =>
        {
            c.Eq(cloud.SpawnChance(), 3, "SpawnChance 声明值");
            c.True(!cloud.RareCloud, "TestCloud.RareCloud=false");
            c.True(rare.RareCloud, "TestRareCloud.RareCloud=true");
        });

        // E1：CloudColor 钩子不崩（直接调作者回调）
        PT.Case("CLOUD-E1", c =>
        {
            c.NoThrow(() =>
            {
                var vanilla = new Il2CppMicrosoft.Xna.Framework.Graphics.Color(200, 200, 255, 255);
                var dyed = cloud.CloudColor(null!, vanilla);
                c.True(dyed.R == 255 && dyed.G == 100, $"CloudColor 染色 {dyed}（期望 R=255 G=100）");
            }, "CloudColor 调用");
        });
    }
}