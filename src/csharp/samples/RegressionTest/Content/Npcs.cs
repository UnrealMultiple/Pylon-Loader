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

        public override void SetDefaults(NPC npc)
        {
            npc.width = 18;
            npc.height = 40;
            npc.aiStyle = 7;
            npc.townNPC = true;
            npc.friendly = true;
            npc.lifeMax = 250;
            npc.life = 250;
            npc.damage = 0;
            npc.defense = 15;
            npc.knockBackResist = 0.5f;
            npc.HitSound = SoundID.NPCHit1;
            npc.DeathSound = SoundID.NPCDeath1;
        }

        public override bool CanTownNPCSpawn(int numTownNPCs) => true;

        public override string[] SetNPCNameList() => ["回归掌柜"];

        public override string GetChat(NPC npc)
        {
            if (npc.homeTileX > 0) WasHome = true;
            return "回归测试商人在岗。";
        }

        /// <summary>P2-9（审计后入口）：UpdateLifeRegen 里写的值要当帧生效。</summary>
        public override void UpdateLifeRegen(NPC npc, ref int regen)
        {
            if (RegressionTestMod.MerchantVampire)
            {
                regen += 6;
                if (Main.GameUpdateCount % 120 == 0)
                    RegressionTestMod.Log?.Info($"[rt] 商人 UpdateLifeRegen regen={regen} life={npc.life}/{npc.lifeMax}");
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

        public override void SetDefaults(NPC npc)
        {
            npc.width = 60;
            npc.height = 44;
            npc.aiStyle = 1; // 史莱姆 AI
            npc.damage = 15;
            npc.defense = 10;
            npc.lifeMax = 500;
            npc.life = 500;
            npc.knockBackResist = 0f;
            npc.value = 10000f;
            npc.boss = true;
            npc.HitSound = SoundID.NPCHit1;
            npc.DeathSound = SoundID.NPCDeath1;
        }

        public override void UpdateLifeRegen(NPC npc, ref int regen)
        {
            if (RegressionTestMod.BossDrain && regen >= 0)
            {
                regen = -10;
                if (npc.life != _lastLife)
                {
                    _lastLife = npc.life;
                    RegressionTestMod.Log?.Info($"[rt] Boss UpdateLifeRegen regen={regen} life={npc.life}/{npc.lifeMax}");
                }
            }
        }

        public override void PostSetupContent()
        {
            _lastLife = -1;
        }
    }
}
