using System;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Terraria.UI;

namespace PipelineTest.Tests;

/// <summary>
/// 装备贴图管线。
/// equip（Ready）：AutoloadEquip 等价的槽位发号、槽位主人表、贴图进表、饰品互斥。
/// equip-w（World）：穿上身后 UpdateEquip / UpdateAccessory / 套装 / 时装套 / 翅膀速度回调。
/// </summary>
internal static class EquipTests
{
    public static void Register()
    {
        PT.Group("equip", Phase.Ready, RunReady);
        PT.Group("equip-w", Phase.World, RunWorld);
    }

    private static Item Make(int type)
    {
        var item = new Item();
        item.SetDefaults(type, null);
        return item;
    }

    private static void RunReady()
    {
        PT.Case("EQUIP-R1", c =>
        {
            var helmetType = ModContent.ItemType<Content.TestHelmet>();
            var bodyType = ModContent.ItemType<Content.TestBreastplate>();
            var legsType = ModContent.ItemType<Content.TestLeggings>();
            var wingType = ModContent.ItemType<Content.TestWings>();
            var shieldType = ModContent.ItemType<Content.TestShield>();

            var helmet = ContentSamples.ItemsByType[helmetType];
            var body = ContentSamples.ItemsByType[bodyType];
            var legs = ContentSamples.ItemsByType[legsType];
            var wings = ContentSamples.ItemsByType[wingType];
            var shield = ContentSamples.ItemsByType[shieldType];
            c.NotNull(helmet, "头盔模板");

            c.True(helmet!.headSlot > 0, $"headSlot={helmet.headSlot}");
            c.True(body!.bodySlot > 0, $"bodySlot={body.bodySlot}");
            c.True(legs!.legSlot > 0, $"legSlot={legs.legSlot}");
            c.True(wings!.wingSlot > 0, $"wingSlot={wings.wingSlot}");
            c.True(shield!.shieldSlot > 0, $"shieldSlot={shield.shieldSlot}");

            // 槽位 → 物品编号：游戏按时装槽位反查物品，没有这张表 mod 盔甲画不出来。
            c.Eq(Item.headType[helmet.headSlot], helmetType, "Item.headType[headSlot]");
            c.Eq(Item.bodyType[body.bodySlot], bodyType, "Item.bodyType[bodySlot]");
            c.Eq(Item.legType[legs.legSlot], legsType, "Item.legType[legSlot]");
        });

        PT.Case("EQUIP-R2", c =>
        {
            var helmet = ContentSamples.ItemsByType[ModContent.ItemType<Content.TestHelmet>()];
            var body = ContentSamples.ItemsByType[ModContent.ItemType<Content.TestBreastplate>()];
            var legs = ContentSamples.ItemsByType[ModContent.ItemType<Content.TestLeggings>()];
            var wings = ContentSamples.ItemsByType[ModContent.ItemType<Content.TestWings>()];
            var shield = ContentSamples.ItemsByType[ModContent.ItemType<Content.TestShield>()];

            var assets = Terraria.GameContent.TextureAssets.ArmorHead;
            c.True(helmet!.headSlot < assets.Length && assets[helmet.headSlot] is not null, $"ArmorHead[{helmet.headSlot}]");
            var composite = Terraria.GameContent.TextureAssets.ArmorBodyComposite;
            c.True(body!.bodySlot < composite.Length && composite[body.bodySlot] is not null, $"ArmorBodyComposite[{body.bodySlot}]");
            var legAssets = Terraria.GameContent.TextureAssets.ArmorLeg;
            c.True(legs!.legSlot < legAssets.Length && legAssets[legs.legSlot] is not null, $"ArmorLeg[{legs.legSlot}]");
            var wingAssets = Terraria.GameContent.TextureAssets.Wings;
            c.True(wings!.wingSlot < wingAssets.Length && wingAssets[wings.wingSlot] is not null, $"Wings[{wings.wingSlot}]");
            var shieldAssets = Terraria.GameContent.TextureAssets.AccShield;
            c.True(shield!.shieldSlot < shieldAssets.Length && shieldAssets[shield.shieldSlot] is not null, $"AccShield[{shield.shieldSlot}]");

            // 女性躯干层也是同一张表长：不写的话女性角色穿 mod 胸甲画不出来（或画成别人的）。
            var femaleBody = Terraria.GameContent.TextureAssets.FemaleBody;
            c.True(body!.bodySlot < femaleBody.Length && femaleBody[body.bodySlot] is not null, $"FemaleBody[{body.bodySlot}]");
        });

        PT.Case("EQUIP-R3", c =>
        {
            var shield = Make(ModContent.ItemType<Content.TestShield>());
            var charm = Make(ModContent.ItemType<Content.TestCharm>());
            var band = Make(ItemID.BandofRegeneration);

            c.True(!ItemSlot.CanEquipBothAccessories(shield, charm, false), "盾 + 护符不能同时装");
            c.True(!ItemSlot.CanEquipBothAccessories(charm, shield, false), "护符 + 盾不能同时装");
            c.True(ItemSlot.CanEquipBothAccessories(charm, band, false), "护符 + 原版饰品可以同时装");
        });
    }

    private static void RunWorld()
    {
        var player = Main.player[Main.myPlayer];
        if (player is null || !player.active)
        {
            PT.SkipCase("EQUIP-W1", "玩家还没就绪");
            return;
        }

        var helmet = Make(ModContent.ItemType<Content.TestHelmet>());
        var body = Make(ModContent.ItemType<Content.TestBreastplate>());
        var legs = Make(ModContent.ItemType<Content.TestLeggings>());
        var wings = Make(ModContent.ItemType<Content.TestWings>());
        var shield = Make(ModContent.ItemType<Content.TestShield>());

        Item[] saved = [];

        PT.Case("EQUIP-W1", c =>
        {
            _ = c;

            var armor = player.armor;
            saved = new Item[5];
            for (var i = 0; i < 5; i++) saved[i] = armor[i];

            armor[0] = helmet;
            armor[1] = body;
            armor[2] = legs;
            armor[3] = wings;
            armor[4] = shield;

            Content.TestBreastplate.Updates = 0;
            Content.TestShield.Updates = 0;
            Content.TestHelmet.SetUpdates = 0;
            Content.TestHelmet.VanityUpdates = 0;

            PT.Later("EQUIP-W1", 20, cc =>
            {
                cc.True(Content.TestBreastplate.Updates > 0, $"UpdateEquip 次数={Content.TestBreastplate.Updates}");
                cc.True(Content.TestShield.Updates > 0, $"UpdateAccessory 次数={Content.TestShield.Updates}");
                cc.True(Content.TestHelmet.SetUpdates > 0, $"UpdateArmorSet 次数={Content.TestHelmet.SetUpdates}");
                cc.True(Content.TestHelmet.VanityUpdates > 0, $"UpdateVanitySet 次数={Content.TestHelmet.VanityUpdates}");

                var worn = player.armor;
                cc.Eq(player.head, worn[0].headSlot, "画出来的头槽 = 头盔槽");
                cc.Eq(player.body, worn[1].bodySlot, "画出来的胸槽 = 胸甲槽");
                cc.Eq(player.legs, worn[2].legSlot, "画出来的腿槽 = 护腿槽");
            });
        });

        PT.Case("EQUIP-W2", c =>
        {
            _ = c;

            PT.Later("EQUIP-W2", 35, cc =>
            {
                var velocity = player.velocity;
                var wingTime = player.wingTime;

                // 量一遍原版翅膀的平飞数值（logic=1 天使翼），mod 翅膀要跟它对齐才"能左右飞"。
                var vanillaLogic = player.wingsLogic;
                var vanillaSpeed = player.accRunSpeed;
                var vanillaAccel = player.runAcceleration;
                var vanillaMax = player.maxRunSpeed;
                player.wingsLogic = 1;
                player.WingAirLogicTweaks();
                player.accRunSpeed = vanillaSpeed;
                player.runAcceleration = vanillaAccel;
                player.maxRunSpeed = vanillaMax;
                player.wingsLogic = vanillaLogic;

                Content.TestWings.Vertical = 0;
                Content.TestWings.Horizontal = 0;
                player.wingsLogic = player.armor[3].wingSlot;
                player.WingMovement();
                player.WingAirLogicTweaks();

                player.velocity = velocity;
                player.wingTime = wingTime;

                cc.Eq(Content.TestWings.Vertical, 1, "VerticalWingSpeeds 调用");
                cc.Eq(Content.TestWings.Horizontal, 1, "HorizontalWingSpeeds 调用");

                // 真的能飞：画出来的翅膀号对得上、飞行时间不是 0（0 就是"按跳跃键毫无反应"）。
                cc.Eq(player.wings, player.armor[3].wingSlot, "player.wings = 翅膀槽");
                cc.True(player.wingTimeMax > 0f, $"wingTimeMax={player.wingTimeMax}");

                // 平飞能左右移动：原版用 maxRunSpeed 夹水平速度，作者改的 accRunSpeed 必须同步到它，
                // 否则按左右只能原地蹭（v3.0 的旧值就是 3）。
                cc.True(player.accRunSpeed >= 9f - 0.01f, $"平飞速度 accRunSpeed={player.accRunSpeed}");
                cc.True(player.maxRunSpeed >= player.accRunSpeed - 0.01f,
                        $"平飞速度上限 maxRunSpeed={player.maxRunSpeed} accRunSpeed={player.accRunSpeed}");
            });
        });

        PT.Case("EQUIP-W3", c =>
        {
            _ = c;

            PT.Later("EQUIP-W3", 50, cc =>
            {
                // 这套盔甲故意留在身上：方便直接看效果（原来那 5 件记在 saved 里）。
                var armor = player.armor;
                cc.True(saved.Length == 5, $"保存了 {saved.Length} 个原装备槽");
                cc.Eq(armor[0].headSlot, player.head, "头盔还在身上");
                cc.Eq(armor[1].bodySlot, player.body, "胸甲还在身上");
                cc.Eq(armor[2].legSlot, player.legs, "护腿还在身上");
                PT.Info("EQUIP：测试盔甲已穿在身上（头/胸/腿 + 翅膀 + 盾），可以直接看效果");
            });
        });
    }
}
