using System;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent;

namespace PipelineTest.Tests;

/// <summary>
/// Hook 引擎回归：返回值是结构体（<see cref="ShoppingSettings"/> = il2cpp 值类型代理，16 字节）。
/// 原生 ABI：x0 = PriceAdjustment，x1 = HappinessReport（都在 x0/x1，不走 sret）。
/// </summary>
internal static class ShopHookTests
{
    private static int _npc = -1;
    private static bool _hooked;

    public static void Register() => PT.Group("shophook", Phase.World, Run);

    private static void Run()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("SHOPHOOK-1", "玩家还没就绪");
            return;
        }

        PT.Case("SHOPHOOK-1", c =>
        {
            c.NoThrow(() =>
            {
                if (_hooked) return;
                On.Terraria.GameContent.ShopHelper.GetShoppingSettings += Hook;
                _hooked = true;
            }, "挂 GetShoppingSettings 钩子");
        });

        PT.Case("SHOPHOOK-2", c =>
        {
            if (_npc < 0)
            {
                _npc = NPC.NewNPC(player.GetProjectileSource_Misc(0),
                    (int)player.Center.X + 40, (int)player.Center.Y - 20,
                    ModContent.NpcType<Content.TestTownNpc>(), 0, 0f, 0f, 0f, 0f, 0);
            }

            c.True(_npc >= 0 && Main.npc[_npc].active, $"测试商人 index={_npc}");
            if (_npc < 0) return;

            ShoppingSettings settings = null!;
            c.NoThrow(() => settings = Main.ShopHelper.GetShoppingSettings(player, Main.npc[_npc]),
                "GetShoppingSettings（结构体返回）");
            c.NotNull(settings, "返回的 ShoppingSettings");
            if (settings is null) return;

            c.True(Math.Abs(settings.PriceAdjustment - 0.42f) < 0.001f,
                $"handler 改过的 PriceAdjustment={settings.PriceAdjustment}");
            c.Eq(settings.HappinessReport, "hook-report", "HappinessReport");

            // 游戏原生调用路径：Player.SetTalkNPC 内部直接 bl 到 GetShoppingSettings（不经过托管代理）。
            // 返回值仍走同一个入口桩，这里验证 x0/x1 的返回在原生调用者那边也对。
            c.NoThrow(() => player.SetTalkNPC(_npc), "Player.SetTalkNPC（原生调用钩子）");
            var native = player.currentShoppingSettings;
            c.True(Math.Abs(native.PriceAdjustment - 0.42f) < 0.001f,
                $"原生路径 PriceAdjustment={native.PriceAdjustment}");
            c.Eq(native.HappinessReport, "hook-report", "原生路径 HappinessReport");

            player.talkNPC = -1;
            Main.npcChatText = "";
            On.Terraria.GameContent.ShopHelper.GetShoppingSettings -= Hook;
            _hooked = false;
        });
    }

    /// <summary>原样调用 orig，改两个字段：验证 x0/x1 的返回与引用字段都能过桥。</summary>
    private static ShoppingSettings Hook(On.Terraria.GameContent.ShopHelper.orig_GetShoppingSettings orig,
                                         ShopHelper self, Player player, NPC npc)
    {
        var result = orig(self, player, npc);
        result.PriceAdjustment = 0.42f;
        result.HappinessReport = "hook-report";
        return result;
    }
}
