// FrameMod / TestSummonItem —— 把 TestNpc 召唤到玩家面前的示例物品（手动使用，不自动刷）。
// 验证：ModItem.UseItem → NPC.NewNPC → ModNpc.OnSpawn/GlobalNpc.OnSpawn 全链。
using Pylon.ModLoader;
using Il2CppMicrosoft.Xna.Framework;
using Terraria;

namespace FrameMod
{
    public sealed class TestSummonItem : ModItem
    {
        public override string DisplayName => "测试召唤物";

        public override string Tooltip => "使用后在你面前召唤一只测试史莱姆\n（验证 ModNpc 管线，不会自动生成）";

        public override void SetDefaults(Item item)
        {
            item.width = 20;
            item.height = 24;
            item.useStyle = 1;
            item.useTime = 20;
            item.useAnimation = 20;
            item.useTurn = true;
            item.autoReuse = true;
            item.maxStack = 1;
            item.rare = 2;
        }

        public override bool UseItem(Item item, Player player)
        {
            var type = ModContent.NpcType<TestNpc>();
            var source = player.GetProjectileSource_Item(item);
            var offset = new Vector2(80f * player.direction, -16f);
            var index = NPC.NewNPC(source,
                (int)(player.Center.X + offset.X), (int)(player.Center.Y + offset.Y), type);
            Mod.Logger.Info($"[npc] 召唤测试史莱姆：槽 {index}（type={type}）");
            return true;
        }
    }
}
