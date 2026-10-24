// FrameMod / TestNpc —— 示例自定义 NPC（敌对，借原版史莱姆的 AI）
// 验证：占号/SetDefaults/ScaleStats/AI（AIType 换 type）/FindFrame/OnSpawn/受击/死亡/绘制分派。
// 生成方式：用 TestSummonItem（自己加到背包里使用），不会自动刷。
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace FrameMod
{
    public sealed class TestNpc : ModNpc
    {
        public override string DisplayName => "测试史莱姆";

        /// <summary>借原版蓝史莱姆的 AI（只在跑游戏 AI 期间换 type）。</summary>
        public override int AIType => NPCID.BlueSlime;

        private int _ticks;
        private static readonly HashSet<string> Logged = new();

        // 自检计数器（FrameSelfTest 用）
        public static int Ticks, OnKillCount;

        private void LogOnce(string what)
        {
            if (!Logged.Add(what)) return;
            Mod.Logger.Info($"[npc] TestNpc.{what}（type={Type}）");
        }

        public override void SetStaticDefaults()
        {
            Main.npcFrameCount[Type] = 1;
            Mod.Logger.Info($"{Name} NPC 静态默认值（ID {Type}）");
        }

        public override void SetDefaults(NPC npc)
        {
            npc.width = 32;
            npc.height = 30;
            npc.aiStyle = 1;            // 史莱姆式跳跃（配合 AIType）
            npc.damage = 12;
            npc.defense = 2;
            npc.lifeMax = 120;
            npc.friendly = false;
            npc.npcSlots = 1f;
            LogOnce("SetDefaults");
        }

        public override void PostSetDefaults(NPC npc) => LogOnce("PostSetDefaults");

        public override void OnSpawn(NPC npc, IEntitySource source) => LogOnce("OnSpawn");

        public override void ResetEffects(NPC npc) => LogOnce("ResetEffects");

        public override bool PreAI(NPC npc)
        {
            LogOnce("PreAI");
            return true;
        }

        public override void AI(NPC npc)
        {
            _ticks++;
            Ticks++;
            if (_ticks % 120 == 0)
                Mod.Logger.Info($"[npc] TestNpc.AI 第 {_ticks} 帧（pos={npc.position.X:0},{npc.position.Y:0}）");
        }

        public override void PostAI(NPC npc) => LogOnce("PostAI");

        /// <summary>自定义动画：单帧贴图，只把 frame 归零（验证 FindFrame 由 mod 接管、不跑原版）。</summary>
        public override void FindFrame(NPC npc, int frameHeight)
        {
            LogOnce("FindFrame");
            var frame = npc.frame;   // Rectangle 是 struct：读出来改完写回
            frame.Y = 0;
            npc.frame = frame;
        }

        public override void ApplyDifficultyAndPlayerScaling(NPC npc, int numPlayers, float balance, float bossAdjustment) =>
            LogOnce("ApplyDifficultyAndPlayerScaling");

        public override void HitEffect(NPC npc, int hitDirection, double damage)
        {
            LogOnce("HitEffect");
        }

        public override bool PreKill(NPC npc)
        {
            LogOnce("PreKill");
            return true;
        }

        public override void OnKill(NPC npc)
        {
            OnKillCount++;
            LogOnce("OnKill");
        }

        public override bool CheckDead(NPC npc) => true;

        public override bool CheckActive(NPC npc) => true;

        public override void DrawEffects(NPC npc, ref Color drawColor)
        {
            LogOnce("DrawEffects");
        }

        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
        {
            LogOnce("PreDraw");
            return true;
        }

        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) =>
            LogOnce("PostDraw");

        public override string GetChat(NPC npc) => null;
    }
}
