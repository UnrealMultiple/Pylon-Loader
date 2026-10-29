// FrameMod / TestMountItem —— 使用后骑上测试坐骑（手动使用）。
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;

namespace FrameMod
{
    public sealed class TestMountItem : ModItem
    {
        public override string DisplayName => "测试坐骑哨";

        public override string Tooltip => "使用后骑上测试史莱姆\n（验证 ModMount 管线，1 小时）";

        public override void SetDefaults(Item item)
        {
            item.width = 22;
            item.height = 24;
            item.useStyle = 4;      // 举起使用
            item.useTime = 20;
            item.useAnimation = 20;
            item.useTurn = true;
            item.maxStack = 1;
            item.rare = 2;
        }

        public override bool UseItem(Item item, Player player)
        {
            var buff = ModContent.BuffType<TestMountBuff>();
            player.AddBuff(buff, 3600, false);
            Mod.Logger.Info($"[mount] 使用坐骑哨：buff={buff} mount={ModContent.MountType<TestMount>()}");
            return true;
        }
    }
}
