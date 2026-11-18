using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Contracts;
using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader.Content;
/// <summary>自定义弹幕（投射物）：一个实例 = 一个自定义弹幕，钩子由 Harmony 层按类型派发。</summary>
public abstract class ModProjectile : ModType
{
    /// <summary>贴图相对 Mod 目录的路径。</summary>
    public override string TexturePath => $"assets/projectiles/{Name}.png";

    /// <summary>显示名（本地化接入后由 `Lang` 钩子接管；这里给默认值）。</summary>
    public virtual string DisplayName => "";

    /// <summary>本地化键的分类段（Mods.&lt;Mod&gt;.&lt;分类&gt;.&lt;Name&gt;.*）。</summary>
    public virtual string LocalizationCategory => "Projectiles";

    /// <summary>弹幕命中的免疫冷却槽（-1 = 不指定）；本构建没有消费点，仅保留声明。</summary>
    public int CooldownSlot { get; set; } = -1;

    /// <summary>发光贴图路径约定（&lt;贴图&gt;_Glow）；本构建没有消费点，仅保留声明。</summary>
    public virtual string GlowTexture => TextureSibling("_Glow");

    /// <summary>用这个原版弹幕的 AI（只在 AI 期间换类型）；0 = 关。</summary>
    public int AIType;

    /// <summary>钩子的 AI 风格（本版没有 ProjAIStyleID.Hook 常量，取它的值 7）。</summary>
    internal const int HookAIStyle = 7;

    public Projectile Template { get; internal set; } = null!;

    /// <summary>当前绑定的游戏实体（SetStaticDefaults 期间为 null，那时用 <see cref="Template"/>）。</summary>
    public Projectile Projectile => BoundEntity;

    /// <summary>实体实例绑定的游戏弹幕（框架维护）。</summary>
    internal Projectile BoundEntity { get; set; } = null!;

    /// <summary>OnSpawn 是否已经跑过（每发弹幕一次；框架维护）。</summary>
    internal bool Spawned { get; set; }

    /// <summary>基类先填 `Main.projFrames`/`Main.projHostile`/`Main.projHook`；覆写时要自己填。</summary>
    public virtual void AutoStaticDefaults()
    {
        var frames = Main.projFrames;
        if (frames is not null && Type >= 0 && Type < frames.Length) frames[Type] = 1;

        var template = Template;
        if (template == null) return;

        if (template.hostile) Main.projHostile[Type] = true;
        if (template.aiStyle == HookAIStyle) Main.projHook[Type] = true;
    }

    /// <summary>只放共享数值（帧数等），不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>这个弹幕的默认值；每个新生成的弹幕调一次（写进 <see cref="Projectile"/>）。</summary>
    public virtual void SetDefaults() { }

    /// <summary>生成后一次；source 是生成来源（两个 NewProjectile 重载里是真 source，其余路径给 null）。</summary>
    public virtual void OnSpawn(IEntitySource? source) { }

    /// <summary>AI 之前；返回 false 跳过游戏 AI 与 <see cref="AI"/>。</summary>
    public virtual bool PreAI() => true;

    /// <summary>修改浮标鱼线的竿端偏移（像素）与染色前颜色；之后由手持鱼竿继续修改。</summary>
    public virtual void ModifyFishingLine(ref Vector2 lineOriginOffset, ref Color lineColor) { }

    /// <summary>自己的每帧 AI。</summary>
    public virtual void AI() { }

    /// <summary>AI 之后。</summary>
    public virtual void PostAI() { }

    /// <summary>原生弹幕同步包发出后，把本弹幕的附加数据写进去。</summary>
    public virtual void SendExtraAI(NetWriter writer) { }

    /// <summary>收到本弹幕的附加数据。</summary>
    public virtual void ReceiveExtraAI(NetReader reader) { }

    /// <summary>死亡前；返回 false 去掉游戏的死亡特效（弹幕照样死）。</summary>
    public virtual bool PreKill(int timeLeft) => true;

    /// <summary>死亡时（尘土、声音、碎片）。</summary>
    public virtual void OnKill(int timeLeft) { }

    /// <summary>撞方块前的碰撞盒 / 是否穿平台 / 盒心；返回 false 本帧不碰方块。</summary>
    public virtual bool TileCollideStyle(ref int width, ref int height, ref bool fallThrough, ref Vector2 hitboxCenterFrac) => true;

    /// <summary>撞到方块；返回 false 这次撞击不死（改 <see cref="Projectile"/> 的 velocity 用来弹开）。</summary>
    public virtual bool OnTileCollide(Vector2 oldVelocity) => true;

    /// <summary>null = 交给游戏判断（原版对玩家弹幕的规则）。</summary>
    public virtual bool? CanHitNPC(NPC target) => null;

    /// <summary>目标受击修饰之后、所属玩家修饰之前修改本次命中。</summary>
    public virtual void ModifyHitNPC(NPC target, ref HitModifiers modifiers) { }

    /// <summary>命中后收到实际扣血量；hit.Damage 保留原版受击结果。</summary>
    public virtual void OnHitNPC(NPC target, HitInfo hit, int damageDone) { }

    /// <summary>false 否决对玩家的 PvP 命中。</summary>
    public virtual bool CanHitPvp(Player target) => true;

    /// <summary>false 否决对玩家的命中。</summary>
    public virtual bool CanHitPlayer(Player target) => true;

    /// <summary>来源侧修饰先于目标玩家的来源与通用受伤修饰。</summary>
    public virtual void ModifyHitPlayer(Player target, ref HurtModifiers modifiers) { }

    /// <summary>原版实际造成正伤害后的来源通知。</summary>
    public virtual void OnHitPlayer(Player target, HurtInfo hurtInfo) { }

    /// <summary>true/false 决定是否命中；null = 用游戏的矩形相交。</summary>
    public virtual bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox) => null;

    /// <summary>false = 这个弹幕不造成伤害。</summary>
    public virtual bool CanDamage() => true;

    /// <summary>true = 仆从/宠物贴身也造成伤害。</summary>
    public virtual bool MinionContactDamage() => false;

    /// <summary>改写造成伤害用的区域。</summary>
    public virtual void ModifyDamageHitbox(ref Rectangle hitbox) { }

    /// <summary>null = 用游戏的判断。</summary>
    public virtual bool? CanCutTiles() => null;

    /// <summary>割草、砍蛛网。</summary>
    public virtual void CutTiles() { }

    /// <summary>最终颜色；null = 用游戏的。</summary>
    public virtual Color? GetAlpha(Color lightColor) => null;

    /// <summary>画游戏那部分"附加物"（鱼线、钩爪链条、链锤铁链、悠悠球绳）之前；返回 false 连附加物一起不画（手机版画在 DrawProjDirect 里）。收弹幕所有者。</summary>
    public virtual bool PreDrawExtras(Player player) => true;

    /// <summary>画之前；返回 false 不画游戏的那张精灵（游戏画的附加部分留下）。player 是弹幕所有者，lightColor 可写。</summary>
    public virtual bool PreDraw(Player player, ref Color lightColor) => true;

    /// <summary>画之后（PreDraw 返回 false 也调）。</summary>
    public virtual void PostDraw(Player player, Color lightColor) { }

    /// <summary>在模板上、发射钩子之前；false 不让发射，null = 交给游戏。</summary>
    public virtual bool? CanUseGrapple(Player player) => null;

    /// <summary>在模板上：往 type 写要发射的弹幕类型。</summary>
    public virtual void UseGrapple(Player player, ref int type) { }

    /// <summary>true/false = 能否挂到 (x, y) 这格上；null = 用游戏的（实心方块）。</summary>
    public virtual bool? GrappleCanLatchOnTo(Player player, int x, int y) => null;

    /// <summary>钩子（aiStyle 7）的射程，单位像素；超过就往回。</summary>
    public virtual float GrappleRange() => 300f;

    /// <summary>同时能挂住几个；超了最老的松开。</summary>
    public virtual void NumGrappleHooks(Player player, ref int numHooks) { }

    /// <summary>收回来时的速度。</summary>
    public virtual void GrappleRetreatSpeed(Player player, ref float speed) { }

    /// <summary>挂住后把玩家拉过去的速度。</summary>
    public virtual void GrapplePullSpeed(Player player, ref float speed) { }

    /// <summary>钩子拉向的点（已挂住的钩子中心之和）。</summary>
    public virtual void GrappleTargetPoint(Player player, ref float grappleX, ref float grappleY) { }

    /// <summary>false = 游戏不把速度加到位置上（位置由 AI 自己写）。</summary>
    public virtual bool ShouldUpdatePosition() => true;

    public int DrawOffsetX { get; set; }

    public int DrawOriginOffsetY { get; set; }

    public float DrawOriginOffsetX { get; set; }
}