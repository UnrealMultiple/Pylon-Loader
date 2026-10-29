// FrameMod / TestFireball —— 示例自定义弹幕（ModProjectile 子类）
// 验证：占号/模板/SetDefaults/OnSpawn/PreAI/AI/PostAI/撞块/死亡/绘制全链；日志每条只打一次（AI 每帧进，防刷屏）。
using Pylon.ModLoader;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace FrameMod
{
    public sealed class TestFireball : ModProjectile
    {
        public override string DisplayName => "测试火球";

        /// <summary>每发自己的状态（验证实体实例化：每发从 0 开始）。</summary>
        private int _ticks;

        private static readonly System.Collections.Generic.HashSet<string> Logged = new();

        private void LogOnce(string what)
        {
            if (!Logged.Add(what)) return;
            Mod.Logger.Info($"[proj] TestFireball.{what}（type={Type} ticks={_ticks}）");
        }

        /// <summary>模板初始化后跑（表定型后，setStaticDefaults）。</summary>
        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;   // 单帧贴图（多帧就写帧数，SetDefaults 里按帧数除高）
            Mod.Logger.Info($"{Name} 弹幕静态默认值（ID {Type}）");
        }

        /// <summary>每个生成的弹幕调一次。</summary>
        public override void SetDefaults(Projectile proj)
        {
            proj.width = 16;
            proj.height = 16;
            proj.friendly = true;
            proj.hostile = false;
            proj.damage = 25;
            proj.penetrate = 3;
            proj.timeLeft = 300;
            proj.ignoreWater = true;
            proj.tileCollide = true;
            proj.scale = 1f;
            LogOnce("SetDefaults");
        }

        public override void OnSpawn(Projectile proj) => LogOnce("OnSpawn");

        public override bool PreAI(Projectile proj)
        {
            LogOnce("PreAI");
            return true;
        }

        /// <summary>自己的每帧 AI：朝当前方向飞 + 发光 + 转起来。</summary>
        public override void AI(Projectile proj)
        {
            _ticks++;
            proj.rotation += 0.15f;
            Lighting.AddLight(proj.Center, 1f, 0.4f, 0.1f);
            if (_ticks % 60 == 0)
                Mod.Logger.Info($"[proj] TestFireball.AI 第 {_ticks} 帧（pos={proj.position.X:0},{proj.position.Y:0}）");
        }

        public override void PostAI(Projectile proj) => LogOnce("PostAI");

        /// <summary>撞块用的盒子和"穿不穿平台"。</summary>
        public override bool TileCollideStyle(Projectile proj, ref int width, ref int height,
                                              ref bool fallThrough, ref Vector2 hitboxCenterFrac)
        {
            LogOnce("TileCollideStyle");
            width += 2;
            height += 2;
            return true;
        }

        /// <summary>撞方块后弹回一次（返回 false = 别死）。</summary>
        public override bool OnTileCollide(Projectile proj, Vector2 oldVelocity)
        {
            LogOnce("OnTileCollide");
            var v = proj.velocity;
            if (v.X != oldVelocity.X) v.X = -oldVelocity.X;
            if (v.Y != oldVelocity.Y) v.Y = -oldVelocity.Y;
            proj.velocity = v;
            return false;
        }

        public override bool CanDamage(Projectile proj)
        {
            LogOnce("CanDamage");
            return true;
        }

        public override bool? Colliding(Projectile proj, Rectangle projHitbox, Rectangle targetHitbox)
        {
            LogOnce("Colliding");
            return null;   // 用游戏的矩形相交
        }

        public override Color? GetAlpha(Projectile proj, Color lightColor)
        {
            LogOnce("GetAlpha");
            return new Color(255, 200, 120, 255);
        }

        public override bool PreDrawExtras(Projectile proj) => true;

        public override bool PreDraw(Projectile proj, Color lightColor)
        {
            LogOnce("PreDraw");
            return true;   // 让游戏画我们写的贴图
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
