using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>全局弹幕：对游戏自己的弹幕（和 mod 的）也跑的一套钩子；一个类一份模板，AppliesToEntity 挑哪些弹幕。</summary>
public abstract class GlobalProjectile : ModType
{
    /// <summary>true：每个弹幕一份自己的副本（SetDefaults 时建）。</summary>
    public virtual bool InstancePerEntity => false;

    /// <summary>false：这套钩子不跑在这个弹幕上；lateInstantiation = 在 SetDefaults 之后（类型已定）。</summary>
    public virtual bool AppliesToEntity(Projectile entity, bool lateInstantiation) => true;

    /// <summary>复制这套钩子实例的字段；弹幕流程里由游戏克隆时调（管线接管）。</summary>
    public virtual GlobalProjectile Clone(Projectile from, Projectile to) => this;

    /// <summary>新弹幕的副本；默认就是模板自己。</summary>
    public virtual GlobalProjectile NewInstance(Projectile target) => this;

    /// <summary>弹幕生成时（网络的另一头也跑）。</summary>
    public virtual void SetDefaults(Projectile proj) { }

    /// <summary>NewProjectile 造出来时，在造它的那一侧；source 是这一发的来源。</summary>
    public virtual void OnSpawn(Projectile proj, IEntitySource source) { }

    /// <summary>每帧 AI 之前；false 跳过游戏 AI 与 <see cref="AI"/>（<see cref="PostAI"/> 照跑）。</summary>
    public virtual bool PreAI(Projectile proj) => true;

    /// <summary>每帧。</summary>
    public virtual void AI(Projectile proj) { }

    /// <summary>每帧，AI 之后。</summary>
    public virtual void PostAI(Projectile proj) { }

    /// <summary>死亡前；false 去掉原生死亡特效，之后不再调 <see cref="OnKill"/>。</summary>
    public virtual bool PreKill(Projectile proj, int timeLeft) => true;

    /// <summary>原生死亡之后，PreKill 没否决时（尘土、声音、碎片）。</summary>
    public virtual void OnKill(Projectile proj, int timeLeft) { }

    /// <summary>打 NPC；null = 交给游戏判断（原版对玩家弹幕的规则）。</summary>
    public virtual bool? CanHitNPC(Projectile proj, NPC target) => null;

    /// <summary>false 否决对玩家的 PvP 命中。</summary>
    public virtual bool CanHitPvp(Projectile proj, Player target) => true;

    /// <summary>false 否决对玩家的命中（敌意弹幕）。</summary>
    public virtual bool CanHitPlayer(Projectile proj, Player target) => true;

    /// <summary>画之前；lightColor 是可写引用，false 不画游戏的那张精灵（游戏画的附加部分留下）。</summary>
    public virtual bool PreDraw(Projectile proj, ref Color lightColor) => true;

    /// <summary>画之后，PreDraw 否决了也跑；lightColor 是直接的颜色，不是引用。</summary>
    public virtual void PostDraw(Projectile proj, Color lightColor) { }

    /// <summary>第一个返回颜色的 Global 说了算；null = 问本地方法和游戏。</summary>
    public virtual Color? GetAlpha(Projectile proj, Color lightColor) => null;

    /// <summary>第一个返回布尔的 Global 说了算；null = 问本地方法和游戏（矩形相交）。</summary>
    public virtual bool? Colliding(Projectile proj, Rectangle projHitbox, Rectangle targetHitbox) => null;

    /// <summary>false 否决伤害与割草；true 盖过本地方法；null = 交给游戏（原生条件照样生效）。</summary>
    public virtual bool? CanDamage(Projectile proj) => null;

    /// <summary>造成伤害用的区域，在本地方法之后（可写引用，改字段或换矩形都行）。</summary>
    public virtual void ModifyDamageHitbox(Projectile proj, ref Rectangle hitbox) { }

    /// <summary>撞到方块；false 这次撞击不死（本地方法也不跑），改 proj.velocity 用来弹开。</summary>
    public virtual bool OnTileCollide(Projectile proj, Vector2 oldVelocity) => true;

    /// <summary>撞方块前的碰撞盒 / 是否穿平台 / 盒心；四个都是可写引用，第一个 false 本帧不碰方块。</summary>
    public virtual bool TileCollideStyle(Projectile proj, ref int width, ref int height, ref bool fallThrough,
                                         ref Vector2 hitboxCenterFrac) => true;

    /// <summary>true：算宠物的弹幕贴身也造成伤害（问本地方法，再问到一个 true 为止）。</summary>
    public virtual bool MinionContactDamage(Projectile proj) => false;

    /// <summary>第一个返回布尔的 Global 说了算；false 还挡掉割草的钩子；null = 问本地方法和游戏。</summary>
    public virtual bool? CanCutTiles(Projectile proj) => null;

    /// <summary>割草、砍蛛网；Globals 先跑，再本地方法和默认的割草。</summary>
    public virtual void CutTiles(Projectile proj) { }

    /// <summary>在模板上、发射钩子之前（参数顺序和 ModProjectile 的相反）；最后一个有答案的 Global 说了算，null = 不管。</summary>
    public virtual bool? CanUseGrapple(int type, Player player) => null;

    /// <summary>在模板上、本地方法之后：type 是可写引用，要换发射的弹幕类型就改它。</summary>
    public virtual void UseGrapple(Player player, ref int type) { }

    /// <summary>false 否决挂到这个方块上，true 允许；null = 保持本地或原生的判断（收到的是方块的实体）。</summary>
    public virtual bool? GrappleCanLatchOnTo(Projectile proj, Player player, Tile tile) => null;

    /// <summary>false = 游戏不把速度加到位置上（位置由 AI 自己写），在本地方法之后，一个 false 就够。</summary>
    public virtual bool ShouldUpdatePosition(Projectile proj) => true;

    /// <summary>原生弹幕同步包发出后，把全局附加数据写进去。</summary>
    public virtual void NetSend(Projectile proj, NetWriter writer) { }

    /// <summary>收到全局附加数据。</summary>
    public virtual void NetReceive(Projectile proj, NetReader reader) { }
}

// 未移植：ModifyHitNPC/OnHitNPC、ModifyHitPlayer/OnHitPlayer（要 HitModifiers/HitInfo/HurtModifiers/HurtInfo，
// 本版 Projectile.Damage_PVE 与 NPC.StrikeNPC 仍是 int/float 参数）；register（走自动发现 + ModContent）。
