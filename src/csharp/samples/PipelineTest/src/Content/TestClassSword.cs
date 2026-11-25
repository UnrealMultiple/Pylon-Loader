using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Pylon.ModLoader.Content;

namespace PipelineTest.Content;

/// <summary>测试武器：SetDefaults 里声明测试伤害职业（70 伤害、4 击退、6 暴击、30 帧挥动）。</summary>
public sealed class TestClassSword : ModItem
{
    public override void SetDefaults()
    {
        Item.DamageType = ModContent.GetInstance<TestDamageClass>();
        Item.useStyle = (byte)ItemUseStyleID.Swing;
        Item.useTime = 30;
        Item.useAnimation = 30;
        Item.damage = 70;
        Item.knockBack = 4;
        Item.crit = (byte)6;
    }
}
