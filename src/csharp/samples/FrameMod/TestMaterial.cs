// FrameMod / TestMaterial —— 示例材料
using Pylon.ModLoader;
using Terraria;

namespace FrameMod
{
    public sealed class TestMaterial : ModItem
    {
        public override string DisplayName => "测试材料";

        public override string Tooltip => "示例材料，验证注入的名字与介绍";

        public override void SetDefaults(Item item)
        {
            item.width = 16;
            item.height = 16;
            item.maxStack = 999;
            item.value = 100;
        }
    }
}