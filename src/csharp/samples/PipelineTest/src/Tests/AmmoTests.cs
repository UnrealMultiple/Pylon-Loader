using PipelineTest.Content;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Tests;

internal static class AmmoTests
{
    public static void Register() => PT.Group("ammo-w", Phase.World, Run);

    private static void Run()
    {
        var player = Main.LocalPlayer;
        if (player is null || !player.active)
        {
            PT.SkipCase("AMMO-H1", "玩家还没就绪");
            return;
        }
        var inventory = player.inventory;
        var saved = new Item[inventory.Length];
        for (var i = 0; i < saved.Length; i++) saved[i] = inventory[i];
        var weapon = new Item();
        weapon.SetDefaults(ItemID.WoodenBow, null);
        var ammo = new Item();
        void Prepare(int count)
        {
            AmmoProbe.Reset();
            for (var i = 0; i < inventory.Length; i++)
            {
                var empty = new Item();
                empty.TurnToAir();
                inventory[i] = empty;
            }
            ammo.SetDefaults(ItemID.WoodenArrow, null);
            ammo.stack = count;
            inventory[54] = ammo;
        }
        void Pick(bool dontConsume, out int type, out float speed, out int damage, out float knockback)
        {
            type = weapon.shoot;
            speed = weapon.shootSpeed;
            damage = 10;
            knockback = 1;
            var canShoot = true;
            var used = 0;
            player.PickAmmo(weapon, ref type, ref speed, ref canShoot, ref damage, ref knockback, out used, dontConsume);
        }
        AmmoProbe.Active = true;
        try
        {
            PT.Case("AMMO-H1", c =>
            {
                Prepare(3);
                c.True(player.HasAmmo(weapon, true), "原生HasAmmo识别可用箭");
                AmmoProbe.Block = true;
                Pick(false, out _, out _, out _, out _);
                c.Eq(ammo.stack, 3, "CanConsumeAmmo否决后仍保留弹药");
                c.True(ammo.consumable, "临时consumable标记已恢复");
                c.Eq(AmmoProbe.Consumed, 0, "否决不发消费通知");
            });
            PT.Case("AMMO-H2", c =>
            {
                Prepare(3);
                AmmoProbe.Alter = true;
                Pick(true, out var type, out var speed, out _, out var knockback);
                c.True(AmmoProbe.Picked > 0, "真实PickAmmo入口调用作者弹药参数回调");
                c.Eq(type, (int)ProjectileID.WoodenArrowFriendly, "弹幕类型回写");
                c.Eq(speed, 7f, "速度回写");
                c.Eq(knockback, 4f, "击退回写");
                c.Eq(ammo.stack, 3, "dontConsume保持stack");
                c.Eq(AmmoProbe.Consumed, 0, "预览不发消费通知");
            });
            PT.Case("AMMO-H3", c =>
            {
                Prepare(1);
                Pick(false, out _, out _, out _, out _);
                c.Eq(AmmoProbe.Consumed, 1, "真实消耗通知一次");
                c.Eq(AmmoProbe.LastAmmoType, (int)ItemID.WoodenArrow, "最后一发通知前保留物品type");
                c.Eq(AmmoProbe.LastAmmoStack, 0, "通知收到消耗后的stack");
                c.Eq(ammo.type, 0, "通知完成后才清空物品");
            });
        }
        finally
        {
            AmmoProbe.Active = false;
            AmmoProbe.Reset();
            for (var i = 0; i < saved.Length; i++) inventory[i] = saved[i];
        }
    }
}
