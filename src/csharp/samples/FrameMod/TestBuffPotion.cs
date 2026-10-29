// FrameMod / TestBuffPotion —— 喝一口给 TestBuff（手动使用，验证加 buff 全链）。
using Pylon.ModLoader;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;

namespace FrameMod
{
    public sealed class TestBuffPotion : ModItem
    {
        public override string DisplayName => "测试药剂";

        public override string Tooltip => "使用后获得 30 秒测试增益\n（验证 ModBuff 管线）";

        public override void SetDefaults(Item item)
        {
            item.width = 20;
            item.height = 26;
            item.useStyle = 9;      // 喝
            item.useTime = 20;
            item.useAnimation = 20;
            item.useTurn = true;
            item.maxStack = 30;
            item.consumable = true;
            item.rare = 1;
        }

        public override bool UseItem(Item item, Player player)
        {
            player.AddBuff(ModContent.BuffType<TestBuff>(), 1800, false);
            Mod.Logger.Info($"[buff] 喝下测试药剂：buff type={ModContent.BuffType<TestBuff>()}");
            return true;
        }
    }
}
