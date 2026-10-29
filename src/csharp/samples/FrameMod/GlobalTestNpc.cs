// FrameMod / GlobalTestNpc —— 验证 GlobalNpc 注册表与分派（AppliesToEntity 只挑测试史莱姆）。
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;

namespace FrameMod
{
    public sealed class GlobalTestNpc : GlobalNpc
    {
        public override bool AppliesToEntity(NPC entity, bool lateInstantiation) =>
            entity.type == ModContent.NpcType<TestNpc>();

        private static readonly System.Collections.Generic.HashSet<string> Logged = new();

        private void LogOnce(string what)
        {
            if (!Logged.Add(what)) return;
            Mod.Logger.Info($"[global] GlobalTestNpc.{what} 命中测试史莱姆");
        }

        public override void SetDefaults(NPC npc) => LogOnce("SetDefaults");

        public override void OnSpawn(NPC npc, IEntitySource source) => LogOnce("OnSpawn");

        public override void ResetEffects(NPC npc) => LogOnce("ResetEffects");

        public override bool PreAI(NPC npc)
        {
            LogOnce("PreAI");
            return true;
        }

        public override void AI(NPC npc) => LogOnce("AI");

        public override void PostAI(NPC npc) => LogOnce("PostAI");

        public override void HitEffect(NPC npc, int hitDirection, double damage) => LogOnce("HitEffect");

        public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, ref Color drawColor)
        {
            LogOnce("PreDraw");
            return true;
        }

        public override void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) =>
            LogOnce("PostDraw");

        public override bool PreKill(NPC npc)
        {
            LogOnce("PreKill");
            return true;
        }

        public override void OnKill(NPC npc) => LogOnce("OnKill");

        public override void ApplyDifficultyAndPlayerScaling(NPC npc, int numPlayers, float balance, float bossAdjustment) =>
            LogOnce("ApplyDifficultyAndPlayerScaling");
    }
}
