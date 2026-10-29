// FrameMod / TestPrefix —— 示例自定义前缀（近战：伤害 +15%、攻速 +10%、体积 +5%、暴击 +4%，价格 ×1.2）
// 验证：分类 → GetRollablePrefixes 出现 → RollAPrefix 按 RollChance 加权抽到；
// SetStats 倍率（含价格/稀有度）由游戏自己应用，Apply 与 GlobalItem.ApplyPrefix 装好后跑一次。
// ⚠ Apply 别再乘 damage：数值游戏已应用，只做"非数值"部分（上游 ExamplePrefix.Apply 是空的）。
using Pylon.ModLoader;
using Terraria;

namespace FrameMod
{
    public sealed class TestPrefix : ModPrefix
    {
        public override string DisplayName { get; set; } = "测试的";

        // PrefixCategory.Melee
        public override int Category => ModPrefix.CategoryMelee;

        /// <summary>Apply 真的跑了没有（自检用；不改物品）。</summary>
        public static int ApplyCount { get; private set; }

        private static bool _logged;

        public override void SetStaticDefaults()
        {
            if (_logged) return;
            _logged = true;
            Mod.Logger.Info($"[prefix] TestPrefix 静态默认值（type={Type}，分类={Category}）");
        }

        public override int RollChance(Item item) => 2;

        public override void SetStats(ref float damageMult, ref float knockbackMult, ref float useTimeMult,
                                     ref float scaleMult, ref float shootSpeedMult, ref float manaMult,
                                     ref int critBonus, ref int tagDamage, ref int armorPenetration)
        {
            damageMult = 1.15f;
            useTimeMult = 0.9f;
            scaleMult = 1.05f;
            critBonus = 4;
        }

        public override void ModifyValue(ref float valueMult) => valueMult *= 1.2f;

        public override void Apply(Item item)
        {
            ApplyCount++;
            if (ApplyCount == 1) Mod.Logger.Info($"[prefix] TestPrefix.Apply 在 {item.Name} 上跑了一次");
        }
    }
}
