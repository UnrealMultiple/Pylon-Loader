// FrameMod / GlobalTestProjectile —— 验证 GlobalProjectile 分派：只挑"测试火球"记日志（AppliesToEntity 过滤）。
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;

namespace FrameMod
{
    public sealed class GlobalTestProjectile : GlobalProjectile
    {
        public override bool AppliesToEntity(Projectile entity, bool lateInstantiation) =>
            entity.type == ModContent.ProjectileType<TestFireball>();

        private static readonly System.Collections.Generic.HashSet<string> Logged = new();

        private void LogOnce(string what)
        {
            if (!Logged.Add(what)) return;
            Mod.Logger.Info($"[global] GlobalTestProjectile.{what} 命中测试火球");
        }

        public override void SetDefaults(Projectile proj) => LogOnce("SetDefaults");

        public override void OnSpawn(Projectile proj, IEntitySource source) => LogOnce("OnSpawn");

        public override bool PreAI(Projectile proj)
        {
            LogOnce("PreAI");
            return true;
        }

        public override void AI(Projectile proj) => LogOnce("AI");

        public override void PostAI(Projectile proj) => LogOnce("PostAI");

        public override bool? CanDamage(Projectile proj)
        {
            LogOnce("CanDamage");
            return null;   // 交给 mod 与游戏
        }

        public override bool PreDraw(Projectile proj, ref Color lightColor)
        {
            LogOnce("PreDraw");
            return true;
        }

        public override void PostDraw(Projectile proj, Color lightColor) => LogOnce("PostDraw");

        public override bool PreKill(Projectile proj, int timeLeft)
        {
            LogOnce("PreKill");
            return true;
        }

        public override void OnKill(Projectile proj, int timeLeft) => LogOnce("OnKill");
    }
}
