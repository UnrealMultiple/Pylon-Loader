using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.Utilities;

namespace Pylon.ModLoader
{
    /// <summary>全局物品：对游戏自己的物品（和 mod 的）也跑的一套钩子；一个类一份模板，AppliesToEntity 挑哪些物品。</summary>
    public abstract class GlobalItem : ModType
    {
        /// <summary>true：每个物品一份自己的副本（Item.Clone 会把状态一起带走）。</summary>
        public virtual bool InstancePerEntity => false;

        /// <summary>false：这套钩子不跑在这个物品上；lateInstantiation = 在 SetDefaults 之后（类型已定）。</summary>
        public virtual bool AppliesToEntity(Item entity, bool lateInstantiation) => true;

        /// <summary>复制这套钩子实例的字段；物品流程里由 Item.Clone 调（管线接管）。</summary>
        public virtual GlobalItem Clone(Item from, Item to) => this;

        /// <summary>新物品的副本；默认就是模板自己。</summary>
        public virtual GlobalItem NewInstance(Item target) => this;

        /// <summary>物品生成或换类型时（游戏的 SetDefaults 之后）。</summary>
        public virtual void SetDefaults(Item item) { }

        /// <summary>false：这一次用不了这个物品。</summary>
        public virtual bool CanUseItem(Item item, Player player) => true;

        /// <summary>使用开始时；true = 物品算用过了（消耗品会被吃掉）。</summary>
        public virtual bool UseItem(Item item, Player player) => false;

        /// <summary>使用时的样子；游戏自己的样式和 ModItem 的之后跑。</summary>
        public virtual void UseStyle(Item item, Player player, float mountOffset, Rectangle heldItemFrame) { }

        /// <summary>握着时的样子；游戏自己的样式和 ModItem 的之后跑。</summary>
        public virtual void HoldStyle(Item item, Player player, float mountOffset, Rectangle heldItemFrame) { }

        /// <summary>手里拿着时每帧一次。</summary>
        public virtual void HoldItem(Item item, Player player) { }

        /// <summary>武器的伤害修正（可写引用）。</summary>
        public virtual void ModifyWeaponDamage(Item item, Player player, ref int damage) { }

        // 故意没翻（ModifyWeaponCrit/ModifyWeaponKnockback/ModifyItemScale）：crit 和 scale 是 Ref 能翻，knockback 是 StatModifier，本版没有，等伤害/击退管线定形再翻

        // 故意没翻（UseItemHitbox）：收 noHitbox（ref bool）和 hitbox（golpe 的 Rectangle），和 ModItem 的同一批

        /// <summary>近战挥击的特效；hitbox 是这次挥击的矩形。</summary>
        public virtual void MeleeEffects(Item item, Player player, Rectangle hitbox) { }

        /// <summary>使用动画的长度（可写引用）。</summary>
        public virtual void UseAnimation(Item item, Player player, ref int useAnimation) { }

        /// <summary>使用时画哪一帧（可写引用）。</summary>
        public virtual void UseItemFrame(Item item, Player player, ref int frame) { }

        /// <summary>握着时画哪一帧（可写引用）。</summary>
        public virtual void HoldItemFrame(Item item, Player player, ref int frame) { }

        // 故意没翻（弹药整块：NeedsAmmo/CanChooseAmmo/CanBeChosenAsAmmo/CanConsumeAmmo/CanBeConsumedAsAmmo/OnConsumeAmmo/OnConsumedAsAmmo/PickAmmo）：
        // 本版没有 StatModifier 和按类型的弹药回调，和 ModItem 的弹药块一起等 Player.PickAmmo 的形状

        /// <summary>回的血量（可写引用），基准是正的回血量。</summary>
        public virtual void GetHealLife(Item item, Player player, bool quickHeal, ref int healValue) { }

        /// <summary>回的魔量（可写引用）。</summary>
        public virtual void GetHealMana(Item item, Player player, bool quickHeal, ref int healValue) { }

        /// <summary>药水延时（可写引用），在游戏判否决和应用之前。</summary>
        public virtual void ModifyPotionDelay(Item item, Player player, ref int baseDelay) { }

        /// <summary>false：不加计时也不给药水病，回血和消耗照旧。</summary>
        public virtual bool ApplyPotionDelay(Item item, Player player, int potionDelay) => true;

        /// <summary>false：用得了，但不发射。</summary>
        public virtual bool CanShoot(Item item, Player player) => true;

        /// <summary>每个弹幕之前：改 stats 的位置、速度、类型、伤害、击退。</summary>
        public virtual void ModifyShootStats(Item item, Player player, ModItem.ShootStats stats) { }

        /// <summary>false：游戏不发射自己的弹幕（在这里造自己的）；source 是这一发的来源。</summary>
        public virtual bool Shoot(Item item, Player player, Vector2 position, Vector2 velocity, int type, int damage,
                                  float knockBack, IEntitySource source) => true;

        // 故意没翻（命中那套：CanHitNPC/CanMeleeAttackCollideWithNPC/ModifyHitNPC/OnHitNPC/CanHitPvp/ModifyHitPvp/OnHitPvp）：
        // 本版没有 HitModifiers/HurtModifiers/HitInfo，和 ModItem 的命中块一起等 CombatLoader 管线定形

        /// <summary>每帧，在背包里（58 个格子）。</summary>
        public virtual void UpdateInventory(Item item, Player player) { }

        /// <summary>装备着时每帧一次（盔甲或饰品）。</summary>
        public virtual void UpdateEquip(Item item, Player player) { }

        /// <summary>饰品装备着时每帧一次；vanity = 在时装栏，hideVisual = 关了显示。</summary>
        public virtual void UpdateAccessory(Item item, Player player, bool vanity, bool hideVisual) { }

        /// <summary>地上物品的更新之前；false = 这一帧跳过游戏的更新（Post 照跑）。</summary>
        public virtual bool PreUpdateInWorld(Item item, WorldItem worldItem) => true;

        /// <summary>地上物品的更新之后。</summary>
        public virtual void PostUpdateInWorld(Item item, WorldItem worldItem) { }

        /// <summary>false：玩家既不捡也不吸这个物品。</summary>
        public virtual bool CanPickup(WorldItem worldItem, Player player) => true;

        /// <summary>被捡起时；false = 物品直接消失，不进背包（红心、经验珠）。</summary>
        public virtual bool OnPickup(WorldItem worldItem, Player player) => true;

        /// <summary>true：背包满了也能捡（配 OnPickup 消耗掉）。</summary>
        public virtual bool ItemSpace(Item item, Player player) => false;

        /// <summary>吸取范围，像素（可写引用，在游戏的范围上加）。</summary>
        public virtual void GrabRange(WorldItem worldItem, Player player, ref int grabRange) { }

        /// <summary>true：由 mod 自己把物品移过去（改 worldItem.velocity），游戏不吸。</summary>
        public virtual bool GrabStyle(WorldItem worldItem, Player player) => false;

        /// <summary>在制作菜单做出这个物品时；item 是玩家将拿到的那份，还能改。</summary>
        public virtual void OnCraft(Item item, Player player, Recipe recipe) { }

        /// <summary>地上画它之前；rotation 和 scale 是可写引用，false = 不画。</summary>
        public virtual bool PreDrawInWorld(Item item, SpriteBatch spriteBatch, Color lightColor, Color alphaColor,
                                           ref float rotation, ref float scale, int whoAmI) => true;

        /// <summary>地上画它之后（否决了也跑）。</summary>
        public virtual void PostDrawInWorld(Item item, SpriteBatch spriteBatch, Color lightColor, Color alphaColor,
                                            float rotation, float scale, int whoAmI) { }

        /// <summary>背包里画它之前（收的是游戏算好的帧和缩放）；false = 不画。</summary>
        public virtual bool PreDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position, Rectangle frame,
                                               Color drawColor, Color itemColor, float scale) => true;

        /// <summary>背包里画它之后（否决了也跑）。</summary>
        public virtual void PostDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position, Rectangle frame,
                                                Color drawColor, Color itemColor, float scale) { }

        /// <summary>每次 tooltip 显示时一次（合成指南弹窗里也会，不带颜色）；ModItem 的之后跑。</summary>
        public virtual void ModifyTooltips(Item item, List<TooltipLine> tooltips) { }

        /// <summary>画整个 tooltip 前；改 x/y 移动所有行，false = 一行都不画。</summary>
        public virtual bool PreDrawTooltip(Item item, List<TooltipLine> lines, ref float x, ref float y) => true;

        /// <summary>所有行画完后（收 DrawableTooltipLine）。</summary>
        public virtual void PostDrawTooltip(Item item, DrawableTooltipLine[] lines) { }

        /// <summary>画这一行前；改 line 的 X/Y/字体/颜色/缩放，yOffset 加在后面所有行上；false = 这行不画。</summary>
        public virtual bool PreDrawTooltipLine(Item item, DrawableTooltipLine line, ref float yOffset) => true;

        /// <summary>这一行画完后（没画也跑）。</summary>
        public virtual void PostDrawTooltipLine(Item item, DrawableTooltipLine line) { }

        /// <summary>掷前缀时强制的那个（&gt; 0），或 -1；这里比 ModItem 的先跑。</summary>
        public virtual int ChoosePrefix(Item item, UnifiedRandom rand) => -1;

        /// <summary>true 强给，false 拦住，null = 游戏的（pre：-1 制作/箱子，-2 重铸）。</summary>
        public virtual bool? PrefixChance(Item item, int pre, UnifiedRandom rand) => null;

        /// <summary>false：这个前缀不在候选里。</summary>
        public virtual bool AllowPrefix(Item item, int pre) => true;

        /// <summary>前缀的数值之后跑。</summary>
        public virtual void ApplyPrefix(Item item, int pre) { }

        /// <summary>false：这对饰品不能同时装（拖进槽和点着换都问）。</summary>
        public virtual bool CanAccessoryBeEquippedWith(Item equippedItem, Item incomingItem, Player player) => true;

        /// <summary>这套是不是凑齐了；返回套装名（'' = 没凑齐）。</summary>
        public virtual string IsArmorSet(Item head, Item body, Item legs) => "";

        /// <summary>套装效果；set 是 IsArmorSet 返回的名字。</summary>
        public virtual void UpdateArmorSet(Player player, string set) { }

        /// <summary>时装套装判定，收的是画出来的槽位；默认问这些槽位物品的 IsArmorSet。</summary>
        public virtual string IsVanitySet(int head, int body, int legs) =>
            IsArmorSet(Sample(SlotItemType(Item.headType, head)), Sample(SlotItemType(Item.bodyType, body)),
                       Sample(SlotItemType(Item.legType, legs)));

        /// <summary>FrameEffects 之前，时装套生效时。</summary>
        public virtual void PreUpdateVanitySet(Player player, string set) { }

        /// <summary>FrameEffects 之后，时装套生效时。</summary>
        public virtual void UpdateVanitySet(Player player, string set) { }

        /// <summary>这套的影子（armorEffectDraw* 那几个字段）。</summary>
        public virtual void ArmorSetShadows(Player player, string set) { }

        /// <summary>部位画到哪个槽：armorSlot 0 头/1 身/2 腿，equipSlot 是槽，robes = true 表示长袍（占腿）。</summary>
        public virtual void SetMatch(int armorSlot, int type, bool male, ref int equipSlot, ref bool robes) { }

        /// <summary>空中这对翅膀的五个可写引用（item 是穿的那件，或 null）。</summary>
        public virtual void VerticalWingSpeeds(Item item, Player player, ref float ascentWhenFalling, ref float ascentWhenRising,
                                               ref float maxCanAscendMultiplier, ref float maxAscentMultiplier, ref float constantAscend) { }

        /// <summary>空中平飞的加速度和速度（两个可写引用）。</summary>
        public virtual void HorizontalWingSpeeds(Item item, Player player, ref float speed, ref float acceleration) { }

        /// <summary>true：翅膀由 mod 自己动画（游戏的 WingFrame 不跑）。</summary>
        public virtual bool WingUpdate(int wings, Player player, bool inUse) => false;

        /// <summary>这个编号的游戏样表那一份（IsVanitySet 用；没有就是 0 号）。</summary>
        private static Item Sample(int type) =>
            Terraria.ID.ContentSamples.ItemsByType.TryGetValue(type, out var sample) ? sample : Terraria.ID.ContentSamples.ItemsByType[0];

        /// <summary>画出来的槽位 → 那件衣服的编号（越界和 0 都按 0 号算）。</summary>
        private static int SlotItemType(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> table, int slot) =>
            table[slot > 0 && slot < table.Length ? slot : 0];
    }
}
