using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;

namespace XiheMagic
{
    public sealed class XiheMagicControler : ModProjectile
    {
        // 画面全在屏幕效果里，弹幕自己不需要贴图。
        public override string TexturePath => null;

        private bool _inited;
        private bool _spawnedPillar;
        private Vector2 _skyTarget;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults(Projectile proj)
        {
            proj.width = 1;
            proj.height = 1;
            proj.aiStyle = -1;
            proj.timeLeft = 500;
            proj.hide = true;
            proj.friendly = true;
            proj.hostile = false;
            proj.tileCollide = false;
            proj.ignoreWater = true;
            proj.penetrate = -1;
            proj.magic = true;
        }

        public override void OnSpawn(Projectile proj)
        {
            _inited = true;
            _spawnedPillar = false;

            var player = Main.player[proj.owner];
            _skyTarget = player != null
                ? new Vector2(Main.MouseWorld.X, player.position.Y - 1500f)
                : proj.Center;

            if (proj.owner == Main.myPlayer)
                XiheMagicMod.Log?.Info($"[xihe] controller spawn dmg={proj.damage}");
        }

        public override void AI(Projectile proj)
        {
            var player = Main.player[proj.owner];
            if (player == null || !player.active)
            {
                proj.Kill();
                return;
            }

            if (!_inited) OnSpawn(proj);

            var alpha = Math.Min(1f, proj.localAI[0] + 0.01f);
            // localAI/ai 是定长结构体属性：改完必须整体写回，否则改的是副本。
            var localai = proj.localAI;
            localai[0] = alpha;
            proj.localAI = localai;

            Lighting.AddLight(proj.Center, 1.5f * alpha, 1.5f * alpha, 0.8f * alpha);

            // 准星 X 缓慢跟随（Y 固定在玩家上方 1500）。
            if (proj.owner == Main.myPlayer)
                _skyTarget.X = MathHelper.Lerp(_skyTarget.X, Main.MouseWorld.X, 0.01f);
            _skyTarget.Y = player.position.Y - 1500f;

            // 引导金尘：越接近发射越密。
            var chance = 12;
            if (proj.timeLeft < 300) chance = 5;
            if (proj.timeLeft < 190) chance = 2;
            if (Main.rand.Next(chance) == 0) SpawnMote(proj, player);

            // 200 tick 后降下激光柱（只在本地拥有者上发射，别的客户端靠同步）。
            if (!_spawnedPillar && proj.timeLeft <= 300)
            {
                _spawnedPillar = true;
                if (proj.owner == Main.myPlayer) SpawnPillar(proj, player);
            }

            // 松手就断。
            if (!player.controlUseItem)
            {
                proj.Kill();
                return;
            }

            // 免费引导 310 tick；之后每 tick 扣 10 魔力续命。
            if (proj.timeLeft < 190)
            {
                if (player.controlUseItem && player.statMana >= 10)
                {
                    player.statMana -= 10;
                    player.ManaEffect(-10);
                    proj.timeLeft++;
                }
                else
                {
                    proj.Kill();
                }
            }

            // 跟着玩家走。
            var target = player.Center + new Vector2(20 * player.direction, -3f);
            if (Vector2.Distance(proj.Center, target) > 30f)
                proj.Center = Vector2.Lerp(proj.Center, target, 0.5f);
            else
                proj.Center = target;
        }

        private void SpawnPillar(Projectile proj, Player player)
        {
            var type = ModContent.ProjectileType<XiheLaserPillar>();
            if (type <= 0) return;

            // MEAC：位置 = 鼠标上方 300，伤害继承控制器，ai0 记控制器槽位（激光柱据此判断引导是否还在）。
            var source = player.GetProjectileSource_Item(player.HeldItem);
            Projectile.NewProjectile(source, Main.MouseWorld - new Vector2(0f, 300f), Vector2.Zero, type,
                proj.damage, 0f, proj.owner, proj.whoAmI, 0f, 0f);
        }

        private void SpawnMote(Projectile proj, Player player)
        {
            var pos = proj.Center + new Vector2(Main.rand.Next(-10, 11), Main.rand.Next(-10, 11));
            var dust = Dust.NewDustDirect(pos, 1, 1, DustID.GoldFlame,
                Main.rand.NextFloat() * 2f - 1f, Main.rand.NextFloat() * -2f, 0, default(Color), 1.1f);
            dust.noGravity = true;
            dust.velocity = (proj.Center - _skyTarget) * -0.018f + new Vector2(0f, -1.2f);

            if (Main.rand.Next(4) == 0)
            {
                var spark = Dust.NewDustDirect(pos, 1, 1, DustID.YellowStarDust, 0f, 0f, 0, default(Color), 0.9f);
                spark.noGravity = true;
                spark.velocity = dust.velocity;
            }
        }

        public override bool CanDamage(Projectile proj) => false;

        public override bool PreDraw(Projectile proj, Color lightColor) => false;

        public override void OnKill(Projectile proj, int timeLeft)
        {
            if (proj.owner != Main.myPlayer) return;

            for (var i = 0; i < 18; i++)
            {
                var dust = Dust.NewDustDirect(proj.Center, 1, 1, DustID.GoldFlame,
                    Main.rand.NextFloat() * 6f - 3f, Main.rand.NextFloat() * 6f - 3f,
                    0, default(Color), 1.3f);
                dust.noGravity = true;
            }
        }
    }
}
