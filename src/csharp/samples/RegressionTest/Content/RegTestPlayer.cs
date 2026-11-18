using System.Collections.Generic;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Pylon.ModLoader.Content;

namespace RegressionTest.Content
{
    /// <summary>
    /// 回归测试玩家面：鱼饵消耗裁决（P2-8）与染料商奖励池手术（P2-8b），原 Mod 级虚方法迁移，
    /// 开关与计数挂在 RegressionTestMod 的静态字段上（单次动作由 ImGui 页置位）。
    /// </summary>
    public sealed class RegTestPlayer : ModPlayer
    {
        public override bool? CanConsumeBait(Item bait)
        {
            RegressionTestMod.CntConsumeBait++;
            switch (RegressionTestMod.BaitMode)
            {
                case 1:
                    RegressionTestMod.Log.Info($"[rt] CanConsumeBait→true（保证消耗一颗，bait={bait.type}）");
                    return true;
                case 2:
                    RegressionTestMod.Log.Info("[rt] CanConsumeBait→false（拒绝消耗：鱼应照上钩、饵应保留）");
                    return false;
                default:
                    return null;
            }
        }

        public override void GetDyeTraderReward(List<int> rewardPool)
        {
            RegressionTestMod.CntDyeReward++;
            switch (RegressionTestMod.DyeAction)
            {
                case 1:
                    rewardPool.Add(ItemID.Fireblossom);
                    RegressionTestMod.Log.Info($"[rt] 染料池 +1（现有 {rewardPool.Count} 条）");
                    break;
                case 2:
                    if (rewardPool.Count > 0)
                    {
                        rewardPool.RemoveAt(rewardPool.Count - 1);
                        RegressionTestMod.Log.Info($"[rt] 染料池 -1（剩 {rewardPool.Count} 条）");
                    }
                    break;
                case 3:
                    rewardPool.Clear();
                    RegressionTestMod.Log.Info("[rt] 染料池已清空（发放应不崩，补 0）");
                    break;
                case 4:
                    rewardPool.Add(-5);
                    RegressionTestMod.Log.Info($"[rt] 染料池塞负数（现有 {rewardPool.Count} 条，-5 应被丢弃）");
                    break;
                default:
                    RegressionTestMod.Log.Info($"[rt] GetDyeTraderReward 旁路观察（池 {rewardPool.Count} 条）");
                    break;
            }
            RegressionTestMod.DyeAction = 0;
        }
    }
}
