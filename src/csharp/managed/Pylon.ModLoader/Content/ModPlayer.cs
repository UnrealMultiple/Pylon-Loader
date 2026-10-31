using Il2CppMicrosoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameInput;
using Terraria.Graphics;

namespace Pylon.ModLoader;

/// <summary>挂在玩家身上的自定义数据与行为：每个玩家一份自己的实例（第一次被问到时建）。</summary>
public abstract class ModPlayer
{
    /// <summary>所属 Mod（自动发现时由框架赋值）。</summary>
    public Mod Mod { get; internal set; } = null!;

    /// <summary>加到生命上限上（每帧由 ModifyMaxStats 之后汇总）。</summary>
    public int CumulativeHealth = 0;

    /// <summary>加到魔力上限上。</summary>
    public int CumulativeMana = 0;

    /// <summary>武器伤害那个字段（ModifyWeaponDamage 管线写入；本版还没接那条管线）。</summary>
    public int WeaponDamage = 0;

    /// <summary>这个实例挂在哪个玩家上（由每玩家的实例保管方赋；钩子参数里的 player 是同一个）。</summary>
    public Player Player { get; internal set; } = null!;

    /// <summary>实例建出来时一次。</summary>
    public virtual void Initialize() { }

    /// <summary>玩家进了世界。</summary>
    public virtual void OnEnterWorld(Player player) { }

    /// <summary>复活回来时。</summary>
    public virtual void OnRespawn(Player player) { }

    /// <summary>游戏重置效果之后：这里把饰品每帧打开的东西关掉。</summary>
    public virtual void ResetEffects(Player player) { }

    /// <summary>ResetEffects 之后：写 CumulativeHealth/CumulativeMana，游戏把它们加到上限上。</summary>
    public virtual void ModifyMaxStats(Player player)
    {
        CumulativeHealth = 0;
        CumulativeMana = 0;
    }

    /// <summary>玩家这一帧开始。</summary>
    public virtual void PreUpdate(Player player) { }

    /// <summary>这一帧结束。</summary>
    public virtual void PostUpdate(Player player) { }

    /// <summary>buff 更新之前。</summary>
    public virtual void PreUpdateBuffs(Player player) { }

    /// <summary>buff 更新之后。</summary>
    public virtual void PostUpdateBuffs(Player player) { }

    /// <summary>装备与饰品之后。</summary>
    public virtual void UpdateEquips(Player player) { }

    /// <summary>装备与饰品之后（在 UpdateEquips 后面）。</summary>
    public virtual void PostUpdateEquips(Player player) { }

    /// <summary>生命再生之前。</summary>
    public virtual void UpdateBadLifeRegen(Player player) { }

    /// <summary>生命再生之后。</summary>
    public virtual void UpdateLifeRegen(Player player) { }

    /// <summary>魔力再生之后。</summary>
    public virtual void UpdateManaRegen(Player player) { }

    /// <summary>死了之后每帧（替代其它更新钩子）。</summary>
    public virtual void UpdateDead(Player player) { }

    /// <summary>这一帧的第一次碰撞与移动之前（冲刺、自定义移动）。</summary>
    public virtual void UpdateMovement(Player player) { }

    /// <summary>更新玩家身上的特效（盔甲套装的影子等）。</summary>
    public virtual void FrameEffects(Player player) { }

    /// <summary>false：跳过游戏自己的幸运求和（瓢虫、火把、药水、风筝、钱币）。</summary>
    public virtual bool PreModifyLuck(Player player, ref float luck) => true;

    /// <summary>改算出来的幸运（每帧重算）。</summary>
    public virtual void ModifyLuck(Player player, ref float luck) { }

    /// <summary>false：这一次用不了这个物品。</summary>
    public virtual bool CanUseItem(Player player, Item item) => true;

    /// <summary>false：跳过这一次的物品检查。</summary>
    public virtual bool PreItemCheck(Player player) => true;

    /// <summary>物品检查之后（被否决了也跑）。</summary>
    public virtual void PostItemCheck(Player player) { }

    /// <summary>false：不让死（要把生命补回去，否则下一帧还会问）。</summary>
    public virtual bool PreKill(Player player, PlayerDeathReason damageSource, double damage, int hitDirection, bool pvp) => true;

    /// <summary>死掉之后。</summary>
    public virtual void Kill(Player player, PlayerDeathReason damageSource, double damage, int hitDirection, bool pvp) { }

    /// <summary>钓鱼掷完物品与敌人之后；直接改 attempt 里的 rolledItemDrop/rolledEnemySpawn。</summary>
    public virtual void CatchFish(Player player, ref FishingAttempt attempt) { }

    /// <summary>回的血量（可写引用），也影响快速治疗的选药。</summary>
    public virtual void GetHealLife(Player player, Item item, bool quickHeal, ref int healValue) { }

    /// <summary>回的魔量（可写引用）。</summary>
    public virtual void GetHealMana(Player player, Item item, bool quickHeal, ref int healValue) { }

    /// <summary>false：不加计时也不给药水病，回血和消耗照旧；收的是算上掷骰的时间。</summary>
    public virtual bool ApplyPotionDelay(Player player, Item item, int potionDelay) => true;

    /// <summary>这一帧的第一次碰撞与移动步进之前。</summary>
    public virtual void PreUpdateMovement(Player player) { }

    /// <summary>CapAttackSpeeds 之后。</summary>
    public virtual void PostUpdateMiscEffects(Player player) { }

    /// <summary>HorizontalMovement 之前。</summary>
    public virtual void PostUpdateRunSpeeds(Player player) { }

    /// <summary>游戏算自然再生时的那个数（可写引用）。</summary>
    public virtual void NaturalLifeRegen(Player player, ref float regen) { }

    /// <summary>本地玩家在暂停状态下的更新之后。</summary>
    public virtual void UpdateAutopause(Player player) { }

    /// <summary>重置效果之后、信息饰品更新之后各一次。</summary>
    public virtual void ResetInfoAccessories(Player player) { }

    /// <summary>在设定的激活方向上双击。</summary>
    public virtual void ArmorSetBonusActivated(Player player) { }

    /// <summary>在设定的激活方向上按住；holdTime 是按住的时间。</summary>
    public virtual void ArmorSetBonusHeld(Player player, int holdTime) { }

    /// <summary>真的换了装备预设之后。</summary>
    public virtual void OnEquipmentLoadoutSwitched(Player player, int oldLoadoutIndex, int loadoutIndex) { }

    /// <summary>绘图准备之后：r/g/b/a 是四个颜色倍率（可写引用），fullBright 是那次的满亮开关。</summary>
    public virtual void DrawEffects(Player player, PlayerDrawSet drawInfo, ref float r, ref float g, ref float b,
                                    ref float a, ref bool fullBright) { }

    /// <summary>游戏把玩家整个画完之后。</summary>
    public virtual void DrawPlayer(Player player, Camera camera) { }

    /// <summary>这一次的绘制里藏掉哪几层（PlayerDrawLayer.Hide()）。</summary>
    public virtual void HideDrawLayers(Player player, PlayerDrawSet drawInfo) { }

    /// <summary>各层建起来之前改 drawInfo（PlayerDrawSet）。</summary>
    public virtual void ModifyDrawInfo(Player player, PlayerDrawSet drawInfo) { }

    /// <summary>游戏的变换之后、渲染缓存之前。</summary>
    public virtual void TransformDrawData(Player player, PlayerDrawSet drawInfo) { }

    /// <summary>更新相机之后；改 Main.screenPosition。</summary>
    public virtual void ModifyScreenPosition(Player player) { }

    /// <summary>缩放（可写引用）；各轴的比例保持不变。</summary>
    public virtual void ModifyZoom(Player player, ref float zoom) { }

    /// <summary>物品与敌人掷骰之前；直接改 attempt。</summary>
    public virtual void ModifyFishingAttempt(Player player, ref FishingAttempt attempt) { }

    /// <summary>真正交到手上之前改这条鱼。</summary>
    public virtual void ModifyCaughtFish(Player player, Item fish) { }

    /// <summary>付款之前否决这次购买。</summary>
    public virtual bool CanBuyItem(Player player, NPC vendor, Item item) => true;

    /// <summary>买成之后；item 是光标上收到的那份。</summary>
    public virtual void PostBuyItem(Player player, NPC vendor, Item item) { }

    /// <summary>卖出去之前否决。</summary>
    public virtual bool CanSellItem(Player player, NPC vendor, Item item) => true;

    /// <summary>卖成之后。</summary>
    public virtual void PostSellItem(Player player, NPC vendor, Item item) { }

    /// <summary>护士治疗的价格（可写引用）；显示价格时也会问。</summary>
    public virtual void ModifyNursePrice(Player player, NPC nurse, int health, bool removeDebuffs, ref int price) { }

    /// <summary>false：不做这次服务，并按 chatText 说话；health/removeDebuffs/chatText 都可写。</summary>
    public virtual bool ModifyNurseHeal(Player player, NPC nurse, ref int health, ref bool removeDebuffs, ref string chatText) => true;

    /// <summary>付过钱的服务之后（价格为 0 也算）。</summary>
    public virtual void PostNurseHeal(Player player, NPC nurse, int health, bool removeDebuffs, int price) { }

    /// <summary>null = 交给游戏；true/false 定这次捕捉。</summary>
    public virtual bool? CanCatchNPC(Player player, NPC target, Item item) => null;

    /// <summary>抓到之后，或想抓岩浆小动物而挨了一下时（failed = true）。</summary>
    public virtual void OnCatchNPC(Player player, NPC npc, Item item, bool failed) { }

    /// <summary>false：物品从地上消失但不进背包。</summary>
    public virtual bool OnPickup(Player player, Item item) => true;

    /// <summary>true：这次悬停由 mod 处理，跳过游戏自己的。</summary>
    public virtual bool HoverSlot(Player player, Item[] inventory, int context, int slot) => false;

    /// <summary>按着 Shift 时；true = 这次点击由 mod 处理。</summary>
    public virtual bool ShiftClickSlot(Player player, Item[] inventory, int context, int slot) => false;

    /// <summary>否决这次传送；context 是 'TeleportRod'、'TeleportationPotion'、'Teleport' 或 'Wormhole'。</summary>
    public virtual bool CanBeTeleportedTo(Player player, Vector2 teleportPosition, string context) => true;

    /// <summary>把本玩家的持久数据写进侧车存档（空 compound 不会落盘）。</summary>
    public virtual void SaveData(TagCompound tag) { }

    /// <summary>从侧车存档读回（没有这个键就不会调用）。</summary>
    public virtual void LoadData(TagCompound tag) { }

    /// <summary>游戏存档之前。</summary>
    public virtual void PreSavePlayer(Player player) { }

    /// <summary>游戏存档与附加数据都做完之后（游戏存档抛异常也跑）。</summary>
    public virtual void PostSavePlayer(Player player) { }

    /// <summary>拼 .plr.bl.json 的数据之前。</summary>
    public virtual void PreSaveCustomData(Player player) { }

    /// <summary>原生玩家同步之后；newPlayer = 这个位置还没见过连接。</summary>
    public virtual void SyncPlayer(Player player, int toWho, int fromWho, bool newPlayer) { }

    /// <summary>客户端上，和上一帧复制下来的那份比。</summary>
    public virtual void SendClientChanges(Player player, ModPlayer clientPlayer) { }

    /// <summary>把状态复制进同类的那份副本（跟着原生的克隆走）。</summary>
    public virtual void CopyClientState(Player player, ModPlayer targetCopy) { }

    /// <summary>玩家连接上之后。</summary>
    public virtual void PlayerConnect(Player player) { }

    /// <summary>把断线的玩家移掉之前。</summary>
    public virtual void PlayerDisconnect(Player player) { }

    /// <summary>把一条绘制数据写进 drawInfo 的下一个活动格；满了或计数不对返回 false。</summary>
    public static bool AddDrawData(PlayerDrawSet drawInfo, DrawData drawData)
    {
        if (drawInfo == null || drawData == null) return false;

        var cache = drawInfo.DrawDataCache;
        var count = drawInfo.DrawDataCacheCount;
        // 手机版缓存是定长原生数组：负下标不会抛托管异常，会直接踩内存，所以 count < 0 也要拦。
        if (cache == null || count < 0 || count >= cache.Length) return false;

        cache[count] = drawData;
        drawInfo.DrawDataCacheCount = count + 1;
        return true;
    }

    /// <summary>本地玩家每更新一次：读触屏按钮（ModTouchButton）或键位（参考的 ModKeybind 用法）。</summary>
    public virtual void ProcessTriggers(Player player, TriggersSet triggersSet) { }
}

// 未移植：伤害/攻速/弹药那套（要 StatModifier，等 ItemCheck_Shoot 的实际形状）、受伤与命中那套（要 HurtModifiers/HurtInfo/
// HitModifiers——都是 JS 侧自造类）、额外跳跃（自造 ExtraJump）、ModifyDrawLayerOrdering（自造 PlayerDrawLayer）、
// 钓鱼结果对象（FishingLevel）、商店库存与集合类契约（shopInventory / 起始物品 / 材料枚举）、
// get/getByName/register（实例查找与注册走 Pylon 自己的自动发现）。
