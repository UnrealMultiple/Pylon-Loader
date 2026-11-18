using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace RegressionTest.Content
{
    /// <summary>
    /// 召唤物：右键/使用后生成仆从。仆从开 usesIDStaticNPCImmunity（P1-3 免疫二维表）。
    /// </summary>
    public sealed class SummonItem : ModItem
    {
        public override string DisplayName => "回归召唤杖";

        public override void SetDefaults()
        {
            Item.width = 20;
            Item.height = 20;
            Item.summon = true;
            Item.damage = 10;
            Item.mana = 10;
            Item.useTime = 30;
            Item.useAnimation = 30;
            Item.useStyle = (byte)ItemUseStyleID.Swing;
            Item.noMelee = true;
            Item.shoot = ModContent.ProjectileType<MinionProjectile>();
            Item.shootSpeed = 1f;
            Item.buffType = 0;
        }
    }

    /// <summary>仆从弹：悬停在主人头顶；usesIDStaticNPCImmunity = true。</summary>
    public sealed class MinionProjectile : ModProjectile
    {
        public override string DisplayName => "回归仆从";

        public override void SetDefaults()
        {
            Projectile.width = 24;
            Projectile.height = 24;
            Projectile.aiStyle = -1;
            Projectile.friendly = true;
            Projectile.minion = true;
            Projectile.minionSlots = 0.5f;
            Projectile.penetrate = -1;
            Projectile.timeLeft = 18000;
            Projectile.tileCollide = false;
            Projectile.usesIDStaticNPCImmunity = true; // P1-3：perIDStaticNPCImmunity 二维表
            Projectile.ignoreWater = true;
            Projectile.netImportant = true;
        }

        public override void AI()
        {
            var proj = Projectile;
            var owner = Main.player[proj.owner];
            if (owner == null || !owner.active)
            {
                proj.Kill();
                return;
            }

            // 悬在主人头顶一点，慢慢飘
            var target = owner.Center + new Vector2(0f, -48f + (float)Math.Sin(Main.GameUpdateCount / 30.0) * 8f);
            var delta = target - proj.Center;
            proj.velocity = delta * 0.08f;

            proj.frameCounter++;
            if (Main.myPlayer == proj.owner && proj.frameCounter % 60 == 0)
                RegressionTestMod.Log?.Info($"[rt] 仆从在岗 type={proj.type} usesIDStatic={proj.usesIDStaticNPCImmunity}");
        }

        public override bool? CanHitNPC(NPC target) => target.friendly ? false : null;
    }
}
