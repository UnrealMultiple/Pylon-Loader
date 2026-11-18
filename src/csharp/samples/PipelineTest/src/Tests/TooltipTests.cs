#nullable enable
using System;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 27：Tooltip（物品提示）。
/// Ready：方法面（ModItem 五个 tooltip 钩子都在基类上）。
/// World：把光标模拟到热键栏第 1 格（测试物品所在）触发真实悬停链，
/// 断言 ModifyTooltips / PreDrawTooltip / PostDrawTooltipLine 被派发；开关控制改行与删行。
/// </summary>
internal static class TooltipTests
{
    public static void Register()
    {
        PT.Group("tooltip", Phase.Ready, RunReady);
        PT.Group("tooltip-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("TIP-R1", c =>
        {
            var type = typeof(ModItem);
            c.NotNull(type.GetMethod("ModifyTooltips"), "ModItem.ModifyTooltips");
            c.NotNull(type.GetMethod("PreDrawTooltip"), "ModItem.PreDrawTooltip");
            c.NotNull(type.GetMethod("PostDrawTooltip"), "ModItem.PostDrawTooltip");
            c.NotNull(type.GetMethod("PreDrawTooltipLine"), "ModItem.PreDrawTooltipLine");
            c.NotNull(type.GetMethod("PostDrawTooltipLine"), "ModItem.PostDrawTooltipLine");
        });

        // 管线的取行链（MouseText 之外的最短验证）：RebuildTooltip 走游戏自己的重建。
        PT.Case("TIP-R2", c =>
        {
            var probe = new Item();
            probe.SetDefaults(ModContent.ItemType<Content.TestItem>(), null);

            var texts = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray(64);
            var modifiers = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool>(64);
            var bad = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool>(64);
            int yoyo = -1, research = -1, materials = -1, count = 0, setBonus = -1, shared = -1;
            var color = new Il2CppMicrosoft.Xna.Framework.Graphics.Color(255, 255, 255, 255);

            Content.TestItem.MutateTooltip = true;
            try
            {
                Terraria.Main.MouseText_DrawItemTooltip_GetLinesInfo(probe, ref yoyo, ref research, ref materials,
                    probe.knockBack, ref count, texts, modifiers, bad, ref setBonus, ref color, ref shared);
            }
            finally { Content.TestItem.MutateTooltip = false; }

            c.True(count >= 2, $"GetLinesInfo 行数={count}（声明 1 + 钩子加 1）");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        var held = player?.inventory is { Length: > 0 } inv && inv[0] is { } slot && slot.type == ModContent.ItemType<Content.TestItem>();
        if (player is null || !player.active || !held)
        {
            PT.SkipCase("TIP-W1", "热键栏第 1 格不是测试物品");
            return;
        }

        Content.TestItem.ResetTooltipProbe();
        var mutatedModify = 0;

        PT.Later("TIP-W1", 10, c =>
        {
            Content.TestItem.MutateTooltip = true;
            PT.Info("请打开背包、按住第 1 格的测试物品约 1 分钟别松手（应看到 [管线改写]/[管线加行]，加行红色）");
        });

        PT.Later("TIP-W2", 1800, c =>
        {
            c.True(Content.TestItem.CtModifyTooltips > 0, $"ModifyTooltips 派发（{Content.TestItem.CtModifyTooltips} 次，行数 {Content.TestItem.LastLineCount}）");
            c.True(Content.TestItem.CtPreDrawTooltip > 0, $"PreDrawTooltip 派发（{Content.TestItem.CtPreDrawTooltip} 次）");
            c.True(Content.TestItem.CtPreDrawTooltipLine > 0, $"PreDrawTooltipLine 派发（{Content.TestItem.CtPreDrawTooltipLine} 次）");
            c.True(Content.TestItem.CtPostDrawTooltip > 0, $"PostDrawTooltip 派发（{Content.TestItem.CtPostDrawTooltip} 次）");
            c.True(Content.TestItem.CtPostDrawTooltipLine > 0, $"PostDrawTooltipLine 派发（{Content.TestItem.CtPostDrawTooltipLine} 次）");
            c.True(Content.TestItem.ProbeLineR > Content.TestItem.ProbeLineG && Content.TestItem.ProbeLineR > Content.TestItem.ProbeLineB,
                $"OverrideColor 应用到绘制色（R={Content.TestItem.ProbeLineR} G={Content.TestItem.ProbeLineG} B={Content.TestItem.ProbeLineB}）");
            PT.Info("肉眼验收：tooltip 应含 [管线改写]/[管线加行]，加行呈红色");
            Content.TestItem.MutateTooltip = false;
            Content.TestItem.MutatedLineCount = Content.TestItem.LastLineCount;
            mutatedModify = Content.TestItem.CtModifyTooltips;
            Content.TestItem.RemoveTooltipLine = true;
        });

        PT.Later("TIP-W3", 3000, c =>
        {
            c.True(Content.TestItem.CtModifyTooltips > mutatedModify,
                $"删行期间悬停仍在派发（Modify {mutatedModify} → {Content.TestItem.CtModifyTooltips}；按住别松手）");
            c.True(Content.TestItem.LastLineCount < Content.TestItem.MutatedLineCount, $"删行后行数 {Content.TestItem.LastLineCount} < 改行时 {Content.TestItem.MutatedLineCount}");
            Content.TestItem.RemoveTooltipLine = false;
        });

        var player2 = Main.player[Main.myPlayer];
        if (player2 is null || !player2.active)
        {
            PT.SkipCase("TIP-W4", "玩家还没就绪");
            return;
        }

        var armor = player2.armor;
        var savedArmor = new Item?[] { armor[0], armor[1], armor[2] };
        Item Make(int type)
        {
            var it = new Item();
            it.SetDefaults(type, null);
            return it;
        }

        armor[0] = Make(ModContent.ItemType<Content.TestHelmet>());
        armor[1] = Make(ModContent.ItemType<Content.TestBreastplate>());
        armor[2] = Make(ModContent.ItemType<Content.TestLeggings>());

        PT.Later("TIP-W4", 480, c =>
        {
            var me = Main.player[Main.myPlayer]!;
            c.True(Content.TestHelmet.SetUpdates > 0, $"套装回调已跑（{Content.TestHelmet.SetUpdates} 次）");
            c.Eq(me.setBonus, Content.TestHelmet.SetBonusText, "UpdateArmorSet 写了 setBonus");

            var helmet = me.armor[0];
            helmet.wornArmor = true;

            var texts = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray(64);
            var modifiers = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool>(64);
            var bad = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<bool>(64);
            int yoyo = -1, research = -1, materials = -1, count = 1, setBonus = -1, shared = -1;
            var color = new Il2CppMicrosoft.Xna.Framework.Graphics.Color(255, 255, 255, 255);

            Terraria.Main.MouseText_DrawItemTooltip_GetLinesInfo(helmet, ref yoyo, ref research, ref materials,
                helmet.knockBack, ref count, texts, modifiers, bad, ref setBonus, ref color, ref shared);

            var line = setBonus >= 0 && setBonus < count ? texts[setBonus] ?? "" : "";
            c.True(line.Contains(Content.TestHelmet.SetBonusText, StringComparison.Ordinal),
                $"穿着中的护甲显示套装行（行 {setBonus}/{count} '{line}'）");
            c.True(color.R == 50 && color.G == 205, $"套装行角色色为绿（R={color.R} G={color.G} B={color.B}）");
        });

        PT.Later("TIP-W5", 500, c =>
        {
            var me = Main.player[Main.myPlayer]!;
            var a = me.armor;
            if (savedArmor[0] is not null) a[0] = savedArmor[0]!;
            if (savedArmor[1] is not null) a[1] = savedArmor[1]!;
            if (savedArmor[2] is not null) a[2] = savedArmor[2]!;
        });
    }
}
