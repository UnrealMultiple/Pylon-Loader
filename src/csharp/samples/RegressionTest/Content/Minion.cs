using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace RegressionTest.Content
{
    /// <summary>
    /// 召唤物：右键/使用后生成仆从。仆从开 usesIDStaticNPCImmunity（P1-3 免疫二维表）。
    /// </summary>
    public sealed class SummonItem : ModItem
    {
        public override string DisplayName => "回归召唤杖";

        public override void SetDefaults(Item item)
        {
            item.width = 20;
            item.height = 20;
            item.summon = true;
            item.damage = 10;
            item.mana = 10;
            item.useTime = 30;
            item.useAnimation = 30;
            item.useStyle = (byte)ItemUseStyleID.Swing;
            item.noMelee = true;
            item.shoot = ModContent.ProjectileType<MinionProjectile>();
            item.shootSpeed = 1f;
            item.buffType = 0;
        }
    }

    /// <summary>仆从弹：悬停在主人头顶；usesIDStaticNPCImmunity = true。</summary>
    public sealed class MinionProjectile : ModProjectile
    {
        public override string DisplayName => "回归仆从";

        public override void SetDefaults(Projectile proj)
        {
            proj.width = 24;
            proj.height = 24;
            proj.aiStyle = -1;
            proj.friendly = true;
            proj.minion = true;
            proj.minionSlots = 0.5f;
            proj.penetrate = -1;
            proj.timeLeft = 18000;
            proj.tileCollide = false;
            proj.usesIDStaticNPCImmunity = true; // P1-3：perIDStaticNPCImmunity 二维表
            proj.ignoreWater = true;
            proj.netImportant = true;
        }

        public override void AI(Projectile proj)
        {
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

        public override bool? CanHitNPC(Projectile proj, NPC target) => target.friendly ? false : null;
    }
}
