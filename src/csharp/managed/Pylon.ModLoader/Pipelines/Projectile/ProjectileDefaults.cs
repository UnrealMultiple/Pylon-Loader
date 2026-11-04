using Pylon.ModLoader.Textures;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// Projectile.SetDefaults 接管：自定义弹幕不调原版（原版对未知 type 会把弹幕判死），
/// 改成"清出厂值 → 作者 SetDefaults → 收尾"，原版弹幕照走原版并补一次 Global 分派。
/// </summary>
internal sealed class ProjectileDefaults(ProjectileBinding binding)
{
    private Projectile? _factory;

    /// <summary>安装期造模板时置位：只填原版字段，不调作者的 mod 分支。</summary>
    public bool Suppress { get; set; }

    public void Install() => On.Terraria.Projectile.SetDefaults += SetDefaults;

    /// <summary>每个自定义弹幕造一份模板：SetStaticDefaults 要在真实 Projectile 上写帧数、hostile 等。</summary>
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
                type.PostStaticDefaults();
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
                mod.SetDefaults(proj);
                mod.PostSetDefaults(proj);
                Finish(proj, mod);
            }

            DispatchGlobals(proj);
        }
        catch (Exception e)
        {
            ContentErrors.Report($"Projectile.SetDefaults 自定义分支异常：{e.Message}");
        }
    }

    /// <summary>原版 SetDefaults 末尾那几步：用作者的值重做一遍，否则尺寸、穿透、active 都不对。</summary>
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
            // 表还没建好：按单帧算，下面的尺寸推断只是尽力而为。
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
            // 贴图还没上传：留 0 让游戏兜底。
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

        // 每次 SetDefaults 都重建实体级列表：槽位复用不能继承上一发的 AppliesToEntity 结果。
        GlobalProjectileRegistry.Attach(proj, true);
        foreach (var global in GlobalProjectileRegistry.For(proj, HookId.SetDefaults)) global.SetDefaults(proj);
    }

    /// <summary>把这些字段恢复成 <c>new Projectile()</c> 的出厂值；type / whoAmI / position 由调用方管。</summary>
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
        // owner 不清：NewProjectile 可能先写 owner 再调 SetDefaults，清掉会把弹幕挂到 0 号玩家身上。
    }
}
