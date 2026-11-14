using Pylon.ModLoader;
using Terraria;

namespace RegTestPrefixMod
{
    /// <summary>独立前缀 Mod（P1-1/A-2/A-3 卸载场景专用）：卸载后前缀键应进 pending，重装恢复。</summary>
    public sealed class StrangerPrefix : ModPrefix
    {
        public override string DisplayName => "陌生之印";

        public override int Category => CategoryAnyWeapon;

        public override int RollChance(Item item) => 3;

        public override void SetStats(ref float damageMult, ref float knockbackMult, ref float useTimeMult,
                                      ref float scaleMult, ref float shootSpeedMult, ref float manaMult,
                                      ref int critBonus, ref int tagDamage, ref int armorPenetration)
        {
            damageMult *= 1.2f;
        }
    }

    public sealed class StrangerPrefixMod : Mod
    {
        public override void Load()
        {
            Logger.Info("[rt] RegTestPrefixMod 已加载（独立前缀 mod：陌生之印）");
        }
    }
}
