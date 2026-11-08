using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 09：Buff —— 注册面 + 功能面。
/// Ready：发号 / 图标进 TextureAssets.Buff。
/// World：给玩家挂 60 秒测试增益，图标应出现在左上角；每帧 UpdatePlayer 生效（回血）。
/// </summary>
internal static class BuffTests
{
    public static void Register()
    {
        PT.Group("buff", Phase.Ready, RunReady);
        PT.Group("buff-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        var type = ModContent.BuffType<Content.TestBuff>();

        PT.Case("BUFF-R1", c =>
        {
            c.True(type >= BuffID.Count, $"type={type}（原版 {BuffID.Count}）");
            c.NotNull(ModContent.GetInstance<Content.TestBuff>(), "GetInstance");
        });

        PT.Case("BUFF-T1", c =>
        {
            var table = TextureAssets.Buff;
            var len = table?.Length ?? 0;
            c.True(len > type, $"buff 贴图表长={len}（应 > {type}）");
            if (len > type) c.NotNull(table![type], $"[{type}] 槽位");
        });
    }

    private static void RunWorld()
    {
        var type = ModContent.BuffType<Content.TestBuff>();
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("BUFF-W1", "玩家还没就绪");
            return;
        }

        PT.Case("BUFF-W1", c =>
        {
            var beforeApply = Content.TestBuff.ApplyCount;
            player.AddBuff(type, 3600, true);

            var index = player.FindBuffIndex(type);
            c.True(index >= 0, $"FindBuffIndex={index}（应 >= 0）");
            c.True(Content.TestBuff.ApplyCount > beforeApply,
                $"ApplyPlayer 未触发（{beforeApply} -> {Content.TestBuff.ApplyCount}）");
            c.True(index >= 0, $"buff 槽位={index}");
            PT.Info($"测试增益已挂上：type={type} 槽位={index} 剩余时间={(index >= 0 ? player.buffTime[index] : -1)} 秒牌");
        });
    }
}
