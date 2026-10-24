using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.Contracts;
using Pylon.ModLoader.Pipeline;
using Pylon.ModLoader.SaveData;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.Graphics;

namespace Pylon.ModLoader.Content;
/// <summary>挂在玩家身上的自定义数据与行为：每个玩家一份自己的实例（第一次被问到时建）。</summary>
public abstract class ModPlayer
{
    /// <summary>所属 Mod（自动发现时由框架赋值）。</summary>
    public Mod Mod { get; internal set; } = null!;

    /// <summary>类名（可覆写）。</summary>
    public virtual string Name => GetType().Name;

    /// <summary>"Mod名/类名"。</summary>
    public string FullName => $"{Mod?.Name ?? "Terraria"}/{Name}";

    /// <summary>同一个 Mod 里的同名实例视为同一类。</summary>
    public bool TypeEquals(ModPlayer other) => Mod == other.Mod && Name == other.Name;

    /// <summary>在全部 ModPlayer 模板里的下标（TML 的 <c>Index</c>；实例建出来时从模板复制）。</summary>
    public ushort Index { get; internal set; }

    /// <summary>相对 ModPlayer 覆写了哪些钩子：实例建出来时从模板复制，分派期只做位运算。</summary>
    internal HookMask Hooks { get; set; }

    /// <summary>这个钩子要不要分派给本实例。</summary>
    internal bool HookEnabled(HookId hook) => Hooks.Has(hook);

    /// <summary>这个实例挂在哪个玩家上（由每玩家的实例保管方赋；钩子里一律用它拿玩家）。</summary>
    public Player Player { get; internal set; } = null!;

    /// <summary>实例建出来时一次。</summary>
    public virtual void Initialize() { }

    /// <summary>玩家进了世界。</summary>
    public virtual void OnEnterWorld() { }

    /// <summary>复活回来时。</summary>
    public virtual void OnRespawn() { }

    /// <summary>游戏重置效果之后：这里把饰品每帧打开的东西关掉。</summary>
    public virtual void ResetEffects() { }

    /// <summary>ResetEffects 之后：写生命/魔力上限修正（同 TML，基数是 statLifeMax/statManaMax）。</summary>
    public virtual void ModifyMaxStats(out StatModifier health, out StatModifier mana)
    {
        health = StatModifier.Default;
        mana = StatModifier.Default;
    }

    /// <summary>玩家这一帧开始。</summary>
    public virtual void PreUpdate() { }

    /// <summary>这一帧结束。</summary>
    public virtual void PostUpdate() { }

    /// <summary>buff 更新之前。</summary>
    public virtual void PreUpdateBuffs() { }

    /// <summary>buff 更新之后。</summary>
    public virtual void PostUpdateBuffs() { }

    /// <summary>装备与饰品之后。</summary>
    public virtual void UpdateEquips() { }

    /// <summary>装备与饰品之后（在 UpdateEquips 后面）。</summary>
    public virtual void PostUpdateEquips() { }

    /// <summary>生命再生之前。</summary>
    public virtual void UpdateBadLifeRegen() { }

    /// <summary>生命再生之后。</summary>
    public virtual void UpdateLifeRegen() { }

    /// <summary>死了之后每帧（替代其它更新钩子）。</summary>
    public virtual void UpdateDead() { }

    /// <summary>更新玩家身上的特效（盔甲套装的影子等）。</summary>
    public virtual void FrameEffects() { }

    /// <summary>false：跳过游戏自己的幸运求和（瓢虫、火把、药水、风筝、钱币）。</summary>
    public virtual bool PreModifyLuck(ref float luck) => true;

    /// <summary>改算出来的幸运（每帧重算）。</summary>
    public virtual void ModifyLuck(ref float luck) { }

    /// <summary>false：跳过这一次的物品检查。</summary>
    public virtual bool PreItemCheck() => true;

    /// <summary>物品检查之后（被否决了也跑）。</summary>
    public virtual void PostItemCheck() { }

    /// <summary>这一帧的第一次碰撞与移动步进之前。</summary>
    public virtual void PreUpdateMovement() { }

    /// <summary>CapAttackSpeeds 之后。</summary>
    public virtual void PostUpdateMiscEffects() { }

    /// <summary>HorizontalMovement 之前。</summary>
    public virtual void PostUpdateRunSpeeds() { }

    /// <summary>游戏算自然再生时的那个数（可写引用）。本版经 UpdateLifeRegen 的中体桥派发（无 On 入口）。</summary>
    public virtual void NaturalLifeRegen(ref float regen) { }

    /// <summary>本地玩家在暂停状态下的更新之后。</summary>
    public virtual void UpdateAutopause() { }

    /// <summary>重置效果之后、信息饰品更新之后各一次。</summary>
    public virtual void ResetInfoAccessories() { }

    /// <summary>可见饰品更新（UpdateVisibleAccessories 之后）。</summary>
    public virtual void UpdateVisibleAccessories() { }

    /// <summary>可见时装更新（可见饰品之后）。</summary>
    public virtual void UpdateVisibleVanityAccessories() { }

    /// <summary>染料更新（UpdateDyes 之后）。</summary>
    public virtual void UpdateDyes() { }

    /// <summary>在设定的激活方向上双击。</summary>
    public virtual void ArmorSetBonusActivated() { }

    /// <summary>在设定的激活方向上按住；holdTime 是按住的时间。</summary>
    public virtual void ArmorSetBonusHeld(int holdTime) { }

    /// <summary>真的换了装备预设之后。</summary>
    public virtual void OnEquipmentLoadoutSwitched(int oldLoadoutIndex, int loadoutIndex) { }

    // ===================== 受伤与闪避 =====================

    /// <summary>true 免疫这次伤害；所有作者仍会收到免疫查询。</summary>
    public virtual bool ImmuneTo(PlayerDeathReason damageSource, int cooldownCounter, bool dodgeable) => false;

    /// <summary>本地可闪避伤害的免费闪避，先于消耗型闪避。</summary>
    public virtual bool FreeDodge(HurtInfo info) => false;

    /// <summary>免费闪避未成功时查询的消耗型闪避。</summary>
    public virtual bool ConsumableDodge(HurtInfo info) => false;

    /// <summary>来源修饰之后修改本次受伤；Cancel 可取消原版调用。</summary>
    public virtual void ModifyHurt(ref HurtModifiers modifiers) { }

    /// <summary>原版确认产生伤害后，收到实际返回的伤害数值。</summary>
    public virtual void OnHurt(HurtInfo info) { }

    /// <summary>受伤后仍然存活时执行，致命伤害不调用。</summary>
    public virtual void PostHurt(HurtInfo info) { }

    /// <summary>NPC 接触命中前可否决，冷却槽可回写。</summary>
    public virtual bool CanBeHitByNPC(NPC npc, ref int cooldownSlot) => true;

    /// <summary>非 PvP 弹幕命中前可否决。</summary>
    public virtual bool CanBeHitByProjectile(Projectile proj) => true;

    /// <summary>NPC 来源的受伤修饰，先于通用 ModifyHurt。</summary>
    public virtual void ModifyHitByNPC(NPC npc, ref HurtModifiers modifiers) { }

    /// <summary>弹幕来源的受伤修饰，先于通用 ModifyHurt。</summary>
    public virtual void ModifyHitByProjectile(Projectile proj, ref HurtModifiers modifiers) { }

    /// <summary>NPC 来源已实际造成伤害。</summary>
    public virtual void OnHitByNPC(NPC npc, HurtInfo hurtInfo) { }

    /// <summary>弹幕来源已实际造成伤害。</summary>
    public virtual void OnHitByProjectile(Projectile proj, HurtInfo hurtInfo) { }

    /// <summary>false：不让死（要把生命补回去，否则下一帧还会问）。
    /// playSound/genDust 只是形状对齐的可写引用，本入口带不进原版死亡流程；damageSource 可整体替换。</summary>
    public virtual bool PreKill(double damage, int hitDirection, bool pvp, ref bool playSound, ref bool genDust,
                                ref PlayerDeathReason damageSource) => true;

    /// <summary>死掉之后（真的死了才发）。</summary>
    public virtual void Kill(double damage, int hitDirection, bool pvp, PlayerDeathReason damageSource) { }

    // ===================== 命中 NPC 与 PvP =====================

    /// <summary>false 否决玩家对该 NPC 的命中，所有玩家作者仍收到查询。</summary>
    public virtual bool CanHitNPC(NPC target) => true;

    /// <summary>null 保留原版近战碰撞规则；false 否决这次近战碰撞。</summary>
    public virtual bool? CanMeleeAttackCollideWithNPC(Item item, Rectangle meleeAttackHitbox, NPC target) => null;

    /// <summary>近战打中 NPC 之前的修饰。</summary>
    public virtual void ModifyHitNPC(NPC target, ref HitModifiers modifiers) { }

    /// <summary>近战打中 NPC 后的实际伤害通知。</summary>
    public virtual void OnHitNPC(NPC target, HitInfo hit, int damageDone) { }

    /// <summary>null 保留原版规则；false 否决物品对该 NPC 的命中。</summary>
    public virtual bool? CanHitNPCWithItem(Item item, NPC target) => null;

    /// <summary>物品打中 NPC 之前的修饰。</summary>
    public virtual void ModifyHitNPCWithItem(Item item, NPC target, ref HitModifiers modifiers) { }

    /// <summary>物品打中 NPC 后的实际伤害通知。</summary>
    public virtual void OnHitNPCWithItem(Item item, NPC target, HitInfo hit, int damageDone) { }

    /// <summary>null 保留原版弹幕命中规则，false 否决该玩家所属弹幕的目标。</summary>
    public virtual bool? CanHitNPCWithProj(Projectile proj, NPC target) => null;

    /// <summary>玩家所属弹幕打中 NPC 之前的修饰。</summary>
    public virtual void ModifyHitNPCWithProj(Projectile proj, NPC target, ref HitModifiers modifiers) { }

    /// <summary>玩家所属弹幕打中 NPC 后的实际伤害通知。</summary>
    public virtual void OnHitNPCWithProj(Projectile proj, NPC target, HitInfo hit, int damageDone) { }

    /// <summary>false 阻止该物品对玩家的 PvP 近战命中。</summary>
    public virtual bool CanHitPvp(Item item, Player target) => true;

    /// <summary>在弹幕所属玩家上查询是否允许该弹幕命中 PvP 目标。</summary>
    public virtual bool CanHitPvpWithProj(Projectile proj, Player target) => true;

    /// <summary>打中任何东西之后（x/y 是命中坐标）。</summary>
    public virtual void OnHitAnything(float x, float y, Entity victim) { }

    // ===================== 物品使用 =====================

    /// <summary>false：这一次用不了这个物品。</summary>
    public virtual bool CanUseItem(Item item) => true;

    /// <summary>null = 交给游戏（原版连发判定）；true/false 强制开或关自动连发。</summary>
    public virtual bool? CanAutoReuseItem(Item item) => null;

    /// <summary>本次使用的用时倍率（乘区）。</summary>
    public virtual float UseTimeMultiplier(Item item) => 1f;

    /// <summary>本次使用的动画倍率（乘区）。</summary>
    public virtual float UseAnimationMultiplier(Item item) => 1f;

    /// <summary>整体使用速度倍率（乘区）。</summary>
    public virtual float UseSpeedMultiplier(Item item) => 1f;

    /// <summary>false：不做这次服务，并按 chatText 说话；health/removeDebuffs/chatText 都可写。</summary>
    public virtual bool ModifyNurseHeal(NPC nurse, ref int health, ref bool removeDebuffs, ref string chatText) => true;

    /// <summary>护士治疗的价格（可写引用）；显示价格时也会问。</summary>
    public virtual void ModifyNursePrice(NPC nurse, int health, bool removeDebuffs, ref int price) { }

    /// <summary>付过钱的服务之后（价格为 0 也算）。</summary>
    public virtual void PostNurseHeal(NPC nurse, int health, bool removeDebuffs, int price) { }

    /// <summary>回的血量（可写引用），也影响快速治疗的选药。</summary>
    public virtual void GetHealLife(Item item, bool quickHeal, ref int healValue) { }

    /// <summary>回的魔量（可写引用）。</summary>
    public virtual void GetHealMana(Item item, bool quickHeal, ref int healValue) { }

    /// <summary>改这次使用的魔力开销：reduce 是减量、mult 是倍率（可写引用）。</summary>
    public virtual void ModifyManaCost(Item item, ref float reduce, ref float mult) { }

    /// <summary>魔力不够放不出来时。</summary>
    public virtual void OnMissingMana(Item item, int neededMana) { }

    /// <summary>实际消耗魔力之后。</summary>
    public virtual void OnConsumeMana(Item item, int manaConsumed) { }

    /// <summary>false：不加计时也不给药水病，回血和消耗照旧；收的是算上掷骰的时间。</summary>
    public virtual bool ApplyPotionDelay(Item item, int potionDelay) => true;

    /// <summary>武器的伤害修正（同 TML：<see cref="StatModifier"/> 链）。</summary>
    public virtual void ModifyWeaponDamage(Item item, ref StatModifier damage) { }

    /// <summary>武器的击退修正（同 TML：<see cref="StatModifier"/> 链）。</summary>
    public virtual void ModifyWeaponKnockback(Item item, ref StatModifier knockback) { }

    /// <summary>武器的暴击修正（同 TML：百分点）。</summary>
    public virtual void ModifyWeaponCrit(Item item, ref float crit) { }

    /// <summary>物品画出来的体积（同 TML）。</summary>
    public virtual void ModifyItemScale(Item item, ref float scale) { }

    /// <summary>false：这次发射不扣弹药，不改变发射与选弹结果。</summary>
    public virtual bool CanShoot(Item item) => true;

    /// <summary>发射前的位置/弹幕类型/伤害/击退修改（可写引用）。</summary>
    public virtual void ModifyShootStats(Item item, ref Vector2 position, ref Vector2 velocity, ref int type,
                                         ref int damage, ref float knockback)
    { }

    /// <summary>亲手发射：false = 这发不出去（游戏给 1000 的无效编号）。</summary>
    public virtual bool Shoot(Item item, EntitySource_ItemUse_WithAmmo? source, Vector2 position, Vector2 velocity,
                              int type, int damage, float knockback) => true;

    /// <summary>近战挥出时；hitbox 是这一次的碰撞盒。</summary>
    public virtual void MeleeEffects(Item item, Rectangle hitbox) { }

    /// <summary>false：这次发射不扣弹药；true 扣。null = 交给游戏。</summary>
    public virtual bool? CanConsumeBait(Item bait) => null;

    /// <summary>实际扣除弹药之后；最后一发的弹药实体尚未清空。</summary>
    public virtual void OnConsumeAmmo(Item weapon, Item ammo) { }

    /// <summary>false：这次发射不扣弹药，不改变发射与选弹结果。</summary>
    public virtual bool CanConsumeAmmo(Item weapon, Item ammo) => true;

    /// <summary>钓鱼等级查询（可写引用；改的是等级倍率）。</summary>
    public virtual void GetFishingLevel(Item fishingRod, Item bait, ref float fishingLevel) { }

    /// <summary>物品与敌人掷骰之前；直接改 attempt。</summary>
    public virtual void ModifyFishingAttempt(ref FishingAttempt attempt) { }

    /// <summary>掷骰之后：itemDrop/npcSpawn 是掷出的结果（可写引用）；sonar 是声纳弹窗（玩家喝着声纳药水才弹）。</summary>
    public virtual void CatchFish(FishingAttempt attempt, ref int itemDrop, ref int npcSpawn,
                                  ref AdvancedPopupRequest sonar, ref Vector2 sonarPosition)
    { }

    /// <summary>真正交到手上之前改这条鱼。</summary>
    public virtual void ModifyCaughtFish(Item fish) { }

    /// <summary>钓鱼人的任务奖励（先收集作者改完再统一发）。</summary>
    public virtual void AnglerQuestReward(float rareMultiplier, List<Item> rewardItems) { }

    /// <summary>染料商的奖励池（就地增删）。</summary>
    public virtual void GetDyeTraderReward(List<int> rewardPool) { }

    /// <summary>开局背包（mediumCoreDeath = 中级核心死亡后的那份）。</summary>
    public virtual IEnumerable<Item> AddStartingItems(bool mediumCoreDeath) => System.Array.Empty<Item>();

    /// <summary>开局背包汇总（itemsByMod 按 mod 名分组，就地增删）。</summary>
    public virtual void ModifyStartingInventory(IReadOnlyDictionary<string, List<Item>> itemsByMod, bool mediumCoreDeath) { }

    /// <summary>给合成塞原料（out 回调在原料被消耗时收到通知）。</summary>
    public virtual IEnumerable<Item> AddMaterialsForCrafting(out ItemConsumedCallback itemConsumedCallback)
    {
        itemConsumedCallback = null!;
        return System.Array.Empty<Item>();
    }

    /// <summary><see cref="AddMaterialsForCrafting"/> 的消耗通知：item 是被消耗的那份，index 是作者列表里的下标。</summary>
    public delegate void ItemConsumedCallback(Item item, int index);

    // ===================== 商店 =====================

    /// <summary>付款之前否决这次购买；shopInventory 是摊位上还能买的全部货。</summary>
    public virtual bool CanBuyItem(NPC vendor, Item[] shopInventory, Item item) => true;

    /// <summary>买成之后；item 是光标上收到的那份。</summary>
    public virtual void PostBuyItem(NPC vendor, Item[] shopInventory, Item item) { }

    /// <summary>卖出去之前否决。</summary>
    public virtual bool CanSellItem(NPC vendor, Item[] shopInventory, Item item) => true;

    /// <summary>卖成之后。</summary>
    public virtual void PostSellItem(NPC vendor, Item[] shopInventory, Item item) { }

    // ===================== 绘制 =====================

    /// <summary>绘图准备之后：r/g/b/a 是四个颜色倍率（可写引用），fullBright 是那次的满亮开关。</summary>
    public virtual void DrawEffects(PlayerDrawSet drawInfo, ref float r, ref float g, ref float b,
                                    ref float a, ref bool fullBright)
    { }

    /// <summary>各层建起来之前改 drawInfo（PlayerDrawSet）。</summary>
    public virtual void ModifyDrawInfo(ref PlayerDrawSet drawInfo) { }

    /// <summary>游戏的变换之后、渲染缓存之前。</summary>
    public virtual void TransformDrawData(ref PlayerDrawSet drawInfo) { }

    /// <summary>整表排序时改某个层的安放位置（每层默认位置之外的精细调整）。</summary>
    public virtual void ModifyDrawLayerOrdering(IDictionary<PlayerDrawLayer, PlayerDrawLayer.Position> positions) { }

    /// <summary>这一次的绘制里藏掉哪几层（拿到层实例调 <see cref="PlayerDrawLayer.Hide"/>）。</summary>
    public virtual void HideDrawLayers(PlayerDrawSet drawInfo) { }

    /// <summary>更新相机之后；改 Main.screenPosition。</summary>
    public virtual void ModifyScreenPosition() { }

    /// <summary>缩放（可写引用）；改的就是这一次写入 GameViewMatrix 的倍率。</summary>
    public virtual void ModifyZoom(ref float zoom) { }

    /// <summary>游戏把玩家整个画完之后。</summary>
    public virtual void DrawPlayer(Camera camera) { }

    // ===================== 额外跳跃 =====================

    /// <summary>这一次双跳的时长倍率（可写引用；乘在 jumpHeight 上）。</summary>
    public virtual void ModifyExtraJumpDurationMultiplier(ExtraJump jump, ref float duration) { }

    /// <summary>false：这次起不了这个双跳；所有作者仍会收到查询。</summary>
    public virtual bool CanStartExtraJump(ExtraJump jump) => true;

    /// <summary>双跳开始之后；playSound 为 false 时压掉起跳音效（对原版跳跃也生效）。</summary>
    public virtual void OnExtraJumpStarted(ExtraJump jump, ref bool playSound) { }

    /// <summary>双跳结束之后（中止也走这里）。</summary>
    public virtual void OnExtraJumpEnded(ExtraJump jump) { }

    /// <summary>双跳刷新（落地回充）之后。</summary>
    public virtual void OnExtraJumpRefreshed(ExtraJump jump) { }

    /// <summary>双跳的视觉特效帧（CanShowExtraJumpVisuals 全放行时每帧调）。</summary>
    public virtual void ExtraJumpVisuals(ExtraJump jump) { }

    /// <summary>false：藏掉这个双跳的视觉特效（原版特效也被藏）。</summary>
    public virtual bool CanShowExtraJumpVisuals(ExtraJump jump) => true;

    /// <summary>双跳的可跳次数被清掉时（落地前撞墙、坐骑禁跳等）。</summary>
    public virtual void OnExtraJumpCleared(ExtraJump jump) { }

    // ===================== 背包与存档 =====================

    /// <summary>false：物品从地上消失但不进背包（参数是世界实体 WorldItem）。</summary>
    public virtual bool OnPickup(WorldItem item) => true;

    /// <summary>抓小动物之前；false 否决这次捕捉，true 直接捕捉（false 一旦出现后面改不回 true）。</summary>
    public virtual bool? CanCatchNPC(NPC target, Item item) => null;

    /// <summary>抓取之后（failed：抓取失败或被反击）。</summary>
    public virtual void OnCatchNPC(NPC npc, Item item, bool failed) { }

    /// <summary>true：这次悬停由 mod 处理，跳过游戏自己的。</summary>
    public virtual bool HoverSlot(Item[] inventory, int context, int slot) => false;

    /// <summary>按着 Shift 时；true = 这次点击由 mod 处理。</summary>
    public virtual bool ShiftClickSlot(Item[] inventory, int context, int slot) => false;

    /// <summary>否决这次传送；context 是 'TeleportRod'、'TeleportationPotion'、'Teleport' 或 'Wormhole'。</summary>
    public virtual bool CanBeTeleportedTo(Vector2 teleportPosition, string context) => true;

    /// <summary>这一帧的能力键状态（每帧一次，本地玩家）。</summary>
    public virtual void ProcessTriggers(TriggersSet triggersSet) { }

    /// <summary>把本玩家的持久数据写进侧车存档（空 compound 不会落盘）。</summary>
    public virtual void SaveData(TagCompound tag) { }

    /// <summary>从侧车存档读回（没有这个键就不会调用）。</summary>
    public virtual void LoadData(TagCompound tag) { }

    /// <summary>游戏存档之前。</summary>
    public virtual void PreSavePlayer() { }

    /// <summary>游戏存档与附加数据都做完之后（游戏存档抛异常也跑）。</summary>
    public virtual void PostSavePlayer() { }

    /// <summary>拼 .plr.bl.json 的数据之前。</summary>
    public virtual void PreSaveCustomData() { }

    /// <summary>原生玩家同步之后；newPlayer = 这个位置还没见过连接。</summary>
    public virtual void SyncPlayer(int toWho, int fromWho, bool newPlayer) { }

    /// <summary>客户端上，和上一帧复制下来的那份比。</summary>
    public virtual void SendClientChanges(ModPlayer clientPlayer) { }

    /// <summary>把状态复制进同类的那份副本（跟着原生的克隆走）。</summary>
    public virtual void CopyClientState(ModPlayer targetCopy) { }

    /// <summary>玩家连接上之后。</summary>
    public virtual void PlayerConnect() { }

    /// <summary>把断线的玩家移掉之前。</summary>
    public virtual void PlayerDisconnect() { }
}
