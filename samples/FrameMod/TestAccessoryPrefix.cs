// FrameMod / TestAccessoryPrefix —— 示例饰品前缀（+4 防御，价格 ×1.2）
// 验证 ApplyAccessoryEffects 链（GrantPrefixBenefits 后按帧调用）；走 PrefixesForAccessories 分类。
using Pylon.ModLoader;
using Terraria;

namespace FrameMod
{
    public sealed class TestAccessoryPrefix : ModPrefix
    {
        public override string DisplayName { get; set; } = "守护的";

        // PrefixCategory.Accessory
        public override int Category => ModPrefix.CategoryAccessory;

        /// <summary>ApplyAccessoryEffects 被调用的次数（自检用）。</summary>
        public static int EffectCount { get; private set; }

        public override void ModifyValue(ref float valueMult) => valueMult *= 1.2f;

        public override void ApplyAccessoryEffects(Player player)
        {
            EffectCount++;
            player.statDefense += 4;
        }
    }
}
