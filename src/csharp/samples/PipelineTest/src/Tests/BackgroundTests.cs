#nullable enable
using System;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 30：Background（表层背景风格）。
/// World：让场景效果声明该风格 → Main.bgStyle 变成 mod 槽位 → DrawBackground 走我们这层，
/// ChooseCloseTexture / PreDrawCloseBackground / ModifyFarFades 都应被调。
/// </summary>
internal static class BackgroundTests
{
    public static void Register()
    {
        PT.Group("background", Phase.Ready, RunReady);
        PT.Group("background-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("BG-R1", c =>
        {
            var style = ModContent.GetInstance<Content.TestSurfaceBackgroundStyle>();
            c.NotNull(style, "TestSurfaceBackgroundStyle 实例");
            c.True(style!.Registered, "已注册");
            c.True(style.Slot >= 17, $"编号={style.Slot}（应在游戏 16 个之后）");
            c.True(BackgroundTextureLoader.TryGetBackgroundSlot(style.Mod, "assets/backgrounds/ExampleBiomeSurfaceFar", out var farSlot) &&
                   farSlot >= BackgroundTextureLoader.VanillaCount, $"远层背景槽位={farSlot}（应在原版槽位之后）");
        });

        PT.Case("BG-R2", c =>
        {
            var method = typeof(ModSurfaceBackgroundStyle).GetMethod("ChooseCloseTexture");
            c.NotNull(method, "ChooseCloseTexture");
            var parameters = method!.GetParameters();
            c.Eq(parameters.Length, 4, "参数个数");
            c.Eq(parameters[1].ParameterType, typeof(double).MakeByRefType(), "第二个参数类型");
        });
    }

    private static void RunWorld()
    {
        var style = ModContent.GetInstance<Content.TestSurfaceBackgroundStyle>();
        if (style is null)
        {
            PT.SkipCase("BG-W1", "背景风格没注册");
            return;
        }

        Content.TestSurfaceBackgroundStyle.Reset();

        // 让探针 A 赢下场景并声明背景风格。
        Content.TestSceneEffectProbe.AActive = true;
        Content.TestSceneEffectProbe.APriority = 100;
        Content.TestSceneEffectProbe.UseBackground = true;

        PT.Later("BG-W1", 150, c =>
        {
            c.Eq(Main.bgStyle, style.Slot, "Main.bgStyle 是 mod 槽位");
            c.True(Content.TestSurfaceBackgroundStyle.CtFar > 0,
                   $"ChooseFarTexture 被调（{Content.TestSurfaceBackgroundStyle.CtFar} 次）");
            c.True(Content.TestSurfaceBackgroundStyle.CtMiddle > 0,
                   $"ChooseMiddleTexture 被调（{Content.TestSurfaceBackgroundStyle.CtMiddle} 次）");
            c.True(Content.TestSurfaceBackgroundStyle.CtClose > 0,
                   $"ChooseCloseTexture 被调（{Content.TestSurfaceBackgroundStyle.CtClose} 次，parallax={Content.TestSurfaceBackgroundStyle.LastParallax}）");
            c.True(Content.TestSurfaceBackgroundStyle.CtPreDrawClose > 0,
                   $"PreDrawCloseBackground 被调（{Content.TestSurfaceBackgroundStyle.CtPreDrawClose} 次）");
            c.True(Content.TestSurfaceBackgroundStyle.CtFades > 0,
                   $"ModifyFarFades 被调且没崩（{Content.TestSurfaceBackgroundStyle.CtFades} 次）");
            PT.Info("肉眼验收：世界背景应变成 TestSurfaceClose 那张图（BG-W2 之前一直保持）");
        });

        // BG-W2：关掉场景效果，背景应回到原版（bgStyle 不再是 mod 槽位）。
        PT.Later("BG-W2", 420, c =>
        {
            Content.TestSceneEffectProbe.UseBackground = false;
            Content.TestSceneEffectProbe.AActive = false;

            PT.Later("BG-W2b", 90, c2 =>
            {
                c2.True(Main.bgStyle != style.Slot, $"关掉后 bgStyle={Main.bgStyle}（不再是 mod 槽位 {style.Slot}）");
                PT.Info("肉眼验收：背景应回到原版");
            });
        });
    }
}