using System;
using System.Collections.Generic;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace RegressionTest.Content
{
    /// <summary>
    /// 城镇商人（P1-9 入住/存读、P2-9 吸血当帧生效、P1-3 场景指标对照）。
    /// 静帧贴图（不设 npcFrameCount → 单帧），AI=7 站立。
    /// </summary>
    public sealed class MerchantNpc : ModNpc
    {
        public static bool WasHome;   // 入住过一次就置位（P1-9 判据用）

        public override string DisplayName => "回归商人";

        public override void SetDefaults()
        {
            NPC.width = 18;
            NPC.height = 40;
            NPC.aiStyle = 7;
            NPC.townNPC = true;
            NPC.friendly = true;
            NPC.lifeMax = 250;
            NPC.life = 250;
            NPC.damage = 0;
            NPC.defense = 15;
            NPC.knockBackResist = 0.5f;
            NPC.HitSound = SoundID.NPCHit1;
            NPC.DeathSound = SoundID.NPCDeath1;
        }

        public override bool CanTownNPCSpawn(int numTownNPCs) => true;

        public override List<string> SetNPCNameList() => ["回归掌柜"];

        public override string GetChat()
        {
            if (NPC.homeTileX > 0) WasHome = true;
            return "回归测试商人在岗。";
        }

        /// <summary>P2-9（审计后入口）：UpdateLifeRegen 里写的值要当帧生效。</summary>
        public override void UpdateLifeRegen(ref int regen)
        {
            if (RegressionTestMod.MerchantVampire)
            {
                regen += 6;
                if (Main.GameUpdateCount % 120 == 0)
                    RegressionTestMod.Log?.Info($"[rt] 商人 UpdateLifeRegen regen={regen} life={NPC.life}/{NPC.lifeMax}");
            }
        }
    }

    /// <summary>
    /// 掉血 Boss（P2-9 负值掉血 + P1-3 免疫表/场景指标对照）。史莱姆 AI，程序化生成。
    /// </summary>
    public sealed class DrainBossNpc : ModNpc
    {
        private static int _lastLife;

        public override string DisplayName => "回归掉血 Boss";

        public override void SetDefaults()
        {
            NPC.width = 60;
            NPC.height = 44;
            NPC.aiStyle = 1; // 史莱姆 AI
            NPC.damage = 15;
            NPC.defense = 10;
            NPC.lifeMax = 500;
            NPC.life = 500;
            NPC.knockBackResist = 0f;
            NPC.value = 10000f;
            NPC.boss = true;
            NPC.HitSound = SoundID.NPCHit1;
            NPC.DeathSound = SoundID.NPCDeath1;
        }

        public override void UpdateLifeRegen(ref int regen)
        {
            if (RegressionTestMod.BossDrain && regen >= 0)
            {
                regen = -10;
                if (NPC.life != _lastLife)
                {
                    _lastLife = NPC.life;
                    RegressionTestMod.Log?.Info($"[rt] Boss UpdateLifeRegen regen={regen} life={NPC.life}/{NPC.lifeMax}");
                }
            }
        }
    }
}
