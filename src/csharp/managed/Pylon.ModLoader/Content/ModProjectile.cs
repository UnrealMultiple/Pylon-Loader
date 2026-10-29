using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>自定义弹幕（投射物）：一个实例 = 一个自定义弹幕，钩子由 Harmony 层按类型派发。</summary>
    public abstract class ModProjectile : ModType
    {
        /// <summary>贴图相对 Mod 目录的路径。</summary>
        public override string TexturePath => $"assets/projectiles/{Name}.png";

        /// <summary>显示名（本地化接入后由 `Lang` 钩子接管；这里给默认值）。</summary>
        public virtual string DisplayName => "";

        /// <summary>用这个原版弹幕的 AI（只在 AI 期间换类型）；0 = 关。</summary>
        public int AIType;

        /// <summary>钩子的 AI 风格（本版没有 ProjAIStyleID.Hook 常量，取它的值 7）。</summary>
        public const int HookAIStyle = 7;

        /// <summary>模板弹幕：只在 SetStaticDefaults 期间由加载器填写，其余时候为 null（对应参考的 `this.Projectile`）。</summary>
        public Projectile Template { get; internal set; }

        /// <summary>当前绑定的游戏实体（SetStaticDefaults 期间为 null，那时用 <see cref="Template"/>）。</summary>
        public Projectile Projectile => BoundEntity;

        /// <summary>实体实例绑定的游戏弹幕（框架维护）。</summary>
        internal Projectile BoundEntity { get; set; }

        /// <summary>OnSpawn 是否已经跑过（每发弹幕一次；框架维护）。</summary>
        internal bool Spawned { get; set; }

        /// <summary>把这个原版弹幕类型的关键字段抄到当前弹幕上（等于 tModLoader 的 CloneDefaults）。</summary>
        public void CloneDefaults(int type)
        {
            var proj = BoundEntity;
            if (proj == null) return;

            var source = new Projectile();
            source.SetDefaults(type);

            proj.width = source.width;
            proj.height = source.height;
            proj.ownerHitCheckDistance = source.ownerHitCheckDistance;
            proj.arrow = source.arrow;
            proj.numHits = source.numHits;
            proj.bobber = source.bobber;
            proj.netImportant = source.netImportant;
            proj.noDropItem = source.noDropItem;
            proj.counterweight = source.counterweight;
            proj.scale = source.scale;
            proj.rotation = source.rotation;
            proj.alpha = source.alpha;
            proj.sentry = source.sentry;
            proj.ai = source.ai;
            proj.localAI = source.localAI;
            proj.aiStyle = source.aiStyle;
            proj.timeLeft = source.timeLeft;
            proj.soundDelay = source.soundDelay;
            proj.damage = source.damage;
            proj.originalDamage = source.originalDamage;
            proj.spriteDirection = source.spriteDirection;
            proj.hostile = source.hostile;
            proj.reflected = source.reflected;
            proj.knockBack = source.knockBack;
            proj.friendly = source.friendly;
            proj.penetrate = source.penetrate;
            proj.maxPenetrate = source.maxPenetrate;
            proj.light = source.light;
            proj.minion = source.minion;
            proj.minionSlots = source.minionSlots;
            proj.tileCollide = source.tileCollide;
            proj.extraUpdates = source.extraUpdates;
            proj.ignoreWater = source.ignoreWater;
            proj.drawLayer = source.drawLayer;
            proj.usesOwnerLight = source.usesOwnerLight;
            proj.hide = source.hide;
            proj.ownerHitCheck = source.ownerHitCheck;
            proj.usesOwnerMeleeHitCD = source.usesOwnerMeleeHitCD;
            proj.melee = source.melee;
            proj.ranged = source.ranged;
            proj.magic = source.magic;
            proj.coldDamage = source.coldDamage;
            proj.noEnchantments = source.noEnchantments;
            proj.noEnchantmentVisuals = source.noEnchantmentVisuals;
            proj.trap = source.trap;
            proj.npcProj = source.npcProj;
            proj.frameCounter = source.frameCounter;
            proj.frame = source.frame;
            proj.manualDirectionChange = source.manualDirectionChange;
            proj.correctSlopeCollision = source.correctSlopeCollision;
            proj.decidesManualFallThrough = source.decidesManualFallThrough;
            proj.shouldFallThrough = source.shouldFallThrough;
            proj.localNPCHitCooldown = source.localNPCHitCooldown;
            proj.idStaticNPCHitCooldown = source.idStaticNPCHitCooldown;
            proj.bannerIdToRespondTo = source.bannerIdToRespondTo;
            proj.tagEffectType = source.tagEffectType;
            proj.bonusTagDamage = source.bonusTagDamage;
            proj.armorPenetration = source.armorPenetration;
            proj.bonusCritChance = source.bonusCritChance;
            proj.stopsDealingDamageAfterPenetrateHits = source.stopsDealingDamageAfterPenetrateHits;
            proj.maxPenetrate = source.maxPenetrate;
            proj.usesIDStaticNPCImmunity = source.usesIDStaticNPCImmunity;
            proj.usesLocalNPCImmunity = source.usesLocalNPCImmunity;
            proj.appliesImmunityTimeOnSingleHits = source.appliesImmunityTimeOnSingleHits;
        }

        /// <summary>基类先填 `Main.projHostile`/`Main.projHook`；覆写时要自己填。</summary>
        public virtual void AutoStaticDefaults()
        {
            var template = Template;
            if (template == null) return;

            if (template.hostile) Main.projHostile[Type] = true;
            if (template.aiStyle == HookAIStyle) Main.projHook[Type] = true;
        }

        /// <summary>只放共享数值（帧数等），不要碰游戏对象。</summary>
        public override void SetStaticDefaults() { }

        /// <summary>这个弹幕的默认值；每个新生成的弹幕调一次。</summary>
        public virtual void SetDefaults(Projectile proj) { }

        /// <summary>SetStaticDefaults 之后一次。</summary>
        public virtual void PostStaticDefaults() { }

        /// <summary>SetDefaults 之后，每次生成。</summary>
        public virtual void PostSetDefaults(Projectile proj) { }

        /// <summary>所有内容安装完成后一次。</summary>
        public virtual void PostSetupContent() { }

        /// <summary>生成后的第一帧一次（第一帧标志由 Harmony 层维护）。</summary>
        public virtual void OnSpawn(Projectile proj) { }

        /// <summary>AI 之前；返回 false 跳过游戏 AI 与 <see cref="AI"/>。</summary>
        public virtual bool PreAI(Projectile proj) => true;

        /// <summary>自己的每帧 AI。</summary>
        public virtual void AI(Projectile proj) { }

        /// <summary>AI 之后。</summary>
        public virtual void PostAI(Projectile proj) { }

        /// <summary>原生弹幕同步包发出后，把本弹幕的附加数据写进去。</summary>
        public virtual void SendExtraAI(Projectile proj, NetWriter writer) { }

        /// <summary>收到本弹幕的附加数据。</summary>
        public virtual void ReceiveExtraAI(Projectile proj, NetReader reader) { }

        /// <summary>死亡前；返回 false 去掉游戏的死亡特效（弹幕照样死）。</summary>
        public virtual bool PreKill(Projectile proj, int timeLeft) => true;

        /// <summary>死亡时（尘土、声音、碎片）。</summary>
        public virtual void OnKill(Projectile proj, int timeLeft) { }

        /// <summary>撞方块前的碰撞盒 / 是否穿平台 / 盒心；返回 false 本帧不碰方块。</summary>
        public virtual bool TileCollideStyle(Projectile proj, ref int width, ref int height, ref bool fallThrough, ref Vector2 hitboxCenterFrac) => true;

        /// <summary>撞到方块；返回 false 这次撞击不死（改 proj.velocity 用来弹开）。</summary>
        public virtual bool OnTileCollide(Projectile proj, Vector2 oldVelocity) => true;

        /// <summary>null = 交给游戏判断（原版对玩家弹幕的规则）。</summary>
        public virtual bool? CanHitNPC(Projectile proj, NPC target) => null;

        /// <summary>false 否决对玩家的 PvP 命中。</summary>
        public virtual bool CanHitPvp(Projectile proj, Player target) => true;

        /// <summary>false 否决对玩家的命中。</summary>
        public virtual bool CanHitPlayer(Projectile proj, Player target) => true;

        /// <summary>true/false 决定是否命中；null = 用游戏的矩形相交。</summary>
        public virtual bool? Colliding(Projectile proj, Rectangle projHitbox, Rectangle targetHitbox) => null;

        /// <summary>false = 这个弹幕不造成伤害。</summary>
        public virtual bool CanDamage(Projectile proj) => true;

        /// <summary>true = 仆从/宠物贴身也造成伤害。</summary>
        public virtual bool MinionContactDamage(Projectile proj) => false;

        /// <summary>改写造成伤害用的区域。</summary>
        public virtual void ModifyDamageHitbox(Projectile proj, ref Rectangle hitbox) { }

        /// <summary>null = 用游戏的判断。</summary>
        public virtual bool? CanCutTiles(Projectile proj) => null;

        /// <summary>割草、砍蛛网。</summary>
        public virtual void CutTiles(Projectile proj) { }

        /// <summary>最终颜色；null = 用游戏的。</summary>
        public virtual Color? GetAlpha(Projectile proj, Color lightColor) => null;

        /// <summary>画游戏那部分"附加物"（鱼线、钩爪链条、链锤铁链、悠悠球绳）之前；返回 false 连附加物一起不画（手机版画在 DrawProjDirect 里）。</summary>
        public virtual bool PreDrawExtras(Projectile proj) => true;

        /// <summary>画之前；返回 false 不画游戏的那张精灵（游戏画的附加部分留下）。</summary>
        public virtual bool PreDraw(Projectile proj, Color lightColor) => true;

        /// <summary>画之后（PreDraw 返回 false 也调）。</summary>
        public virtual void PostDraw(Projectile proj, Color lightColor) { }

        /// <summary>在模板上、发射钩子之前；false 不让发射。</summary>
        public virtual bool CanUseGrapple(Player player, int type) => true;

        /// <summary>在模板上：返回要发射的类型。</summary>
        public virtual int UseGrapple(Player player, int type) => type;

        /// <summary>true/false = 能否挂到这个方块上；null = 用游戏的（实心方块）。</summary>
        public virtual bool? GrappleCanLatchOnTo(Projectile proj, Player player, Tile tile) => null;

        /// <summary>钩子（aiStyle 7）的射程，单位像素；超过就往回。</summary>
        public virtual float GrappleRange(Projectile proj) => 300;

        /// <summary>同时能挂住几个；超了最老的松开。</summary>
        public virtual void NumGrappleHooks(Projectile proj, Player player, ref int numHooks) { }

        /// <summary>收回来时的速度。</summary>
        public virtual void GrappleRetreatSpeed(Projectile proj, Player player, ref float speed) { }

        /// <summary>挂住后把玩家拉过去的速度。</summary>
        public virtual void GrapplePullSpeed(Projectile proj, Player player, ref float speed) { }

        /// <summary>钩子拉向的点（已挂住的钩子中心之和）。</summary>
        public virtual void GrappleTargetPoint(Projectile proj, Player player, ref float grappleX, ref float grappleY) { }

        /// <summary>false = 游戏不把速度加到位置上（位置由 AI 自己写）。</summary>
        public virtual bool ShouldUpdatePosition(Projectile proj) => true;

        /// <summary>游戏给长矛类手持弹幕的默认值。</summary>
        public virtual void DefaultToSpear(Projectile proj) => proj.DefaultToSpear();

        /// <summary>游戏给悠悠球类手持弹幕的默认值。</summary>
        public virtual void DefaultToYoyo(Projectile proj) => proj.DefaultToYoyo();

        /// <summary>游戏给链锤类手持弹幕的默认值。</summary>
        public virtual void DefaultToFlail(Projectile proj) => proj.DefaultToFlail();

        /// <summary>游戏给鞭子类手持弹幕的默认值。</summary>
        public virtual void DefaultToWhip(Projectile proj) => proj.DefaultToWhip();

        /// <summary>游戏给钻头/电锯类手持弹幕的默认值。</summary>
        public virtual void DefaultToDrillOrChainsaw(Projectile proj) => proj.DefaultToDrillOrChainsaw();

        /// <summary>游戏给风筝类手持弹幕的默认值。</summary>
        public virtual void DefaultToKite(Projectile proj) => proj.DefaultToKite();
    }
}
