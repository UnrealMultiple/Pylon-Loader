#nullable enable
using System;
using System.Collections.Generic;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 29：ResourceOverlay（生命/魔力条覆盖层）。
/// World：进世界后血条/魔力星绘制时 Pre/PostResource 与整条 Pre/PostResourceDisplay 计数应 > 0。
/// </summary>
internal static class ResourceOverlayTests
{
    public static void Register()
    {
        PT.Group("resourceoverlay", Phase.Ready, RunReady);
        PT.Group("resourceoverlay-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("RO-R1", c =>
        {
            var overlay = typeof(ModResourceOverlay);
            c.NotNull(overlay.GetMethod("PreDrawResource"), "ModResourceOverlay.PreDrawResource");
            c.NotNull(overlay.GetMethod("PostDrawResource"), "ModResourceOverlay.PostDrawResource");
            c.NotNull(overlay.GetMethod("PreDrawResourceDisplay"), "ModResourceOverlay.PreDrawResourceDisplay");
            c.NotNull(overlay.GetMethod("PostDrawResourceDisplay"), "ModResourceOverlay.PostDrawResourceDisplay");
            c.NotNull(overlay.GetMethod("DisplayHoverText"), "ModResourceOverlay.DisplayHoverText");

            var pre = overlay.GetMethod("PreDrawResource");
            c.True(pre!.GetParameters()[0].ParameterType == typeof(ResourceOverlayDrawContext),
                   $"PreDrawResource 参数类型 {pre.GetParameters()[0].ParameterType.Name}");

            var context = typeof(ResourceOverlayDrawContext);
            c.NotNull(context.GetField("snapshot"), "context.snapshot");
            c.NotNull(context.GetField("resourceNumber"), "context.resourceNumber");
            c.NotNull(context.GetField("texture"), "context.texture");
            c.NotNull(context.GetField("position"), "context.position");
            c.NotNull(context.GetField("source"), "context.source");
            c.NotNull(context.GetField("color"), "context.color");
            c.NotNull(context.GetField("rotation"), "context.rotation");
            c.NotNull(context.GetField("origin"), "context.origin");
            c.NotNull(context.GetField("scale"), "context.scale");
            c.NotNull(context.GetField("effects"), "context.effects");
            c.NotNull(context.GetProperty("DisplaySet"), "context.DisplaySet");
            c.NotNull(context.GetProperty("SpriteBatch"), "context.SpriteBatch");
            c.NotNull(context.GetMethod("Draw"), "context.Draw");
        });
    }

    private static void RunWorld()
    {
        Content.TestLifeOverlay.Reset();
        Content.TestManaOverlay.Reset();

        // 进世界后血条/魔力星开始画；给 120 帧让绘制链跑起来再断言。
        PT.Later("RO-W1", 120, c =>
        {
            c.True(Content.TestLifeOverlay.CtPreDrawResource > 0,
                   $"生命 PreDrawResource 派发（{Content.TestLifeOverlay.CtPreDrawResource} 次，最后序号 {Content.TestLifeOverlay.LastResourceNumber}）");
            c.True(Content.TestLifeOverlay.CtPostDrawResource > 0,
                   $"生命 PostDrawResource 派发（{Content.TestLifeOverlay.CtPostDrawResource} 次）");
            c.True(Content.TestManaOverlay.CtPreDrawResource > 0,
                   $"魔力 PreDrawResource 派发（{Content.TestManaOverlay.CtPreDrawResource} 次，最后序号 {Content.TestManaOverlay.LastResourceNumber}）");
            c.True(Content.TestManaOverlay.CtPostDrawResource > 0,
                   $"魔力 PostDrawResource 派发（{Content.TestManaOverlay.CtPostDrawResource} 次）");
            c.True(Content.TestLifeOverlay.CtPreDrawResourceDisplay > 0,
                   $"PreDrawResourceDisplay 派发（{Content.TestLifeOverlay.CtPreDrawResourceDisplay} 次）");
            c.True(Content.TestLifeOverlay.CtPostDrawResourceDisplay > 0,
                   $"PostDrawResourceDisplay 派发（{Content.TestLifeOverlay.CtPostDrawResourceDisplay} 次）");
            PT.Info("肉眼验收：心应变成 ClassicLifeOverlay（金色），魔力星应变成 ClassicManaOverlay");
        });

        // RO-W2..W4：真机切换三种资源条样式（Classic / FancyClassic / HorizontalBars），
        // 每种样式都要求生命与魔力的 Pre/PostResource 派发——验证管线给的 context.texture 与样式对得上。
        var sets = Main.PlayerResourcesSets;
        var original = Main.ActivePlayerResourcesSet;

        // 样式名以游戏字典为准（不同版本键名不同），不硬编码。
        var styles = new List<string>();
        if (sets is not null)
        {
            foreach (var pair in sets) styles.Add(pair.Key);
        }
        PT.Info("资源条样式：" + string.Join(",", styles));

        void Step(int index)
        {
            if (index >= styles.Count)
            {
                Main.ActivePlayerResourcesSet = original;
                return;
            }

            var name = styles[index];

            PT.Later($"RO-W{2 + index}S", 40, _ =>
            {
                Content.TestLifeOverlay.Reset();
                Content.TestManaOverlay.Reset();
                Main.ActivePlayerResourcesSet = sets[name];
                PT.Info($"已切换资源条样式：{name}");

                PT.Later($"RO-W{2 + index}", 90, c =>
                {
                    c.True(Content.TestLifeOverlay.CtPreDrawResource > 0 && Content.TestLifeOverlay.CtPostDrawResource > 0,
                           $"{name} 生命 Pre/Post（{Content.TestLifeOverlay.CtPreDrawResource}/{Content.TestLifeOverlay.CtPostDrawResource}）");
                    c.True(Content.TestManaOverlay.CtPreDrawResource > 0 && Content.TestManaOverlay.CtPostDrawResource > 0,
                           $"{name} 魔力 Pre/Post（{Content.TestManaOverlay.CtPreDrawResource}/{Content.TestManaOverlay.CtPostDrawResource}）");
                    Step(index + 1);
                });
            });
        }

        Step(0);
    }
}