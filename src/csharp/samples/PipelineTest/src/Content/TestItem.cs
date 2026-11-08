using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>测试物品：非常规数值 + 一行介绍 + ModifyTooltips 加一行，验证 tooltip 链不崩。</summary>
public sealed class TestItem : ModItem
{
    public override string DisplayName => "测试物品";
    public override string Tooltip => "这是一行测试介绍";

    public override void SetDefaults(Item item)
    {
        item.width = 20;
        item.height = 20;
        item.maxStack = ModItem.CommonMaxStack;
        item.value = 100;
        item.rare = 1;

        // 近战武器：能挥舞，命中判定尺寸
        item.damage = 15;
        item.melee = true;
        item.useTime = 20;
        item.useAnimation = 20;
        item.useStyle = (byte)Terraria.ID.ItemUseStyleID.Swing;
        item.knockBack = 4f;
        item.autoReuse = true;

        // 挥舞时发射弹幕（ Shoot 钩子链路）
        item.shoot = Terraria.ID.ProjectileID.WoodenArrowFriendly;
        item.shootSpeed = 8f;
    }

    public override void ModifyTooltipLines()
    {
        // TooltipLines 管线功能验证：额外加一行
        TooltipLines.Add("[TestItem] ModifyTooltipLines 加的行");
    }

    public override bool Shoot(Item item, Player player, Vector2 position, Vector2 velocity, int type, int damage, float knockBack)
    {
        Tests.ItemTests2.ShootCalled = true;
        // 发射自定义测试弹幕（Projectile 管线肉眼验证）：贴图为 assets/projectiles/TestProjectile.png
        var projType = ModContent.ProjectileType<TestProjectile>();
        Terraria.Projectile.NewProjectile(null, position, velocity,
            projType, damage, knockBack, player.whoAmI);
        return false; // 不走原版的弹药消耗路径
    }

}