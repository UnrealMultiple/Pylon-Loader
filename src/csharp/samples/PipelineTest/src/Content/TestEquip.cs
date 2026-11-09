using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.Enums;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ObjectData;

namespace PipelineTest.Content;

/// <summary>测试头盔：穿齐三件跑套装 / 时装套回调，各计数。</summary>
public sealed class TestHelmet : ModItem
{
    public static int SetUpdates, VanityUpdates;

    public override void Load() => AddEquipTexture("TestHelmet_Head", EquipType.Head, "assets/items/TestHelmet_Head.png");

    public override void SetDefaults(Item item)
    {
        item.width = 18;
        item.height = 18;
        item.defense = 5;
    }

    public override bool IsArmorSet(Item head, Item body, Item legs) =>
        body.type == ModContent.ItemType<TestBreastplate>() && legs.type == ModContent.ItemType<TestLeggings>();

    public override void UpdateArmorSet(Item item, Player player) => SetUpdates++;

    public override void UpdateVanitySet(Player player) => VanityUpdates++;
}

/// <summary>测试胸甲：UpdateEquip 计数。</summary>
public sealed class TestBreastplate : ModItem
{
    public static int Updates;

    public override void Load() => AddEquipTexture("TestBreastplate_Body", EquipType.Body, "assets/items/TestBreastplate_Body.png");

    public override void SetDefaults(Item item)
    {
        item.width = 18;
        item.height = 18;
        item.defense = 6;
    }

    public override void UpdateEquip(Item item, Player player)
    {
        Updates++;
        player.statManaMax2 += 20;
    }
}

/// <summary>测试护腿。</summary>
public sealed class TestLeggings : ModItem
{
    public override void Load() => AddEquipTexture("TestLeggings_Legs", EquipType.Legs, "assets/items/TestLeggings_Legs.png");

    public override void SetDefaults(Item item)
    {
        item.width = 18;
        item.height = 18;
        item.defense = 5;
    }
}

/// <summary>测试翅膀：竖直 / 平飞速度回调各一次计数。</summary>
public sealed class TestWings : ModItem
{
    public static int Vertical, Horizontal;

    public override void Load() => AddEquipTexture("TestWings_Wings", EquipType.Wing, "assets/items/TestWings_Wings.png");

    public override void SetDefaults(Item item)
    {
        item.width = 22;
        item.height = 20;
        item.accessory = true;
    }

    public override void VerticalWingSpeeds(Item item, Player player, ref float ascentWhenFalling, ref float ascentWhenRising,
                                            ref float maxCanAscendMultiplier, ref float maxAscentMultiplier, ref float constantAscend)
    {
        Vertical++;
        ascentWhenFalling = 0.85f;
        ascentWhenRising = 0.15f;
        maxCanAscendMultiplier = 1f;
        maxAscentMultiplier = 3f;
        constantAscend = 0.135f;
    }

    public override void HorizontalWingSpeeds(Item item, Player player, ref float speed, ref float acceleration)
    {
        Horizontal++;
        speed = 9f;
        acceleration = 0.12f;
    }
}

/// <summary>测试盾：UpdateAccessory 计数。</summary>
public sealed class TestShield : ModItem
{
    public static int Updates;
    public static bool LastVanity;

    public override void Load() => AddEquipTexture("TestShield_Shield", EquipType.Shield, "assets/items/TestShield_Shield.png");

    public override void SetDefaults(Item item)
    {
        item.width = 24;
        item.height = 28;
        item.accessory = true;
        item.defense = 2;
    }

    public override void UpdateAccessory(Item item, Player player, bool vanity, bool hideVisual)
    {
        Updates++;
        LastVanity = vanity;
    }
}

/// <summary>测试护符：和盾互斥。</summary>
public sealed class TestCharm : ModItem
{
    public override void SetDefaults(Item item)
    {
        item.width = 20;
        item.height = 20;
        item.accessory = true;
    }

    public override bool CanAccessoryBeEquippedWith(Item equippedItem, Item incomingItem, Player player) =>
        equippedItem.type != ModContent.ItemType<TestShield>() && incomingItem.type != ModContent.ItemType<TestShield>();
}
