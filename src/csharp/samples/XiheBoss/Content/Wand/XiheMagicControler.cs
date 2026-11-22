using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace XiheBoss.Wand
{

    public sealed class XiheMagicControler : ModProjectile
    {

        public override string TexturePath => null;

        private bool _inited;
        private bool _spawnedPillar;
        private Vector2 _skyTarget;

        public override void SetStaticDefaults()
        {
            Main.projFrames[Type] = 1;
        }

        public override void SetDefaults()
        {
            var proj = Projectile;
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

        public override void OnSpawn(Terraria.DataStructures.IEntitySource source)
        {
            var proj = Projectile;
            _inited = true;
            _spawnedPillar = false;

            var player = Main.player[proj.owner];
            _skyTarget = player != null
                ? new Vector2(Main.MouseWorld.X, player.position.Y - 1500f)
                : proj.Center;

            if (proj.owner == Main.myPlayer)
                XiheBossMod.Log?.Info($"[xihe] controller spawn dmg={proj.damage}");
        }

        public override void AI()
        {
            var proj = Projectile;
            var player = Main.player[proj.owner];
            if (player == null || !player.active)
            {
                proj.Kill();
                return;
            }

            if (!_inited) OnSpawn(null);

            var alpha = Math.Min(1f, proj.localAI[0] + 0.01f);

            proj.localAI[0] = alpha;

            Lighting.AddLight(proj.Center, 1.5f * alpha, 1.5f * alpha, 0.8f * alpha);

            if (proj.owner == Main.myPlayer)
                _skyTarget.X = MathHelper.Lerp(_skyTarget.X, Main.MouseWorld.X, 0.01f);
            _skyTarget.Y = player.position.Y - 1500f;

            proj.localAI[1] = _skyTarget.X;
            proj.localAI[2] = _skyTarget.Y;

            var chance = 12;
            if (proj.timeLeft < 300) chance = 5;
            if (proj.timeLeft < 190) chance = 2;
            if (Main.rand.Next(chance) == 0) SpawnMote(proj);

            if (!_spawnedPillar && proj.timeLeft <= 300)
            {
                _spawnedPillar = true;
                if (proj.owner == Main.myPlayer) SpawnPillar(proj, player);
            }

            if (!player.controlUseItem)
            {
                proj.Kill();
                return;
            }

            if (proj.timeLeft < 190)
            {
                var cost = (int)(10f * player.manaCost);
                if (cost < 1) cost = 1;

                if (player.controlUseItem && player.statMana >= cost)
                {
                    player.statMana -= cost;
                    proj.timeLeft++;
                }
                else
                {
                    proj.Kill();
                }
            }

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

            var source = player.GetProjectileSource_Item(player.HeldItem);
            Projectile.NewProjectile(source, Main.MouseWorld - new Vector2(0f, 300f), Vector2.Zero, type,
                proj.damage, 0f, proj.owner, proj.whoAmI, 0f, 0f);
        }

        private static void SpawnMote(Projectile proj)
        {
            var mote = XiheVisuals.Create(VfxKind.LightDust00, proj.Center, XiheHelper.NextVector2Circular(8f));
            mote.BloomColor = new Color(1f, 0.8f, 0.1f);
        }

        public override bool CanDamage() => false;

        public override bool PreDraw(Player player, ref Color lightColor) => false;

    }
}
