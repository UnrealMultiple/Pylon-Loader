using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.UI;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;

namespace Pylon.ModLoader;

/// <summary>自定义 NPC（敌对/小动物/Boss）：一个类型一份模板，每个实体一份实例；钩子里用 <see cref="NPC"/> 拿实体。</summary>
public abstract class ModNpc : ModType
{
    /// <summary>相对 ModNpc 覆写了哪些钩子：绑定时从模板复制，分派期只做位运算。</summary>
    internal HookMask Hooks { get; set; }

    /// <summary>这个钩子要不要分派给本实例。</summary>
    internal bool HookEnabled(HookId hook) => Hooks.Has(hook);

    /// <summary>贴图是竖排帧条，帧数写进 <c>Main.npcFrameCount[Type]</c>。</summary>
    public override string TexturePath => $"assets/npcs/{Name}.png";

    /// <summary>只在跑游戏 AI 期间把 npc.type 换成它；<c>npc.aiStyle</c> 要另设（只读属性，按类固定）。</summary>
    public virtual int AIType => 0;

    /// <summary>照这个原版 NPC 的 FindFrame 动画（0 = 自定）；在 SetDefaults 里定；覆写了 <see cref="FindFrame"/> 时以自己的为准。</summary>
    public virtual int AnimationType => 0;

    /// <summary>旗帜 ID（代表 NPC 的类型）；0 = 关。与 <see cref="BannerItem"/> 必须成对填写。</summary>
    public virtual int Banner => 0;

    /// <summary>旗帜解锁的物品 ID；0 = 关。与 <see cref="Banner"/> 必须成对填写。</summary>
    public virtual int BannerItem => 0;

    /// <summary>血条；null = 用游戏那条。要在 <see cref="SetDefaults"/> 里给。</summary>
    public virtual ModBossBar BossBar => null!;

    /// <summary>显示名；空 = 用 <c>NPCName.&lt;Name&gt;</c>（ContentInstaller 写 Lang）。</summary>
    public virtual string DisplayName => "";

    /// <summary>地图与房屋菜单上的头；空 = <c>&lt;Texture&gt;_Head</c>。安装期加载，缺省文件不存在时不注册。</summary>
    public virtual string HeadTexture => "";

    /// <summary>微光后的头；空 = <c>&lt;Texture&gt;_Shimmer_Head</c>。安装期加载，缺省文件不存在时不注册。</summary>
    public virtual string ShimmerHeadTexture => "";

    /// <summary>首领血条与地图图标；空 = <c>&lt;Texture&gt;_Head_Boss</c>。安装期加载，缺省文件不存在时不注册。</summary>
    public virtual string BossHeadTexture => "";

    /// <summary>靠近屏幕时放的音乐；-1 = 用游戏的。由 MusicPipeline 按优先级选择。</summary>
    public virtual int Music => -1;

    /// <summary>原版是 SceneEffectPriority.BossLow = 6；两个都有音乐时高的赢。</summary>
    public virtual int SceneEffectPriority => 6;

    /// <summary>当前绑定的游戏实体（SetStaticDefaults 期间为 null）。</summary>
    public NPC NPC => BoundEntity;

    /// <summary>实体实例绑定的游戏 NPC（框架维护）。</summary>
    internal NPC BoundEntity { get; set; } = null!;

    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>这个类型的每个 NPC 生成时；专家/大师缩放在这之后（finishModNpc）。</summary>
    public virtual void SetDefaults() { }

    /// <summary>一次（静态默认值之后）：掉落写进游戏库（npcLoot.Add(rule)）。</summary>
    public virtual void ModifyNPCLoot(NPCLoot npcLoot) { }

    /// <summary>一次：图鉴条目（生物群系、时间、文本）。</summary>
    public virtual void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry) { }

    /// <summary>生成后一次，收得到原生的生成来源。</summary>
    public virtual void OnSpawn(IEntitySource source) { }

    /// <summary>按人数缩放；balance 是游戏算的数，bossAdjustment 大师 0.85、其它模式 1。</summary>
    public virtual void ApplyDifficultyAndPlayerScaling(int numPlayers, float balance, float bossAdjustment) { }

    // ===================== 每帧 =====================

    /// <summary>增益标志重置完、重新生效与跑 AI 之前。</summary>
    public virtual void ResetEffects() { }

    /// <summary>每帧；false 跳过游戏 AI 与 AI。</summary>
    public virtual bool PreAI() => true;

    /// <summary>每帧。</summary>
    public virtual void AI() { }

    /// <summary>每帧，AI 之后（PreAI 否决了也跑）。</summary>
    public virtual void PostAI() { }

    /// <summary>自定义动画：改 <c>npc.frame.Y</c>；frameHeight = 贴图高 ÷ 帧数；覆写后原版 FindFrame 整段不跑（帧的 X/Width/Height 由框架先填好）。</summary>
    public virtual void FindFrame(int frameHeight) { }

    /// <summary>false：离玩家再远也不消失。</summary>
    public virtual bool CheckActive() => true;

    /// <summary>回血拦截：原版算完本帧回血后调用，改 damage 即改本帧回血扣减（负值 = 掉血）。</summary>
    public virtual void UpdateLifeRegen(ref int damage) { }

    /// <summary>微光平台：null 交给游戏，false 不穿。</summary>
    public virtual bool? CanFallThroughPlatforms() => null;

    // ===================== 受击与命中 =====================

    /// <summary>每次被打中、在游戏自己的效果之后；<c>npc.life &lt;= 0</c> 是致命的这击。收的是实际伤害信息。</summary>
    public virtual void HitEffect(HitInfo hit) { }

    /// <summary>生命 ≤ 0 时；false 阻止死亡（要把生命补回去，否则下一帧还会问）。</summary>
    public virtual bool CheckDead() => true;

    /// <summary>死亡时；false 取消掉落与原版 NPCLoot。</summary>
    public virtual bool PreKill() => true;

    /// <summary>死亡时，在 PreKill 与游戏掉落之后。</summary>
    public virtual void OnKill() { }

    /// <summary>null 保留原版命中规则，false 否决玩家的物品攻击。</summary>
    public virtual bool? CanBeHitByItem(Player player, Item item) => null;

    /// <summary>null 使用原版近战命中盒相交判定。</summary>
    public virtual bool? CanCollideWithPlayerMeleeAttack(Player player, Item item, Rectangle meleeAttackHitbox) => null;

    /// <summary>所有攻击来源共用的受击修饰。</summary>
    public virtual void ModifyIncomingHit(ref HitModifiers modifiers) { }

    /// <summary>玩家物品攻击的来源相关修饰。</summary>
    public virtual void ModifyHitByItem(Player player, Item item, ref HitModifiers modifiers) { }

    /// <summary>玩家物品攻击实际造成伤害后的通知。</summary>
    public virtual void OnHitByItem(Player player, Item item, HitInfo hit, int damageDone) { }

    /// <summary>null 保留原版弹幕命中规则，false 否决。</summary>
    public virtual bool? CanBeHitByProjectile(Projectile projectile) => null;

    /// <summary>弹幕来源的目标受击修饰。</summary>
    public virtual void ModifyHitByProjectile(Projectile projectile, ref HitModifiers modifiers) { }

    /// <summary>弹幕造成伤害后接收实际扣除生命量。</summary>
    public virtual void OnHitByProjectile(Projectile projectile, HitInfo hit, int damageDone) { }

    /// <summary>NPC 接触攻击命中玩家前查询；可修改本次使用的受伤冷却槽。</summary>
    public virtual bool CanHitPlayer(Player target, ref int cooldownSlot) => true;

    /// <summary>NPC 接触攻击造成伤害之前的来源修饰。</summary>
    public virtual void ModifyHitPlayer(Player target, ref HurtModifiers modifiers) { }

    /// <summary>原版实际造成正伤害后通知，收到实际伤害信息。</summary>
    public virtual void OnHitPlayer(Player target, HurtInfo hurtInfo) { }

    /// <summary>npc 对 npc（敌人撞上居民）；false 就是挡下。</summary>
    public virtual bool CanHitNPC(NPC target) => true;

    /// <summary>npc 挨 npc 打；false 就是挡下。</summary>
    public virtual bool CanBeHitByNPC(NPC attacker) => true;

    /// <summary>NPC 接触攻击命中另一个 NPC 前的修饰。</summary>
    public virtual void ModifyHitNPC(NPC target, ref HitModifiers modifiers) { }

    /// <summary>NPC 接触攻击实际造成正伤害后的通知。</summary>
    public virtual void OnHitNPC(NPC target, HitInfo hit) { }

    /// <summary>true = 这个 NPC 死掉时走特殊掉落逻辑（原版掉落整段跳过）。</summary>
    public virtual bool SpecialOnKill() => false;

    // ===================== 绘制 =====================

    /// <summary>NPC 中心那格的光照色，在 PreDraw 之前；改它就是改这次的画色。</summary>
    public virtual void DrawEffects(ref Color drawColor) { }

    /// <summary>false 跳过游戏自己的绘制；PostDraw 照样会跑。</summary>
    public virtual bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) => true;

    /// <summary>绘制之后。</summary>
    public virtual void PostDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) { }

    /// <summary>绘制缓存之后；把 index 加进 <c>Main.instance.DrawCacheNPCs*</c> 就画在那批里。</summary>
    public virtual void DrawBehind(int index) { }

    /// <summary>返回 <c>null</c> 用游戏的；返回颜色就连 alpha 0 一起接管。</summary>
    public virtual Color? GetAlpha(Color drawColor) => null;

    /// <summary>首领图标的槽位（初值是游戏算的）；改成 -1 就隐藏。</summary>
    public virtual void BossHeadSlot(ref int index) { }

    /// <summary>首领图标的旋转（初值是游戏算的）。</summary>
    public virtual void BossHeadRotation(ref float rotation) { }

    /// <summary>首领图标的镜像（初值是游戏算的）。</summary>
    public virtual void BossHeadSpriteEffects(ref SpriteEffects spriteEffects) { }

    /// <summary>血条：null 用游戏的，false 不画，true 接管 scale/position。</summary>
    public virtual bool? DrawHealthBar(byte hbPosition, ref float scale, ref Vector2 position) => null;

    /// <summary>悬停时的高亮框（可写引用）。</summary>
    public virtual void ModifyHoverBoundingBox(ref Rectangle boundingBox) { }

    /// <summary>鼠标悬停交互（城镇/可对话 NPC）；false = 这次不响应。</summary>
    public virtual bool PreHoverInteract(bool mouseIntersects) => true;

    // ===================== 表情与派对 =====================

    /// <summary>这个 NPC 被表情气泡点名时：返回要回应的表情号（<c>null</c> = 从 emoteList 里随机挑一个）。
    /// emoteList 的第一项是泡泡当前的表情号；可以直接往里追加候选。</summary>
    public virtual int? PickEmote(Player closestPlayer, List<int> emoteList, WorldUIAnchor otherAnchor) => null;

    /// <summary>这个 NPC 的表情气泡摆哪儿（拿到的是游戏算好的初值）。</summary>
    public virtual void EmoteBubblePosition(ref Vector2 position, ref SpriteEffects spriteEffects) { }

    /// <summary>聊天气泡摆哪儿（可写引用）。基线经原生绘制调节桥，本版不接。</summary>
    public virtual void ChatBubblePosition(ref Vector2 position, ref SpriteEffects spriteEffects) { }

    /// <summary>开派对时戴不戴派对帽（城镇）。</summary>
    public virtual bool UsesPartyHat() => true;

    /// <summary>派对帽摆哪儿（可写引用）。基线经原生绘制调节桥，本版不接。</summary>
    public virtual void PartyHatPosition(ref Vector2 position, ref SpriteEffects spriteEffects) { }

    // ===================== 死亡与名字 =====================

    /// <summary>死亡消息正文（城镇）；默认空。基线走五入口链（checkDead/CelebrateBossDeath/NewText×2/Broadcast），属后续批次。</summary>
    public virtual Terraria.Localization.LocalizedText DeathMessage => null!;

    /// <summary>改死亡消息正文与颜色；返回 false = 这条消息整条吞掉（作者已自行处理）。</summary>
    public virtual bool ModifyDeathMessage(ref Terraria.Localization.NetworkText customText, ref Color color) => true;

    /// <summary>改显示名（图鉴/入侵进度条）。</summary>
    public virtual void ModifyTypeName(ref string typeName) { }

    // ===================== 传送雕像与捕捉 =====================

    /// <summary>能不能被国王/王后雕像传走（toKingStatue = 国王）；本类型参与王座/后座候选。</summary>
    public virtual bool CanGoToStatue(bool toKingStatue) => false;

    /// <summary>被雕像传走之后。</summary>
    public virtual void OnGoToStatue(bool toKingStatue) { }

    /// <summary>null 交给游戏；true/false 决定能不能被这个物品捕捉。</summary>
    public virtual bool? CanBeCaughtBy(Item item, Player player) => null;

    /// <summary>被捕捉之后（failed = true 是想抓岩浆小动物挨了一下）。</summary>
    public virtual void OnCaughtBy(Player player, Item item, bool failed) { }

    // ===================== 存档 =====================

    /// <summary>城镇/有主 NPC 随存档保存的自定义数据（需要配 NeedSaving）。</summary>
    public virtual bool NeedSaving() => false;

    /// <summary>随世界存档保存的自定义数据。</summary>
    public virtual void SaveData(TagCompound tag) { }

    /// <summary>读档后按保存的数据恢复（与 SaveData 配对）。</summary>
    public virtual void LoadData(TagCompound tag) { }

    // ===================== 原生同步 =====================

    /// <summary>原生 NPC 同步包发出后，把本 NPC 的附加数据写进去。</summary>
    public virtual void SendExtraAI(NetWriter writer) { }

    /// <summary>收到本 NPC 的附加数据。</summary>
    public virtual void ReceiveExtraAI(NetReader reader) { }

    // ===================== 生成 =====================

    /// <summary>每次自然生成：返回权重（原版重 1，0 = 不生成）。分派给完整 NPCSpawnInfo。</summary>
    public virtual float SpawnChance(NPCSpawnInfo spawnInfo) => 0f;

    /// <summary>只有生成器上下文时按 InfoOf 现构造一份条件再走上面的（基线同款转发）。</summary>
    public virtual float SpawnChance(NPC.Spawner spawner) => SpawnChance(NpcSpawn.InfoOf(spawner));

    /// <summary>被抽中时怎么生成，在生成的格 (tileX, tileY)；返回 NPC 下标。</summary>
    public virtual int SpawnNPC(int tileX, int tileY) =>
        Terraria.NPC.NewNPC(Terraria.NPC.GetSpawnSourceForNaturalSpawn(), tileX * 16 + 8, tileY * 16, Type, 0, 0f, 0f, 0f, 0f, 255);

    // ===================== 城镇 =====================

    /// <summary>隔一阵子问一次（世界里还没有这个类型时）：true 就搬进下一间空房。</summary>
    public virtual bool CanTownNPCSpawn(int numTownNPCs) => false;

    /// <summary>这间房（单位：格）合适吗？</summary>
    public virtual bool CheckConditions(int left, int right, int top, int bottom) => true;

    /// <summary>它的名字表。</summary>
    public virtual List<string> SetNPCNameList() => [];

    /// <summary>这个 NPC 的外形变体档案（变体/名字/贴图/头像）；null = 不参与。
    /// 返回游戏自带的具体实现（如 TownNPCProfiles.LegacyWithSimpleShimmer），注册期写进游戏的档案表，
    /// 之后的变体摇号/命名/贴图/头像都由游戏按表查询。</summary>
    public virtual ITownNPCProfile? TownNPCProfile() => null;

    /// <summary>把本 NPC 的商店加进列表（内容安装时一次）；店自己调 <see cref="NpcShop.Register"/>。</summary>
    public virtual void AddShops() { }

    /// <summary>能不能聊天；默认就是 <c>npc.townNPC</c>（分派在原版判定之后，false 挡下）。</summary>
    public virtual bool CanChat() => NPC.townNPC;

    /// <summary>聊天时说的话；<c>null</c> 用游戏的。Global 之后还能再改（ref）。</summary>
    public virtual string GetChat() => null!;

    /// <summary>聊天的两个按钮文字；留空 = 没这个按钮（手机最多显示两个）。</summary>
    public virtual void SetChatButtons(ref string button, ref string button2) { }

    /// <summary>点了按钮（城镇）；往 shopName 写商店名就打开它。</summary>
    public virtual void OnChatButtonClicked(bool firstButton, ref string shopName) { }

    /// <summary>打开的商店库存（城镇，按 shopName）。</summary>
    public virtual void ModifyActiveShop(string shopName, Item[] items) { }

    /// <summary>这个 NPC 的图鉴条目挂在哪些 mod 群落的图鉴元素下（填 <see cref="ModBiome"/> 的类型号）。</summary>
    public int[] SpawnModBiomes { get; set; } = [];

    // ===================== 城镇攻击 =====================

    /// <summary>城镇 NPC 攻击伤害与击退（可写引用）。</summary>
    public virtual void TownNPCAttackStrength(ref int damage, ref float knockback) { }

    /// <summary>城镇 NPC 攻击冷却（可写引用）。</summary>
    public virtual void TownNPCAttackCooldown(ref int cooldown, ref int randExtraCooldown) { }

    /// <summary>城镇 NPC 攻击发射的弹幕与攻击间隔（可写引用）。</summary>
    public virtual void TownNPCAttackProj(ref int projType, ref int attackDelay) { }

    /// <summary>城镇 NPC 弹幕速度参数（可写引用）。</summary>
    public virtual void TownNPCAttackProjSpeed(ref float multiplier, ref float gravityCorrection, ref float randomOffset) { }

    /// <summary>城镇 NPC 连发节奏（可写引用）。</summary>
    public virtual void TownNPCAttackShoot(ref bool inBetweenShots) { }

    /// <summary>城镇 NPC 魔法攻击的光环亮度（可写引用）。</summary>
    public virtual void TownNPCAttackMagic(ref float auraLightMultiplier) { }

    /// <summary>城镇 NPC 挥舞尺寸（可写引用）。</summary>
    public virtual void TownNPCAttackSwing(ref int itemWidth, ref int itemHeight) { }

    /// <summary>城镇 NPC 开枪的持枪绘制（可写引用）。</summary>
    public virtual void DrawTownAttackGun(ref Texture2D item, ref Rectangle itemFrame, ref float scale,
                                          ref int horizontalHoldoutOffset) { }

    /// <summary>城镇 NPC 挥舞的绘制（可写引用）。</summary>
    public virtual void DrawTownAttackSwing(ref Texture2D item, ref Rectangle itemFrame, ref int itemSize,
                                            ref float scale, ref Vector2 offset) { }
}

// 分派未接的声明钩子见各方法注释；接分派时同步 HookId（追加区）与调用点。
// 暂缓：ModifyCollisionData（要的 MultipliableFloat 不在本构建游戏元数据里）、
// RegisterChatButtons/OnChatButtonClicked(NPCInteraction)（要的 NPCInteractionList 是对侧自造类，本构建只有 NPCInteraction）。
// AutoStaticDefaults 的贴图登记走 Pylon 的安装管线，不另设钩子。
