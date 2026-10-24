using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace XiheMagic
{
    public sealed class XiheLaserPillar : ModProjectile
    {
        // 画面全在屏幕效果里，弹幕自己不需要贴图。
        public override string TexturePath => null;

        private bool _fired;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults(Projectile proj)
        {
            proj.width = 20;
            proj.height = 20;
            proj.aiStyle = -1;
            proj.hostile = false;
            proj.friendly = true;
            proj.timeLeft = 360;
            proj.usesLocalNPCImmunity = true;
            proj.localNPCHitCooldown = 3;
            proj.ignoreWater = true;
            proj.tileCollide = false;
            proj.penetrate = -1;
            proj.scale = 0f;
            proj.alpha = 255;
            proj.extraUpdates = 0;
            proj.hide = true;
        }

        public override bool ShouldUpdatePosition(Projectile proj) => false;

        public override void OnSpawn(Projectile proj)
        {
            // 兜底：控制器传过来的伤害若是 0（GetWeaponDamage 在个别路径返回 0），至少给武器基础伤害。
            if (proj.damage <= 0) proj.damage = 200;
            proj.originalDamage = proj.damage;
            if (proj.owner == Main.myPlayer)
                XiheMagicMod.Log?.Info($"[xihe] pillar spawn dmg={proj.damage} scale={proj.scale}");
        }

        public override void AI(Projectile proj)
        {
            proj.rotation = 1.57f;
            var localai = proj.localAI;
            localai[1] = 10000f;
            proj.localAI = localai;

            if (!_fired && proj.timeLeft <= 250)
            {
                _fired = true;
                XiheFx.NotifyFlash();
                if (proj.owner == Main.myPlayer)
                    XiheSounds.PlayLaser(proj.Center);
            }

            // 预热：慢慢变亮（MEAC：timeLeft <= 340 && > 260 时每 tick +0.002）。
            if (proj.timeLeft <= 340 && proj.timeLeft > 260)
                proj.scale += 0.002f;

            // 点火：250→230 快速变粗。
            if (proj.timeLeft > 230 && proj.timeLeft < 250)
                proj.scale += 0.15f;

            if (proj.timeLeft < 245 && proj.timeLeft > 20)
            {
                if (proj.timeLeft < 230)
                {
                    if (ControllerAlive(proj))
                    {
                        proj.timeLeft++;
                        if (proj.owner == Main.myPlayer)
                            proj.Center = new Vector2(
                                MathHelper.Lerp(proj.Center.X, Main.MouseWorld.X, 0.005f),
                                proj.Center.Y);
                    }
                    else
                    {
                        proj.timeLeft = 20;
                    }
                }

                SpawnBeamDust(proj);
            }

            // 收尾：快速收缩。
            if (proj.timeLeft < 20)
                proj.scale = Math.Max(0f, proj.scale - 0.15f);

            var light = Math.Min(1.5f, proj.scale * 0.5f);
            Lighting.AddLight(proj.Center, 1.2f * light, 0.95f * light, 0.4f * light);

            // Pylon 的战斗命中管线未接（migration-map：StrikeNPC/Hurt 缺），mod 弹幕的
            // Projectile.Damage 打不到 NPC。这里按 MEAC 的 Colliding 规则直接结算：
            // |NPC中心X - 柱心X| < scale*90（整柱竖直贯通），用 NPC.immune 做 3 tick 间隔。
            if (proj.scale > 0.2f && proj.owner == Main.myPlayer)
                DamageColumn(proj);
        }

        private static void DamageColumn(Projectile proj)
        {
            var radius = proj.scale * 90f;
            for (var i = 0; i < Main.npc.Length; i++)
            {
                var npc = Main.npc[i];
                if (npc == null || !npc.active || npc.friendly || npc.dontTakeDamage) continue;
                if (Math.Abs(npc.Center.X - proj.Center.X) >= radius) continue;
                if (proj.owner >= 0 && proj.owner < npc.immune.Length && npc.immune[proj.owner] > 0) continue;

                var dir = npc.Center.X > proj.Center.X ? 1 : -1;
                npc.StrikeNPC(proj.damage, proj.knockBack, dir, false, false, proj.owner);
            }
        }

        /// <summary>ai[0] 是控制器槽位；控制器还活着就继续维持激光柱。</summary>
        private static bool ControllerAlive(Projectile proj)
        {
            var index = (int)proj.ai[0];
            if (index < 0 || index >= Main.projectile.Length) return false;

            var controller = Main.projectile[index];
            return controller != null
                && controller.active
                && controller.type == ModContent.ProjectileType<XiheMagicControler>();
        }

        private static void SpawnBeamDust(Projectile proj)
        {
            if (Main.rand.Next(2) != 0) return;

            var x = proj.Center.X + Main.rand.Next(-80, 81) * proj.scale;
            var y = Main.screenPosition.Y + Main.rand.Next(-500, Main.screenHeight + 400);
            var dust = Dust.NewDustDirect(new Vector2(x, y), 1, 1,
                Main.rand.Next(4) == 0 ? DustID.YellowStarDust : DustID.GoldFlame,
                0f, Main.rand.Next(5, 10), 0, default(Color), 1.2f);
            dust.noGravity = true;
        }

        public override bool CanDamage(Projectile proj) => proj.scale > 0.2f;

        public override bool? Colliding(Projectile proj, Rectangle projHitbox, Rectangle targetHitbox)
        {
            // 只判 X：整根柱子在竖直方向贯穿（MEAC 原版行为）。
            return Math.Abs(targetHitbox.Center.X - proj.Center.X) < proj.scale * 90f;
        }

        public override bool PreDraw(Projectile proj, Color lightColor) => false;

        public override void OnKill(Projectile proj, int timeLeft)
        {
            if (proj.owner != Main.myPlayer) return;

            for (var i = 0; i < 40; i++)
            {
                var pos = new Vector2(
                    proj.Center.X + Main.rand.Next(-90, 91) * Math.Max(proj.scale, 0.5f),
                    Main.screenPosition.Y + Main.rand.Next(Main.screenHeight));
                var dust = Dust.NewDustDirect(pos, 1, 1, DustID.GoldFlame,
                    Main.rand.NextFloat() * 8f - 4f, Main.rand.NextFloat() * 8f - 4f,
                    0, default(Color), 1.5f);
                dust.noGravity = true;
            }
        }
    }
}

