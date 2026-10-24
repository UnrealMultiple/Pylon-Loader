using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace PipelineTest.Tests;

/// <summary>物品绘制四钩（B2 验证脚手架）：背包末格放一枚 + 世界掉落一枚，延迟断言四钩计数、skip 帧、ref 写回。</summary>
internal static class ItemDrawTests
{
    private static bool _dropped;

    public static void Register() => PT.Group("itemdraw-w", Phase.World, RunWorld);

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("IDW-*", "玩家未就绪");
            return;
        }

        var type = ModContent.ItemType<Content.TestDrawHooksItem>();
        if (type <= 0)
        {
            PT.SkipCase("IDW-*", "测试物品未注册");
            return;
        }

        Content.TestDrawHooksItem.Reset();

        // 库存格（热键栏末格）：库存绘制钩每帧触发。逐步打点定位 NRE（验证脚手架诊断）。
        PT.Info($"itemdraw: type={type}");
        var inv = player.inventory;
        var held = new Item();
        held.SetDefaults(type, null);
        held.stack = 1;
        PT.Info("itemdraw: SetDefaults 完成");
        inv[8] = held;
        PT.Info("itemdraw: inv[8] 完成");

        PT.Case("IDW-1", c =>
        {
            c.True(inv[8] is { } && inv[8].type == type, $"背包第 9 格 type={inv[8]?.type}");

            // 世界掉落一枚（玩家右侧 40px）：世界绘制钩每帧触发；异常就地记录不连坐。
            // source 用玩家来源：本构建 interop 对 null IEntitySource 参数封送会 NRE，仓库内生成调用统一走 GetProjectileSource_Misc。
            var dropped = -1;
            var dropSource = player.GetProjectileSource_Misc(0);
            c.NoThrow(() => dropped = Item.NewItem(dropSource, player.Center + new Vector2(40f, 0f), type, 1),
                "掉落生成");
            c.True(dropped >= 0, $"掉落 index={dropped}");
            _dropped = dropped >= 0;
        });

        PT.Later("IDW-2", 120, c =>
        {
            c.True(Content.TestDrawHooksItem.CtPreInv > 0, $"PreDrawInInventory 已触发 {Content.TestDrawHooksItem.CtPreInv} 次");
            c.True(Content.TestDrawHooksItem.CtPostInv > 0, $"PostDrawInInventory 已触发 {Content.TestDrawHooksItem.CtPostInv} 次");
        });

        PT.Later("IDW-3", 150, c =>
        {
            if (!_dropped)
            {
                c.Skip("掉落未生成（见 IDW-1），世界钩断言跳过");
                return;
            }
            var pre = Content.TestDrawHooksItem.CtPreWorld;
            var post = Content.TestDrawHooksItem.CtPostWorld;
            c.True(pre > 3, $"PreDrawInWorld 已触发 {pre} 次");
            c.Eq(Content.TestDrawHooksItem.SkipCount, 3, "skip 帧数（前 3 帧否决）");
            c.True(post >= pre, $"PostDrawInWorld {post} ≥ Pre {pre}（否决也跑）");
            c.True(Content.TestDrawHooksItem.RefWrites > 0, $"rotation ref 已写回 {Content.TestDrawHooksItem.RefWrites} 次");
        });
    }
}
