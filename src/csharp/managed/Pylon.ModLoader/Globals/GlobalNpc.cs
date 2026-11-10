using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.ItemDropRules;

namespace Pylon.ModLoader;

/// <summary>全局 NPC：对游戏自己的 NPC（和 mod 的）也跑的一套钩子；一个类一份模板，AppliesToEntity 挑哪些 NPC。</summary>
public abstract class GlobalNpc : ModType
{
    /// <summary>true：每个 NPC 一份自己的副本（存 per-entity 状态时用）。</summary>
    public virtual bool InstancePerEntity => false;

    /// <summary>false：这套钩子不跑在这个 NPC 上；lateInstantiation = 在 SetDefaults 之后（类型已定）。</summary>
    public virtual bool AppliesToEntity(NPC entity, bool lateInstantiation) => true;

    /// <summary>新 NPC 的副本；默认就是模板自己（InstancePerEntity 时覆写它）。</summary>
    public virtual GlobalNpc NewInstance(NPC target) => this;

    /// <summary>复制这套钩子实例的字段；默认返回自己。</summary>
    public virtual GlobalNpc Clone(NPC from, NPC to) => this;

    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>全部内容装完之后一次；配方的分组（跨 mod 引用放这里）。</summary>
    public virtual void AddRecipeGroups() { }

    /// <summary>全部内容装完之后一次。</summary>
    public virtual void AddRecipes() { }

    /// <summary>全部内容装完之后一次。</summary>
    public virtual void PostSetupContent() { }

    /// <summary>这个类型的 NPC 生成时（客户端收到同步数据、重建时也会）。</summary>
    public virtual void SetDefaults(NPC npc) { }

    /// <summary>生成后一次，收得到原生的生成来源；只在服务端或单人跑。</summary>
    public virtual void OnSpawn(NPC npc, IEntitySource source) { }

    /// <summary>NPC 的更新开始时。</summary>
    public virtual void ResetEffects(NPC npc) { }

    /// <summary>每帧；false 跳过游戏 AI 与所有 Global 的 AI。</summary>
    public virtual bool PreAI(NPC npc) => true;

    /// <summary>每帧（每个 NPC 每帧都进，最贵的钩子）。</summary>
    public virtual void AI(NPC npc) { }

    /// <summary>每帧，AI 之后；PreAI 否决了也跑。</summary>
    public virtual void PostAI(NPC npc) { }

    /// <summary>每次挨打、在游戏自己的效果之后；<c>npc.life &lt;= 0</c> 是致命的这击。</summary>
    public virtual void HitEffect(NPC npc, int hitDirection, double damage) { }

    /// <summary>接触玩家前查询；可修改本次使用的受伤冷却槽。</summary>
    public virtual bool CanHitPlayer(NPC npc, Player target, ref int cooldownSlot) => true;

    /// <summary>NPC 接触攻击造成伤害之前的来源修饰。</summary>
    public virtual void ModifyHitNPC(NPC npc, NPC target, ref HitModifiers modifiers) { }

    /// <summary>NPC 接触攻击实际造成正伤害后的通知。</summary>
    public virtual void OnHitNPC(NPC npc, NPC target, HitInfo hit) { }

    /// <summary>null 保留原版弹幕命中规则，false 否决。</summary>
    public virtual bool? CanBeHitByProjectile(NPC npc, Projectile projectile) => null;

    /// <summary>弹幕来源的目标受击修饰。</summary>
    public virtual void ModifyHitByProjectile(NPC npc, Projectile projectile, ref HitModifiers modifiers) { }

    /// <summary>弹幕造成伤害后接收实际扣除生命量。</summary>
    public virtual void OnHitByProjectile(NPC npc, Projectile projectile, HitInfo hit, int damageDone) { }

    /// <summary>false 阻止此 NPC 对目标的接触攻击。</summary>
    public virtual bool CanHitNPC(NPC npc, NPC target) => true;

    /// <summary>false 阻止此 NPC 被另一 NPC 接触攻击。</summary>
    public virtual bool CanBeHitByNPC(NPC npc, NPC attacker) => true;


    /// <summary>在来源本地修饰之后、目标玩家修饰之前处理伤害。</summary>
    public virtual void ModifyHitPlayer(NPC npc, Player target, ref HurtModifiers modifiers) { }

    /// <summary>正伤害命中之后，先于目标玩家的来源通知执行。</summary>
    public virtual void OnHitPlayer(NPC npc, Player target, HurtInfo hurtInfo) { }

    /// <summary>null 保留原版命中规则；false 否决。</summary>
    public virtual bool? CanBeHitByItem(NPC npc, Player player, Item item) => null;

    /// <summary>null 使用原版近战命中盒相交判定。</summary>
    public virtual bool? CanCollideWithPlayerMeleeAttack(NPC npc, Player player, Item item, Rectangle meleeAttackHitbox) => null;

    /// <summary>所有攻击来源共用的受击修饰。</summary>
    public virtual void ModifyIncomingHit(NPC npc, ref HitModifiers modifiers) { }

    /// <summary>玩家物品攻击的来源相关修饰。</summary>
    public virtual void ModifyHitByItem(NPC npc, Player player, Item item, ref HitModifiers modifiers) { }

    /// <summary>玩家物品攻击实际造成伤害后的通知。</summary>
    public virtual void OnHitByItem(NPC npc, Player player, Item item, HitInfo hit, int damageDone) { }

    /// <summary>死亡时（只在服务端或单人）；false = 不掉落也不跑 OnKill。</summary>
    public virtual bool PreKill(NPC npc) => true;

    /// <summary>死亡时，在 PreKill 与游戏掉落之后。</summary>
    public virtual void OnKill(NPC npc) { }

    /// <summary>自定义动画：改 <c>npc.frame.Y</c>；frameHeight 是贴图高 ÷ 帧数。</summary>
    public virtual void FindFrame(NPC npc, int frameHeight) { }

    /// <summary>false：离玩家再远也不消失。</summary>
    public virtual bool CheckActive(NPC npc) => true;

    /// <summary>生命 ≤ 0 时；false 阻止死亡（要把生命补回去，否则下一帧还会问）。</summary>
    public virtual bool CheckDead(NPC npc) => true;

    /// <summary>按人数缩放之后；balance 是游戏算的数，bossAdjustment 大师 0.85、其它模式 1。</summary>
    public virtual void ApplyDifficultyAndPlayerScaling(NPC npc, int numPlayers, float balance, float bossAdjustment) { }

    /// <summary>能不能聊天；false 挡下，true 放行，<c>null</c> 用游戏的。</summary>
    public virtual bool? CanChat(NPC npc) => null;

    /// <summary>聊天时说的话；chat 是可写引用，换掉 <paramref name="chat"/> 就是换这句。</summary>
    public virtual void GetChat(NPC npc, ref string chat) { }

    /// <summary>每次开这家店时，物品已经备好；shopName 是店名，items 是这次摆出来的（原生数组）。</summary>
    public virtual void ModifyActiveShop(NPC npc, string shopName, Item[] items) { }

    /// <summary>旅行商人的店（<c>Main.travelShop</c> 的类型表）；nextSlot 是指向第一个空位的可写引用。</summary>
    public virtual void SetupTravelShop(int[] shop, ref int nextSlot) { }

    /// <summary>原版按玩家算完生成速率和上限之后修改;spawnRate 最终至少为 1。</summary>
    public virtual void EditSpawnRate(Player player, ref int spawnRate, ref int maxSpawns) { }

    /// <summary>生成与安全矩形的半径(格);后续限制到世界边界。</summary>
    public virtual void EditSpawnRange(Player player, ref int spawnRangeX, ref int spawnRangeY,
                                       ref int safeRangeX, ref int safeRangeY) { }

    /// <summary>原版玩家生成标记写完之后;尚未选择格子,直接改 spawner 会影响本次生成。</summary>
    public virtual void EditSpawnFlags(NPC.Spawner spawner) { }

    /// <summary>生成格已选好后改 spawner 标记;可通过 NPCSpawnInfo 读取本次坐标。</summary>
    public virtual void EditSpawnInfo(NPC.Spawner spawner) { }

    /// <summary>自然生成池:类型 0 表示保留原版抽选,清空或全零权重表示不生成。</summary>
    public virtual void EditSpawnPool(SpawnPool pool, NPCSpawnInfo spawnInfo) { }

    /// <summary>自然生成实体就绪后一次;仅对该实体适用的全局实例分发,坐标单位为格。</summary>
    public virtual void SpawnNPC(int npc, int tileX, int tileY) { }

    /// <summary>false 跳过游戏自己的绘制（PostDraw 照样跑）；drawColor 是可写引用。</summary>
    public virtual bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, ref Color drawColor) => true;

    /// <summary>绘制之后，否决了也跑。</summary>
    public virtual void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) { }

    /// <summary>NPC 那格的光照色，在 PreDraw 之前；改它就是这个钩子返回的像素着色。</summary>
    public virtual void DrawEffects(NPC npc, ref Color drawColor) { }

    /// <summary>返回 <c>null</c> 用游戏的；返回颜色就连 alpha 0 一起接管。</summary>
    public virtual Color? GetAlpha(NPC npc, Color drawColor) => null;

    /// <summary>首领图标的槽位（初值是游戏算的）；改成 -1 就隐藏。</summary>
    public virtual void BossHeadSlot(NPC npc, ref int index) { }

    /// <summary>首领图标的旋转（初值是游戏算的）。</summary>
    public virtual void BossHeadRotation(NPC npc, ref float rotation) { }

    /// <summary>首领图标的镜像（初值是游戏算的）。</summary>
    public virtual void BossHeadSpriteEffects(NPC npc, ref SpriteEffects spriteEffects) { }

    /// <summary>每种 NPC 一次（加载结束时），收的是那一类的样表；掉落写进这个库。</summary>
    public virtual void ModifyNPCLoot(NPC npc, ItemDropDatabase npcLoot) { }

    /// <summary>一次：对每个 NPC 都成立的掉落规则。</summary>
    public virtual void ModifyGlobalLoot(ItemDropDatabase globalLoot) { }

    /// <summary>原生 NPC 同步包发出后，把全局附加数据写进去。</summary>
    public virtual void NetSend(NPC npc, NetWriter writer) { }

    /// <summary>收到全局附加数据。</summary>
    public virtual void NetReceive(NPC npc, NetReader reader) { }
}

// 未移植：命中套（14 个，缺 HitModifiers/HurtModifiers/HitInfo/HurtInfo——NPC.StrikeNPC 与 Player.Hurt 仍是
// int/float 参数）；幸福度（缺 ShopHelper/HelperInfo）；商店（参数是自造的 NPCShop 类，本版没有原生对应物）；
// 生成那套 EditSpawn*/SpawnNPC（要自造的 NPCSpawnInfo/SpawnPool）；register（注册走 Pylon 的自动发现 + ModContent）。
