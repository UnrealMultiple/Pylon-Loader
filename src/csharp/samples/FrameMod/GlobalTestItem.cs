// FrameMod / GlobalTestItem —— 验证 GlobalItem 分派：只挑"测试之剑"记一次日志（防刷屏）。
using Pylon.ModLoader;
using Terraria;

namespace FrameMod
{
    public sealed class GlobalTestItem : GlobalItem
    {
        private static int _logged;

        private void LogOnce(string what)
        {
            if (_logged >= 6) return;
            _logged++;
            Mod.Logger.Info($"[global] GlobalTestItem.{what} 命中测试之剑");
        }

        /// <summary>SetDefaults：每个物品生成/换类型时（原版物品也进）。</summary>
        public override void SetDefaults(Item item)
        {
            if (item.type == Pylon.ModLoader.ModContent.ItemType<TestSword>()) LogOnce("SetDefaults");
        }

        /// <summary>CanUseItem：global 的否决也生效（这里放行）。</summary>
        public override bool CanUseItem(Item item, Player player)
        {
            if (item.type == Pylon.ModLoader.ModContent.ItemType<TestSword>()) LogOnce("CanUseItem");
            return true;
        }

        /// <summary>ModifyShootStats：global 也改参数（叠加在 ModItem 之后）。</summary>
        public override void ModifyShootStats(Item item, Player player, ModItem.ShootStats stats)
        {
            if (item.type == Pylon.ModLoader.ModContent.ItemType<TestSword>())
            {
                stats.Damage += 3;
                LogOnce("ModifyShootStats(+3)");
            }
        }
    }
}