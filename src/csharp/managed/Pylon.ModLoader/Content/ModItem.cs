using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Utilities;

namespace Pylon.ModLoader;

/// <summary>自定义物品：一个类 = 一个物品类型，每个地上/背包里的实例对应一个本类实例；钩子里用 <see cref="Item"/> 拿实体。</summary>
public abstract class ModItem : ModType
{
    /// <summary>名字（空 = 用 ItemName.&lt;类名&gt; 的本地化键）。</summary>
    public virtual string DisplayName => Name;

    /// <summary>描述（空 = 用 ItemTooltip.&lt;类名&gt;）；多行用 \n。</summary>
    public virtual string Tooltip => null!;

    /// <summary>贴图相对 Mod 目录的路径；注册器按类名定，这里由管线接。</summary>
    public override string TexturePath => $"assets/items/{Name}.png";

    /// <summary>这个实例对应的游戏物品；注册期的模板上为 null。</summary>
    public Item Item { get; internal set; } = null!;

    /// <summary>游戏复制了这个物品；返回副本对应的实例（默认浅拷贝并换实体）。</summary>
    public virtual ModItem Clone(Item newItem)
    {
        var copy = (ModItem)MemberwiseClone();
        copy.Item = newItem;
        return copy;
    }

    /// <summary>true：新物品拿模板的副本（Clone），不是新实例。</summary>
    public virtual bool CloneNewInstances => false;

    /// <summary>给一个新生实体造对应的实例（默认新造，不是复用模板）。</summary>
    public virtual ModItem NewInstance(Item entity)
    {
        if (CloneNewInstances) return Clone(entity);

        var instance = (ModItem)Activator.CreateInstance(GetType(), true)!;
        instance.Mod = Mod;
        instance.Item = entity;
        return instance;
    }

    /// <summary>注册期一次。安装期由装备管线调用，每个物品一次。</summary>
    public virtual void Load() { }

    private List<EquipTexture>? _equipTextures;

    /// <summary>这件物品注册过的装备贴图（安装期由装备管线读）。</summary>
    internal IReadOnlyList<EquipTexture> EquipTextures =>
        _equipTextures is null ? Array.Empty<EquipTexture>() : _equipTextures;

    /// <summary>注册一张装备贴图：在 <see cref="Load"/> 里调，返回的实例留到 <c>SetDefaults</c> 里取 <c>Slot</c>。</summary>
    protected EquipTexture AddEquipTexture(string name, EquipType type, string texture)
    {
        var equip = new EquipTexture
        {
            Name = name,
            Type = (int)type,
            Texture = texture,
            Item = this,
        };

        (_equipTextures ??= []).Add(equip);
        return equip;
    }

    /// <summary>按注册名查这张物品某张装备贴图的槽位（在 SetDefaults 里用；没有就是 -1）。</summary>
    public int GetEquipSlot(EquipType part, string name) => EquipTable.SlotOf(part, name);

    /// <summary>这一类型的每个物品生成时跑；数值都写进 <see cref="Item"/>。</summary>
    public virtual void SetDefaults() { }

    /// <summary>配方注册期一次（AddRecipe 之类放这里）。</summary>
    public virtual void AddRecipes() { }

    /// <summary>每次 tooltip 显示时一次（合成指南弹窗里也会，不带颜色）；直接改 tooltips 列表。</summary>
    public virtual void ModifyTooltips(List<TooltipLine> tooltips) { }

    /// <summary>画整个 tooltip 前；改 x/y 移动所有行，false = 一行都不画。</summary>
    public virtual bool PreDrawTooltip(ReadOnlyCollection<TooltipLine> lines, ref int x, ref int y) => true;

    /// <summary>所有行画完后（收 DrawableTooltipLine）。</summary>
    public virtual void PostDrawTooltip(ReadOnlyCollection<DrawableTooltipLine> lines) { }

    /// <summary>画这一行前；改 line 的 X/Y/字体/颜色/缩放，yOffset 加在后面所有行上；false = 这行不画。</summary>
    public virtual bool PreDrawTooltipLine(DrawableTooltipLine line, ref int yOffset) => true;

    /// <summary>这一行画完后（没画也跑）。</summary>
    public virtual void PostDrawTooltipLine(DrawableTooltipLine line) { }

    /// <summary>物品被创建时（同 TML）：制作、掉落、商店等来源各自带上下文；制作走 <see cref="RecipeItemCreationContext"/>。</summary>
    public virtual void OnCreated(ItemCreationContext context) { }

    // ===================== 使用 =====================

    /// <summary>false：这一次用不了这个物品。</summary>
    public virtual bool CanUseItem(Player player) => true;

    /// <summary>每帧使用；true = 物品算用过了（消耗品会被吃掉），false 显式不算，null = 交给游戏。</summary>
    public virtual bool? UseItem(Player player) => null;

    /// <summary>使用动画应用时（每帧使用）。</summary>
    public virtual void UseAnimation(Player player) { }

    /// <summary>null = 交给游戏（原版连发判定）；true/false 强制开或关自动连发。</summary>
    public virtual bool? CanAutoReuseItem(Player player) => null;

    /// <summary>本次使用的用时倍率（乘区）。</summary>
    public virtual float UseTimeMultiplier(Player player) => 1f;

    /// <summary>本次使用的动画倍率（乘区）。</summary>
    public virtual float UseAnimationMultiplier(Player player) => 1f;

    /// <summary>整体使用速度倍率（乘区）。</summary>
    public virtual float UseSpeedMultiplier(Player player) => 1f;

    /// <summary>改这次使用的魔力开销：reduce 是减量、mult 是倍率（可写引用）。</summary>
    public virtual void ModifyManaCost(Player player, ref float reduce, ref float mult) { }

    /// <summary>魔力不够放不出来时。</summary>
    public virtual void OnMissingMana(Player player, int neededMana) { }

    /// <summary>实际消耗魔力之后。</summary>
    public virtual void OnConsumeMana(Player player, int manaConsumed) { }

    /// <summary>使用时画哪一帧。</summary>
    public virtual void UseItemFrame(Player player) { }

    /// <summary>握着时画哪一帧。</summary>
    public virtual void HoldItemFrame(Player player) { }

    /// <summary>true：右键也算主用途（AltFunctionUse 路径）。</summary>
    public virtual bool AltFunctionUse(Player player) => false;

    /// <summary>手里拿着时每帧一次。</summary>
    public virtual void HoldItem(Player player) { }

    /// <summary>鱼线从哪儿出来、什么颜色（两个可写引用，像素）。</summary>
    public virtual void ModifyFishingLine(Projectile bobber, ref Vector2 lineOriginOffset, ref Color lineColor) { }

    /// <summary>true：是渔夫的任务鱼。</summary>
    public virtual bool IsQuestFish() => false;

    /// <summary>false：每天的抽签不会落在它身上。</summary>
    public virtual bool IsAnglerQuestAvailable() => true;

    /// <summary>渔夫说的那句话（description）和去哪儿钓（catchLocation）。</summary>
    public virtual void AnglerQuestChat(ref string description, ref string catchLocation) { }

    /// <summary>使用时的样子；游戏自己的样式之后跑。</summary>
    public virtual void UseStyle(Player player, Rectangle heldItemFrame) { }

    /// <summary>握着时的样子；游戏自己的样式之后跑。</summary>
    public virtual void HoldStyle(Player player, Rectangle heldItemFrame) { }

    /// <summary>手上武器的位移，像素；返 null = 不动。</summary>
    public virtual Vector2? HoldoutOffset() => null;

    /// <summary>每帧，在背包里。</summary>
    public virtual void UpdateInventory(Player player) { }

    // ===================== 武器数值 =====================

    /// <summary>武器的伤害修正（同 TML：<see cref="StatModifier"/> 链）。</summary>
    public virtual void ModifyWeaponDamage(Player player, ref StatModifier damage) { }

    /// <summary>武器的击退修正（同 TML：<see cref="StatModifier"/> 链）。</summary>
    public virtual void ModifyWeaponKnockback(Player player, ref StatModifier knockback) { }

    /// <summary>武器的暴击修正（同 TML：百分点）。</summary>
    public virtual void ModifyWeaponCrit(Player player, ref float crit) { }

    // ===================== 弹药 =====================

    /// <summary>false：没有背包弹药也可使用对应弹药类别的默认非消耗样例。</summary>
    public virtual bool NeedsAmmo(Player player) => true;

    /// <summary>弹药被选中后修改最终弹幕参数（本类是弹药，weapon 是选它的武器）；damage 修饰应用在游戏已经计算的伤害上。</summary>
    public virtual void PickAmmo(Item weapon, Player player, ref int type, ref float speed,
                                 ref StatModifier damage, ref float knockback) { }

    /// <summary>null 按弹药类别匹配，true 允许额外类型，false 否决该候选（本类是武器）。</summary>
    public virtual bool? CanChooseAmmo(Item ammo, Player player) => null;

    /// <summary>作为弹药时决定能否被这件武器选中；false 优先于任何允许（本类是弹药）。</summary>
    public virtual bool? CanBeChosenAsAmmo(Item weapon, Player player) => null;

    /// <summary>false：正常发射但不消耗这份弹药（本类是武器）。</summary>
    public virtual bool CanConsumeAmmo(Item ammo, Player player) => true;

    /// <summary>作为弹药时否决消耗（本类是弹药）。</summary>
    public virtual bool CanBeConsumedAsAmmo(Item weapon, Player player) => true;

    /// <summary>实际扣除弹药之后，最后一发清空物品之前（本类是武器）。</summary>
    public virtual void OnConsumeAmmo(Item ammo, Player player) { }

    /// <summary>作为弹药实际扣除之后。</summary>
    public virtual void OnConsumedAsAmmo(Item weapon, Player player) { }

    // ===================== 发射 =====================

    /// <summary>false：用得了，但不发射。</summary>
    public virtual bool CanShoot(Player player) => true;

    /// <summary>每个弹幕之前：位置/速度/类型/伤害/击退都是可写引用。</summary>
    public virtual void ModifyShootStats(Player player, ref Vector2 position, ref Vector2 velocity, ref int type,
                                         ref int damage, ref float knockback) { }

    /// <summary>false：游戏不发射自己的弹幕（在这里用 source 造自己的）；source 可能是 null 包装。</summary>
    public virtual bool Shoot(Player player, EntitySource_ItemUse_WithAmmo? source, Vector2 position, Vector2 velocity,
                              int type, int damage, float knockback) => true;

    /// <summary>这次挥击的碰撞盒（可写引用）；noHitbox = true 关掉伤害。</summary>
    public virtual void UseItemHitbox(Player player, ref Rectangle hitbox, ref bool noHitbox) { }

    /// <summary>近战挥出时；hitbox 是这一次的碰撞盒。</summary>
    public virtual void MeleeEffects(Player player, Rectangle hitbox) { }

    /// <summary>物品体积的倍率（同 TML）。</summary>
    public virtual void ModifyItemScale(Player player, ref float scale) { }

    // ===================== 命中 =====================

    /// <summary>null 保留原版命中规则，false 否决，true 允许当前近战目标。</summary>
    public virtual bool? CanHitNPC(Player player, NPC target) => null;

    /// <summary>允许额外近战碰撞时扩展本次判定矩形，不改变后续目标的判定。</summary>
    public virtual bool? CanMeleeAttackCollideWithNPC(Rectangle meleeAttackHitbox, Player player, NPC target) => null;

    /// <summary>目标防御修饰之后、原版扣血之前修改本次命中。</summary>
    public virtual void ModifyHitNPC(Player player, NPC target, ref HitModifiers modifiers) { }

    /// <summary>原版确认命中后通知，damageDone 为目标实际可扣除的生命。</summary>
    public virtual void OnHitNPC(Player player, NPC target, HitInfo hit, int damageDone) { }

    /// <summary>false 否决当前武器的近战玩家目标，不覆盖原版距离与阵营判定。</summary>
    public virtual bool CanHitPvp(Player player, Player target) => true;

    /// <summary>玩家防御计算之前修改此武器造成的 PvP 伤害。</summary>
    public virtual void ModifyHitPvp(Player player, Player target, ref HurtModifiers modifiers) { }

    /// <summary>原版实际造成 PvP 伤害之后通知武器。</summary>
    public virtual void OnHitPvp(Player player, Player target, HurtInfo info) { }

    // ===================== 装备 =====================

    /// <summary>装备着时每帧一次（盔甲或饰品）。</summary>
    public virtual void UpdateEquip(Player player) { }

    /// <summary>饰品装备着时每帧一次；hideVisual = 关了显示。</summary>
    public virtual void UpdateAccessory(Player player, bool hideVisual) { }

    /// <summary>饰品在时装栏里。</summary>
    public virtual void UpdateVanity(Player player) { }

    /// <summary>这件衣服画到哪个槽：equipSlot 是槽，robes = true 表示长袍（占腿）。</summary>
    public virtual void SetMatch(bool male, ref int equipSlot, ref bool robes) { }

    /// <summary>穿着这张贴图时每帧一次。</summary>
    public virtual void EquipFrameEffects(Player player, EquipType type) { }

    /// <summary>每件穿着的 mod 衣服都会问；true = 这套齐了。</summary>
    public virtual bool IsArmorSet(Item head, Item body, Item legs) => false;

    /// <summary>套装效果；文字写 player 的 setBonus。</summary>
    public virtual void UpdateArmorSet(Player player) { }

    /// <summary>时装套装判定，收的是画出来的槽位；默认问这些槽位物品的 IsArmorSet。</summary>
    public virtual bool IsVanitySet(int head, int body, int legs) =>
        IsArmorSet(Sample(SlotItemType(Item.headType, head)), Sample(SlotItemType(Item.bodyType, body)),
                   Sample(SlotItemType(Item.legType, legs)));

    /// <summary>FrameEffects 之前，时装套生效时。</summary>
    public virtual void PreUpdateVanitySet(Player player) { }

    /// <summary>FrameEffects 之后，时装套生效时。</summary>
    public virtual void UpdateVanitySet(Player player) { }

    /// <summary>这套的影子（armorEffectDraw* 那几个字段）。</summary>
    public virtual void ArmorSetShadows(Player player) { }

    /// <summary>画盔甲时的颜色（可写引用）。经装备贴图分派；装备贴图注册入口删除后等基线装备 API 移植再接。</summary>
    public virtual void DrawArmorColor(Player drawPlayer, float shadow, ref Color color, ref int glowMask,
                                       ref Color glowMaskColor) { }

    /// <summary>画手臂盔甲的辉光（可写引用）。经装备贴图分派；同 DrawArmorColor 待基线装备 API。</summary>
    public virtual void ArmorArmGlowMask(Player drawPlayer, float shadow, ref int glowMask, ref Color color) { }

    /// <summary>空中这对翅膀的五个可写引用。</summary>
    public virtual void VerticalWingSpeeds(Player player, ref float ascentWhenFalling, ref float ascentWhenRising,
                                           ref float maxCanAscendMultiplier, ref float maxAscentMultiplier,
                                           ref float constantAscend) { }

    /// <summary>空中平飞的加速度和速度（两个可写引用）。</summary>
    public virtual void HorizontalWingSpeeds(Player player, ref float speed, ref float acceleration) { }

    /// <summary>true：翅膀由 mod 自己动画（游戏的 WingFrame 不跑）。</summary>
    public virtual bool WingUpdate(Player player, bool inUse) => false;

    /// <summary>false：这个饰品槽装不上。</summary>
    public virtual bool CanEquipAccessory(Player player, int slot, bool modded) => true;

    /// <summary>false：这对饰品不能同时装（拖进槽和点着换都问）。</summary>
    public virtual bool CanAccessoryBeEquippedWith(Item equippedItem, Item incomingItem, Player player) => true;

    // ===================== 回复 =====================

    /// <summary>回的血量（可写引用），基准是正的回血量。</summary>
    public virtual void GetHealLife(Player player, bool quickHeal, ref int healValue) { }

    /// <summary>回的魔量（可写引用）。</summary>
    public virtual void GetHealMana(Player player, bool quickHeal, ref int healValue) { }

    /// <summary>药水延时（可写引用），在否决判定（ApplyPotionDelay）之前。</summary>
    public virtual void ModifyPotionDelay(Player player, ref int baseDelay) { }

    /// <summary>false：不加计时也不给药水病，回血和消耗照旧。</summary>
    public virtual bool ApplyPotionDelay(Player player, int potionDelay) => true;

    // ===================== 拾取 =====================

    /// <summary>吸取范围，像素（可写引用，在游戏的范围上加）。</summary>
    public virtual void GrabRange(Player player, ref int grabRange) { }

    /// <summary>true：由 mod 自己把物品移过去（改 velocity），游戏不吸。</summary>
    public virtual bool GrabStyle(Player player) => false;

    /// <summary>false：玩家既不捡也不吸这个物品（同 TML）。</summary>
    public virtual bool CanPickup(Player player) => true;

    /// <summary>被捡起时；false = 物品直接消失，不进背包（红心、经验珠）。</summary>
    public virtual bool OnPickup(Player player) => true;

    /// <summary>true：背包满了也能捡（配 OnPickup 消耗掉）。</summary>
    public virtual bool ItemSpace(Player player) => false;

    // ===================== 前缀 =====================

    /// <summary>吃近战前缀（有伤害的近战武器）。</summary>
    public virtual bool MeleePrefix() => Item.melee && !Item.noUseGraphic;

    /// <summary>吃武器类前缀（noUseGraphic 的近战物品）。</summary>
    public virtual bool WeaponPrefix() => Item.melee && Item.noUseGraphic;

    /// <summary>吃远程前缀。</summary>
    public virtual bool RangedPrefix() => Item.ranged;

    /// <summary>吃魔法前缀。</summary>
    public virtual bool MagicPrefix() => Item.magic;

    /// <summary>吃召唤前缀。</summary>
    public virtual bool SummonPrefix() => Item.summon;

    /// <summary>false：这个前缀不在候选里。</summary>
    public virtual bool AllowPrefix(int pre) => true;

    /// <summary>掷前缀时强制的那个（&gt; 0），或 -1。</summary>
    public virtual int ChoosePrefix(UnifiedRandom rand) => -1;

    /// <summary>true 强给，false 拦住，null = 游戏的（pre：-1 制作/箱子，-2 重铸）。</summary>
    public virtual bool? PrefixChance(int pre, UnifiedRandom rand) => null;

    // ===================== 右键开袋 =====================

    /// <summary>true：这物品能在背包里打开（袋子、箱子）。</summary>
    public virtual bool CanRightClick() => false;

    /// <summary>打开时（ModifyItemLoot 之前）。</summary>
    public virtual void RightClick(Player player) { }

    /// <summary>false：打开不消耗这个物品。</summary>
    public virtual bool ConsumeItem(Player player) => true;

    /// <summary>物品掉落表第一次被问时跑一次：loot.Add/Remove 改掉落。</summary>
    public virtual void ModifyItemLoot(ItemLoot itemLoot) { }

    // ===================== 绘制 =====================

    /// <summary>画这个物品用的颜色；返 null = 用原色（同 TML）。</summary>
    public virtual Color? GetAlpha(Color lightColor) => null;

    /// <summary>地上画它之前；rotation 和 scale 是可写引用，false = 不画（同 TML）。</summary>
    public virtual bool PreDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor,
                                       ref float rotation, ref float scale, int whoAmI) => true;

    /// <summary>地上画它之后（否决了也跑）。</summary>
    public virtual void PostDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor,
                                        float rotation, float scale, int whoAmI) { }

    /// <summary>背包里画它之前（收的是游戏算好的帧、原点和缩放）；false = 不画。</summary>
    public virtual bool PreDrawInInventory(SpriteBatch spriteBatch, Vector2 position, Rectangle frame,
                                           Color drawColor, Color itemColor, Vector2 origin, float scale) => true;

    /// <summary>背包里画它之后（否决了也跑）。</summary>
    public virtual void PostDrawInInventory(SpriteBatch spriteBatch, Vector2 position, Rectangle frame,
                                            Color drawColor, Color itemColor, Vector2 origin, float scale) { }

    // ===================== 网络与存档 =====================

    /// <summary>
    /// 这件物品要跟着原版物品包一起过网的那部分状态；对端按 <see cref="NetReceive"/> 同顺序读。
    /// 只在安装期登记过钩子（重写本方法）时才发，其余物品不占带宽。NetWriter 是 Pylon 对原生 writer 的安全封装。
    /// </summary>
    public virtual void NetSend(NetWriter writer) { }

    /// <summary>对端收到同一次物品包时读；顺序必须和 <see cref="NetSend"/> 一致。</summary>
    public virtual void NetReceive(NetReader reader) { }

    /// <summary>物品随玩家存档保存的自定义数据（写进侧车行；mod 卸载期间由？池原样保管）。</summary>
    public virtual void SaveData(TagCompound tag) { }

    /// <summary>读档后按保存的数据恢复（与 SaveData 配对）。</summary>
    public virtual void LoadData(TagCompound tag) { }

    /// <summary>这个编号的游戏样表那一份（IsVanitySet 用；没有就是 0 号）。</summary>
    private static Item Sample(int type) =>
        ContentSamples.ItemsByType.TryGetValue(type, out var sample) ? sample : ContentSamples.ItemsByType[0];

    /// <summary>画出来的槽位 → 那件衣服的编号（越界和 0 都按 0 号算）。</summary>
    private static int SlotItemType(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> table, int slot) =>
        table[slot > 0 && slot < table.Length ? slot : 0];

    private static ArmorSetBonus.ArmorSetEffect VanillaArmorSetEffect()
    {
        foreach (var set in ArmorSetBonuses.All)
            if (set.Head == ItemID.PumpkinHelmet) return set.Effect;

        throw new InvalidOperationException("找不到游戏自己的南瓜盔甲套装");
    }
}

// 分派未接的声明钩子见各方法注释；接分派时同步 HookId（追加区）与调用点。
// NetSend/NetReceive 用 Pylon 的 NetWriter/NetReader（原生 writer 的安全封装），形状与基准的 BinaryWriter 等价。
