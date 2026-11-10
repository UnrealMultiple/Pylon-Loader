using Pylon.ModLoader;
using PipelineTest.Content;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 管线：PlayerDrawInfo —— 六个绘制信息时机的真机验证。
/// 缓存条数在 TransformDrawData 里读；缩放改完读 Main.GameViewMatrix.Zoom。
/// </summary>
internal static class PlayerDrawInfoTests
{
    public static void Register()
    {
        PT.Group("playerdrawinfo", Phase.Ready, RunReady);
        PT.Group("playerdrawinfo-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("PDINFO-R1", c =>
        {
            // 六个时机的入口都在基类上（作者按 tModLoader 形状覆写即可）。
            var type = typeof(ModPlayer);
            string[] names =
            [
                nameof(ModPlayer.DrawEffects), nameof(ModPlayer.ModifyDrawInfo),
                nameof(ModPlayer.TransformDrawData), nameof(ModPlayer.DrawPlayer),
                nameof(ModPlayer.ModifyScreenPosition),
            ];

            foreach (var name in names)
                c.NotNull(type.GetMethod(name), $"ModPlayer.{name}");
        });

        PT.Case("PDINFO-R2", c =>
        {
            var type = typeof(ModPlayer);
            c.NotNull(type.GetMethod("ModifyZoom", [typeof(float).MakeByRefType()]),
                "ModifyZoom(ref float)");
        });
    }

    private static void RunWorld()
    {
        var me = Main.player[Main.myPlayer];
        if (me is null || !me.active)
        {
            PT.SkipCase("PDINFO-W1", "玩家还没就绪");
            return;
        }

        DrawInfoProbe.Reset();

        // W1：五个"每帧一次"的时机都被调到了。
        PT.Later("PDINFO-W1", 64, c =>
        {
            c.True(DrawInfoProbe.EffectsCalls > 0, $"DrawEffects 被调（{DrawInfoProbe.EffectsCalls} 次）");
            c.True(DrawInfoProbe.InfoCalls > 0, $"ModifyDrawInfo 被调（{DrawInfoProbe.InfoCalls} 次）");
            c.True(DrawInfoProbe.TransformCalls > 0, $"TransformDrawData 被调（{DrawInfoProbe.TransformCalls} 次）");
            c.True(DrawInfoProbe.DrawPlayerCalls > 0, $"DrawPlayer 被调（{DrawInfoProbe.DrawPlayerCalls} 次）");
            c.True(DrawInfoProbe.ScreenPosCalls > 0, $"ModifyScreenPosition 被调（{DrawInfoProbe.ScreenPosCalls} 次）");
        });

        // W2：DrawPlayer 拿到的是真相机；TransformDrawData 看到的是已经填过的缓存。
        PT.Later("PDINFO-W2", 67, c =>
        {
            c.True(DrawInfoProbe.CameraSeen, "DrawPlayer 拿到非空 Camera");
            c.True(DrawInfoProbe.TransformCache > 0,
                $"TransformDrawData 时缓存已有数据（{DrawInfoProbe.TransformCache} 条）");
        });

        // W3：DrawEffects 改的红倍率落到了颜色字段上（衣服/裤子红分量变 0），随后打开染色。
        PT.Later("PDINFO-W3", 70, c =>
        {
            c.True(DrawInfoProbe.ShirtRed >= 0 && DrawInfoProbe.PantsRed >= 0,
                $"ModifyDrawInfo 读到颜色（衣 {DrawInfoProbe.ShirtRed} / 裤 {DrawInfoProbe.PantsRed}）");
            DrawInfoProbe.TintOn = true;
        });

        PT.Later("PDINFO-W4", 74, c =>
        {
            c.Eq(DrawInfoProbe.ShirtRed, 0, "DrawEffects 把 r 置 0 后衣服红分量为 0");
            c.Eq(DrawInfoProbe.PantsRed, 0, "裤子红分量同样为 0");
            DrawInfoProbe.TintOn = false;
        });

        // W5：染色关掉后颜色回正常（探针不再影响画面）。
        PT.Later("PDINFO-W5", 78, c =>
        {
            c.True(DrawInfoProbe.ShirtRed > 0, $"关掉染色后衣服红分量回正常（{DrawInfoProbe.ShirtRed}）");
            DrawInfoProbe.ZoomOn = true;
        });

        // W6：ModifyZoom(ref float) 生效，两轴都按同一倍率写回。
        PT.Later("PDINFO-W6", 82, c =>
        {
            c.True(DrawInfoProbe.ZoomScalarCalls > 0, $"ModifyZoom 被调（{DrawInfoProbe.ZoomScalarCalls} 次）");

            var zoom = Main.GameViewMatrix.Zoom;
            c.True(System.Math.Abs(zoom.X - DrawInfoProbe.ZoomValue) < 0.01f &&
                   System.Math.Abs(zoom.Y - DrawInfoProbe.ZoomValue) < 0.01f,
                $"缩放生效（Zoom={zoom.X:0.00},{zoom.Y:0.00} 期望 {DrawInfoProbe.ZoomValue}）");

            DrawInfoProbe.ZoomInvalidOn = true;
        });

        // W8：非法缩放（0）被丢掉：这一帧按原版的倍率算，不会把镜头压成 0。
        PT.Later("PDINFO-W8", 86, c =>
        {
            var zoom = Main.GameViewMatrix.Zoom;
            c.True(zoom.X > 0f && System.Math.Abs(zoom.X - 1f) < 0.01f,
                $"非法缩放被丢掉、回落到原版倍率（Zoom.X={zoom.X:0.00}）");

            DrawInfoProbe.ZoomInvalidOn = false;
            DrawInfoProbe.ZoomOn = false;
        });

        // W9：探针收工后缩放回到 1（不影响后续组）。
        PT.Later("PDINFO-W9", 90, c =>
        {
            var zoom = Main.GameViewMatrix.Zoom;
            c.True(System.Math.Abs(zoom.X - 1f) < 0.01f, $"关掉后缩放松手（Zoom.X={zoom.X:0.00}）");
            DrawInfoProbe.Reset();
        });
    }
}