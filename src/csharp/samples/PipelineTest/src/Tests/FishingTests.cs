using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using PipelineTest.Content;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Tests;

/// <summary>
/// fishing-w：通过生成 DLL 的真实 GiveItemToPlayer / QuickSpawnItem / DrawProj_FishingLine 入口。
/// 渲染入口必须在 PostDrawInterface 的已 Begin SpriteBatch 内执行；不能从世界更新中调用。
/// 自动断言只覆盖入口、回调顺序、ref 串接、作用域恢复；线的实际像素另列视觉观察。
/// </summary>
internal static class FishingTests
{
    private static Action _draw;
    public static void Register() => PT.Group("fishing-w", Phase.World, Run);

    internal static void Draw()
    {
        var pending = _draw;
        _draw = null;
        pending?.Invoke();
    }

    private static void Run()
    {
        var player = Main.LocalPlayer;
        if (player is null || !player.active || Main.netMode != 0)
        {
            PT.SkipCase("FISH-*", "需要已进入世界的单机本地玩家");
            return;
        }
        var index = -1;
        for (var i = 0; i < Main.projectile.Length; i++)
            if (Main.projectile[i] is { active: false }) { index = i; break; }
        if (index < 0)
        {
            PT.SkipCase("FISH-*", "没有可隔离的弹幕槽位");
            return;
        }

        var inventory = player.inventory;
        var savedInventory = new Item[inventory.Length];
        for (var i = 0; i < inventory.Length; i++) savedInventory[i] = inventory[i];
        var oldProjectile = Main.projectile[index];
        var projectile = new Projectile();
        projectile.SetDefaults(ModContent.ProjectileType<TestFishingBobber>());
        projectile.whoAmI = index;
        projectile.owner = player.whoAmI;
        projectile.position = player.Center + new Vector2(160f, 50f);
        projectile.velocity = Vector2.Zero;
        // 绘制管线通过 Main.projectile[whoAmI] 反查 ModProjectile，需保持活动且只占用隔离槽位。
        projectile.active = true;
        Main.projectile[index] = projectile;
        FishingProbe.Active = true;
        FishingProbe.PlayerIndex = player.whoAmI;
        FishingProbe.ProjectileIndex = index;

        void ClearInventory()
        {
            for (var i = 0; i < inventory.Length; i++)
            {
                var item = new Item();
                item.TurnToAir();
                inventory[i] = item;
            }
        }
        int FishCount()
        {
            var count = 0;
            for (var i = 0; i < inventory.Length; i++)
                if (inventory[i].type == ItemID.Bass) count += inventory[i].stack;
            return count;
        }
        void Restore()
        {
            _draw = null;
            FishingProbe.Active = false;
            FishingProbe.Reset();
            FishingProbe.PlayerIndex = FishingProbe.ProjectileIndex = -1;
            for (var i = 0; i < savedInventory.Length; i++) inventory[i] = savedInventory[i];
            projectile.active = false;
            Main.projectile[index] = oldProjectile;
        }

        var waitingForDraw = false;
        try
        {
            PT.Case("FISH-C1", c =>
            {
                ClearInventory();
                FishingProbe.Reset();
                projectile.AI_061_FishingBobber_GiveItemToPlayer(player, ItemID.Bass);
                c.Eq(FishingProbe.CaughtCalls, 1, "真实 Give 入口中 Item 重载转调只派发一次");
                c.NotNull(FishingProbe.LastFish, "作者收到真正发放的鱼 Item");
                c.Eq(FishCount(), 3, "ModifyCaughtFish 的 stack=3 在入包前生效");
            });
            PT.Case("FISH-C2", c =>
            {
                ClearInventory();
                FishingProbe.Reset();
                var item = new Item();
                item.SetDefaults(ItemID.Bass, null);
                player.QuickSpawnItem(null, item);
                var second = new Item();
                second.SetDefaults(ItemID.Bass, null);
                player.QuickSpawnItem(null, second, GetItemSettings.InventoryEntityToPlayerInventorySettings);
                player.QuickSpawnItem(null, ItemID.Bass, 1);
                c.Eq(FishingProbe.CaughtCalls, 0, "Give 作用域外两个 Item 重载和 int 重载均不派发 Caught");
                c.Eq(FishCount(), 3, "作用域外原版三次发放仍生效");
            });
            PT.Case("FISH-C3", c =>
            {
                ClearInventory();
                FishingProbe.Reset();
                FishingProbe.ThrowCaught = true;
                c.NoThrow(() => projectile.AI_061_FishingBobber_GiveItemToPlayer(player, ItemID.Bass),
                    "作者异常不阻止原版发鱼");
                c.Eq(FishingProbe.CaughtCalls, 1, "异常路径也只派发一次");
                c.True(FishCount() > 0, "异常后鱼仍实际入包");
                FishingProbe.ThrowCaught = false;
                var before = FishingProbe.CaughtCalls;
                var item = new Item();
                item.SetDefaults(ItemID.Bass, null);
                player.QuickSpawnItem(null, item);
                c.Eq(FishingProbe.CaughtCalls, before, "Give finally 清理后普通发物品不误命中捕鱼");
            });

            ClearInventory();
            var pole = new Item();
            pole.SetDefaults(ModContent.ItemType<TestFishingPole>(), null);
            var selectedSlot = player.selectedItem;
            inventory[selectedSlot] = pole;
            _draw = () =>
            {
                try
                {
                    PT.Case("FISH-L1", c =>
                    {
                        FishingProbe.Reset();
                        FishingProbe.AlterLine = true;
                        var x = 0f;
                        var y = 0f;
                        Main.DrawProj_FishingLine(projectile, player, ref x, ref y, player.MountedCenter);
                        c.Eq(FishingProbe.ProjectileCalls, 1, "真实 DrawProj_FishingLine 调用 ModProjectile");
                        c.Eq(FishingProbe.ItemCalls, 1, "真实 DrawProj_FishingLine 调用持有 ModItem");
                        c.Eq(FishingProbe.Order, "PI", "鱼线回调顺序为弹幕→物品");
                        c.Eq(FishingProbe.ItemInputOffset.X, FishingProbe.ProjectileOffset.X, "物品收到弹幕的 ref offset.X");
                        c.Eq(FishingProbe.ItemInputOffset.Y, FishingProbe.ProjectileOffset.Y, "物品收到弹幕的 ref offset.Y");
                        c.Eq(FishingProbe.ItemInputColor.PackedValue, FishingProbe.ProjectileColor.PackedValue,
                            "物品收到弹幕写回的 ref Color");
                        PT.Info("视觉观察（非像素断言）：本帧探针线应为绿色，起点相对 mountedCenter 偏移 (+42,-28)；" +
                            "自动断言只证明真实入口、PI 顺序及参数串接，颜色经过原版染色函数后的像素需另行截图核对。");
                    });
                    PT.Case("FISH-L2", c =>
                    {
                        var input = new Color(12, 34, 56, 255);
                        var output = Main.TryApplyingPlayerStringColor(0, input);
                        c.Eq(output.PackedValue, input.PackedValue,
                            "Draw 返回后 TryApplyingPlayerStringColor(0,input) 恢复普通输入色");
                    });
                }
                finally { Restore(); }
            };
            waitingForDraw = true;
            // 页面离开/没有渲染回调时仍释放快照，不让背包被测试状态占用。
            PT.Later("FISH-CLEANUP", 120, c =>
            {
                if (_draw is not null)
                {
                    Restore();
                    c.True(false, "120 帧内未抵达 PostDrawInterface，鱼线入口尚未执行；已恢复状态");
                }
                else c.True(!FishingProbe.Active, "捕鱼探针与临时玩家/弹幕状态已恢复");
            });
        }
        finally
        {
            if (!waitingForDraw) Restore();
        }
    }
}
