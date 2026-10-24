using Pylon.ModLoader.Content;
using Pylon.ModLoader.Hooks;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.Textures;
using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader.Pipelines;
/// <summary>Projectile.SetDefaults 接管：自定义弹幕清出厂值后走作者 SetDefaults，原版弹幕照走原版并补一次 Global 分派。</summary>
internal sealed class ProjectileDefaults(ProjectileBinding binding)
{
    private Projectile? _factory;

    public bool Suppress { get; set; }

    public void Install() => On.Terraria.Projectile.SetDefaults += SetDefaults;

    public void CreateTemplates(IModLog log)
    {
        var count = 0;
        foreach (var type in ModContent.GetContent<ModProjectile>())
        {
            try
            {
                Suppress = true;
                try
                {
                    var template = new Projectile();
                    template.SetDefaults(type.Type);
                    type.Template = template;
                }
                finally
                {
                    Suppress = false;
                }

                type.AutoStaticDefaults();
                type.SetStaticDefaults();

                var whip = ProjectileID.Sets.IsAWhip;
                if (type.Plan.Has(HookId.Colliding) ||
                    whip is not null && type.Type >= 0 && type.Type < whip.Length && whip[type.Type])
                    ProjectileID.Sets.IsAComplexCollision[type.Type] = true;

                count++;
            }
            catch (Exception e)
            {
                log.Warn($"{type.Name} 弹幕模板初始化失败：{Safe.Describe(e)}");
            }
        }

        log.Info($"弹幕模板：{count}");
    }

    private void SetDefaults(On.Terraria.Projectile.orig_SetDefaults orig, Projectile self, int type)
    {
        if (!Suppress && type >= 0 && ModContent.Find(ContentKind.Projectile, type) is not null)
        {
            Custom(self, type);
            return;
        }

        orig(self, type);

        if (Suppress || type < 0 || ModContent.Find(ContentKind.Projectile, type) is not null) return;
        DispatchGlobals(self);
    }

    private void Custom(Projectile proj, int type)
    {
        try
        {
            proj.type = type;
            ResetToFactory(proj);

            var mod = binding.Rebind(proj);
            if (mod is not null)
            {
                mod.SetDefaults();
                Finish(proj, mod);
            }

            DispatchGlobals(proj);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"Projectile.SetDefaults 自定义分支异常：{e.Message}");
        }
    }

    private static void Finish(Projectile proj, ModProjectile mod)
    {
        proj.type = mod.Type;
        proj.active = true;

        var frames = 1;
        try
        {
            var table = Main.projFrames;
            if (table is not null && proj.type >= 0 && proj.type < table.Length && table[proj.type] > 0)
                frames = table[proj.type];
        }
        catch
        {
        }

        try
        {
            var texture = mod.Texture?.Value;
            if (texture is not null)
            {
                PylonTexture.Size(texture, out var textureWidth, out var textureHeight);
                if (proj.width <= 0) proj.width = textureWidth;
                if (proj.height <= 0) proj.height = textureHeight / frames;
            }
        }
        catch
        {
        }

        var scale = proj.scale <= 0 ? 1f : proj.scale;
        proj.width = (int)(proj.width * scale);
        proj.height = (int)(proj.height * scale);
        proj.maxPenetrate = proj.penetrate;
    }

    private static void DispatchGlobals(Projectile proj)
    {
        if (proj.type <= 0) return;
        if (GlobalProjectileRegistry.All.Count == 0) return;

        GlobalProjectileRegistry.Attach(proj, true);
        foreach (var global in GlobalProjectileRegistry.For(proj, HookId.SetDefaults)) global.SetDefaults(proj);
    }

    private void ResetToFactory(Projectile p)
    {
        var d = _factory ??= new Projectile();

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
    }
}
