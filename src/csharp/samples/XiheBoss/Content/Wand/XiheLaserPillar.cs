using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Pylon.ModLoader.Audio;
using Terraria;
using Terraria.ID;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace XiheBoss.Wand
{

    public sealed class XiheLaserPillar : ModProjectile
    {

        public override string TexturePath => null;

        private bool _fired;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults()
        {
            var proj = Projectile;
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

        public override bool ShouldUpdatePosition() => false;

        public override void OnSpawn(Terraria.DataStructures.IEntitySource source)
        {
            var proj = Projectile;

            if (proj.damage <= 0) proj.damage = 200;
            proj.originalDamage = proj.damage;
            if (proj.owner == Main.myPlayer)
                XiheBossMod.Log?.Info($"[xihe] pillar spawn dmg={proj.damage} scale={proj.scale}");
        }

        public override void AI()
        {
            var proj = Projectile;
            proj.rotation = 1.57f;
            var localai = proj.localAI;
            localai[1] = 10000f;
            proj.localAI = localai;

            if (!_fired && proj.timeLeft <= 250)
            {

                _fired = true;
                XiheFx.RadialGlur(proj.Center, 60, true);
                SoundEngine.PlaySound(SoundID.Item159, proj.Center);
                XiheSounds.PlayLaser(proj.Center);
            }

            if (proj.timeLeft <= 340 && proj.timeLeft > 260)
                proj.scale += 0.002f;

            if (proj.timeLeft > 230 && proj.timeLeft < 250)
                proj.scale += 0.15f;

            if (proj.timeLeft < 245 && proj.timeLeft > 20)
            {

                XiheFx.ScreenShake(8f, proj.Center);

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

            if (proj.timeLeft < 20)
                proj.scale = Math.Max(0f, proj.scale - 0.15f);

            var light = Math.Min(1.5f, proj.scale * 0.5f);
            Lighting.AddLight(proj.Center, 1.2f * light, 0.95f * light, 0.4f * light);

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
            for (var i = 0; i < 2; i++)
            {
                var pos = new Vector2(
                    proj.Center.X + Main.rand.Next(-80, 81) * proj.scale,
                    Main.screenPosition.Y + Main.rand.Next(-500, 1200));
                var dust = XiheVisuals.Create(VfxKind.LightDust, pos, new Vector2(0f, Main.rand.Next(5, 10)));
                dust.DrawColor = Color.Yellow;
                if (XiheHelper.RandBool(3))
                {
                    var cn = XiheVisuals.Create(VfxKind.LightDustCn,
                        new Vector2(
                            proj.Center.X + Main.rand.Next(-70, 71) * proj.scale,
                            Main.screenPosition.Y + Main.rand.Next(-500, 1200)),
                        new Vector2(0f, Main.rand.Next(5, 10)));
                    cn.BloomColor = Color.Yellow;
                }
            }
        }

        public override bool CanDamage() => Projectile.scale > 0.2f;

        public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
        {
            var proj = Projectile;

            return Math.Abs(targetHitbox.Center.X - proj.Center.X) < proj.scale * 90f;
        }

        public override bool PreDraw(Player player, ref Color lightColor) => false;

    }
}
