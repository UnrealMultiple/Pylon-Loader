// 层：Mod 框架 / Core —— 弹幕的"干净初值"
// 自定义弹幕走自己的 SetDefaults 分支（跳过原版），槽位复用留下的旧值必须自己清掉，
// 否则上一发原版弹幕的 ai/timeLeft/friendly 会漏进来。持一个 new Projectile() 当"出厂值"，
// SetDefaults 先抄回这些字段再让 mod 覆盖；字段表与 ModProjectile.CloneDefaults 一致。
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>弹幕字段的出厂值与应用。</summary>
    internal static class ProjectileDefaults
    {
        private static Projectile _defaults;

        /// <summary>拿/建出厂值对象（只在游戏线程调）。</summary>
        private static Projectile Defaults => _defaults ??= new Projectile();

        /// <summary>把这些字段恢复成 new Projectile() 的出厂值（type/whoAmI/position 由调用方管）。</summary>
        internal static void Apply(Projectile p)
        {
            var d = Defaults;

            p.width = d.width;
            p.height = d.height;
            p.ownerHitCheckDistance = d.ownerHitCheckDistance;
            p.arrow = d.arrow;
            p.numHits = d.numHits;
            p.bobber = d.bobber;
            p.netImportant = d.netImportant;
            p.noDropItem = d.noDropItem;
            p.counterweight = d.counterweight;
            p.scale = d.scale;
            p.rotation = d.rotation;
            p.alpha = d.alpha;
            p.sentry = d.sentry;
            p.ai = d.ai;
            p.localAI = d.localAI;
            p.aiStyle = d.aiStyle;
            p.timeLeft = d.timeLeft;
            p.soundDelay = d.soundDelay;
            p.damage = d.damage;
            p.originalDamage = d.originalDamage;
            p.spriteDirection = d.spriteDirection;
            p.hostile = d.hostile;
            p.reflected = d.reflected;
            p.knockBack = d.knockBack;
            p.friendly = d.friendly;
            p.penetrate = d.penetrate;
            p.maxPenetrate = d.maxPenetrate;
            p.light = d.light;
            p.minion = d.minion;
            p.minionSlots = d.minionSlots;
            p.tileCollide = d.tileCollide;
            p.extraUpdates = d.extraUpdates;
            p.ignoreWater = d.ignoreWater;
            p.drawLayer = d.drawLayer;
            p.usesOwnerLight = d.usesOwnerLight;
            p.hide = d.hide;
            p.ownerHitCheck = d.ownerHitCheck;
            p.usesOwnerMeleeHitCD = d.usesOwnerMeleeHitCD;
            p.melee = d.melee;
            p.ranged = d.ranged;
            p.magic = d.magic;
            p.coldDamage = d.coldDamage;
            p.noEnchantments = d.noEnchantments;
            p.noEnchantmentVisuals = d.noEnchantmentVisuals;
            p.trap = d.trap;
            p.npcProj = d.npcProj;
            p.frameCounter = d.frameCounter;
            p.frame = d.frame;
            p.manualDirectionChange = d.manualDirectionChange;
            p.correctSlopeCollision = d.correctSlopeCollision;
            p.decidesManualFallThrough = d.decidesManualFallThrough;
            p.shouldFallThrough = d.shouldFallThrough;
            p.localNPCHitCooldown = d.localNPCHitCooldown;
            p.idStaticNPCHitCooldown = d.idStaticNPCHitCooldown;
            p.bannerIdToRespondTo = d.bannerIdToRespondTo;
            p.tagEffectType = d.tagEffectType;
            p.bonusTagDamage = d.bonusTagDamage;
            p.armorPenetration = d.armorPenetration;
            p.bonusCritChance = d.bonusCritChance;
            p.stopsDealingDamageAfterPenetrateHits = d.stopsDealingDamageAfterPenetrateHits;
            p.usesIDStaticNPCImmunity = d.usesIDStaticNPCImmunity;
            p.usesLocalNPCImmunity = d.usesLocalNPCImmunity;
            p.appliesImmunityTimeOnSingleHits = d.appliesImmunityTimeOnSingleHits;
            // owner 不在这里清：NewProjectile 可能先写 owner 再调 SetDefaults，清掉会把弹幕算到 0 号玩家头上。
        }
    }
}
