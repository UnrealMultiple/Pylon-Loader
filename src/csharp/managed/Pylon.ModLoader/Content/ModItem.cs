using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Utilities;

namespace Pylon.ModLoader
{
    /// <summary>自定义物品：一个类 = 一个物品类型，每个地上/背包里的实例对应一个本类实例。</summary>
    public abstract class ModItem : ModType
    {
        /// <summary>这套游戏的物品堆叠上限。</summary>
        public const int CommonMaxStack = 9999;

        /// <summary>true：不在 Mod 菜单里出现。</summary>
        public virtual bool HideFromModMenu => false;

        /// <summary>名字（空 = 用 ItemName.&lt;类名&gt; 的本地化键）。</summary>
        public virtual string DisplayName => Name;

        /// <summary>描述（空 = 用 ItemTooltip.&lt;类名&gt;）；多行用 \n。</summary>
        public virtual string Tooltip => null;

        /// <summary>ModifyTooltipLines 改的那几行（由本地化管线填好）。</summary>
        public List<string> TooltipLines { get; } = new();

        /// <summary>贴图相对 Mod 目录的路径；注册器按类名定，这里由管线接。</summary>
        public override string TexturePath => $"assets/items/{Name}.png";

        /// <summary>这个实例对应的游戏物品；注册期的模板上为 null。</summary>
        public Item Item { get; internal set; }

        /// <summary>游戏复制了这个物品；返回副本对应的实例（由管线接管）。</summary>
        public virtual ModItem Clone(Item newItem) => this;

        /// <summary>true：新物品拿模板的副本（Clone），不是新实例。</summary>
        public virtual bool CloneNewInstances => false;

        /// <summary>给一个新生实体造对应的实例（管线调）。</summary>
        public virtual ModItem NewInstance(Item entity) => CloneNewInstances ? Clone(entity) : this;

        /// <summary>注册时一次（AddEquipTexture 之类放这里）。</summary>
        public virtual void Load() { }

        /// <summary>这一类型的每个物品生成时跑；数值都写在这。</summary>
        public virtual void SetDefaults(Item item) { }

        /// <summary>SetStaticDefaults 之后紧接着。</summary>
        public virtual void PostStaticDefaults() { }

        /// <summary>SetDefaults 之后紧接着。</summary>
        public virtual void PostSetDefaults(Item item) { }

        /// <summary>所有内容安装完成之后一次（跨 Mod 引用放这里）。</summary>
        public virtual void PostSetupContent() { }

        /// <summary>每种语言一次，TooltipLines 已填好；直接改它。</summary>
        public virtual void ModifyTooltipLines() { }

        /// <summary>每次 tooltip 显示时一次（合成指南弹窗里也会，不带颜色）；直接改 tooltips 列表。</summary>
        public virtual void ModifyTooltips(Item item, List<TooltipLine> tooltips) { }

        /// <summary>画整个 tooltip 前；改 x/y 移动所有行，false = 一行都不画。</summary>
        public virtual bool PreDrawTooltip(Item item, List<TooltipLine> lines, ref float x, ref float y) => true;

        /// <summary>所有行画完后（收 DrawableTooltipLine）。</summary>
        public virtual void PostDrawTooltip(Item item, DrawableTooltipLine[] lines) { }

        /// <summary>画这一行前；改 line 的 X/Y/字体/颜色/缩放，yOffset 加在后面所有行上；false = 这行不画。</summary>
        public virtual bool PreDrawTooltipLine(Item item, DrawableTooltipLine line, ref float yOffset) => true;

        /// <summary>这一行画完后（没画也跑）。</summary>
        public virtual void PostDrawTooltipLine(Item item, DrawableTooltipLine line) { }

        // SaveData/LoadData/NetSend/NetReceive、AddRecipeGroups/AddRecipes、CreateRecipe/CreateRecipeGroup 没翻：存档/网络/配方注册都在各自管线里
        /// <summary>在制作菜单做出这个物品时；item 是玩家将拿到的那份，还能改。</summary>
        public virtual void OnCraft(Item item, Player player, Recipe recipe) { }

        /// <summary>false：这一次用不了这个物品。</summary>
        public virtual bool CanUseItem(Item item, Player player) => true;

        /// <summary>每帧使用；返回 true = 物品算用过了（消耗品会被吃掉）。</summary>
        public virtual bool UseItem(Item item, Player player) => false;

        /// <summary>true：这物品能在背包里打开（袋子、箱子）。</summary>
        public virtual bool CanRightClick(Item item) => false;

        /// <summary>打开时（ModifyItemLoot 之前）。</summary>
        public virtual void RightClick(Item item, Player player) { }

        /// <summary>物品掉落表第一次被问时跑一次：loot.Add/Remove 改掉落。</summary>
        public virtual void ModifyItemLoot(ItemLoot itemLoot) { }
        /// <summary>false：打开不消耗这个物品。</summary>
        public virtual bool ConsumeItem(Item item, Player player) => true;

        /// <summary>true：是渔夫的任务鱼。</summary>
        public virtual bool IsQuestFish() => false;

        /// <summary>false：每天的抽签不会落在它身上。</summary>
        public virtual bool IsAnglerQuestAvailable() => true;

        /// <summary>渔夫说的那句话（description）和去哪儿钓（catchLocation）。</summary>
        public virtual void AnglerQuestChat(ref string description, ref string catchLocation) { }

        /// <summary>手里拿着时每帧一次。</summary>
        public virtual void HoldItem(Item item, Player player) { }

        /// <summary>使用时的样子；游戏自己的样式之后跑。</summary>
        public virtual void UseStyle(Item item, Player player, float mountOffset, Rectangle heldItemFrame) { }

        /// <summary>握着时的样子；游戏自己的样式之后跑。</summary>
        public virtual void HoldStyle(Item item, Player player, float mountOffset, Rectangle heldItemFrame) { }

        /// <summary>手上武器的位移，像素；返 null = 不动。</summary>
        public virtual Vector2? HoldoutOffset(Item item, Player player) => null;

        /// <summary>false：用得了，但不发射。</summary>
        public virtual bool CanShoot(Item item, Player player) => true;

        /// <summary>一个弹幕发射前的参数；改字段就是改这一发。</summary>
        public sealed class ShootStats
        {
            /// <summary>出膛位置。</summary>
            public Vector2 Position;

            /// <summary>出膛速度。</summary>
            public Vector2 Velocity;

            /// <summary>弹幕类型（已经被弹药换过）。</summary>
            public int Type;

            /// <summary>伤害。</summary>
            public int Damage;

            /// <summary>击退。</summary>
            public float KnockBack;
        }

        /// <summary>每个弹幕之前：改 stats 的位置、速度、类型、伤害、击退。</summary>
        public virtual void ModifyShootStats(Item item, Player player, ShootStats stats) { }

        /// <summary>false：游戏不发射自己的弹幕（在这里造自己的）。</summary>
        public virtual bool Shoot(Item item, Player player, Vector2 position, Vector2 velocity, int type, int damage, float knockBack) => true;

        // 弹药整块（NeedsAmmo…PickAmmo 八个）没翻：本版没有 StatModifier 和按类型弹药回调，要照 Player.PickAmmo 实际形状重设计
        /// <summary>回的血量（可写引用），基准是正的回血量。</summary>
        public virtual void GetHealLife(Item item, Player player, bool quickHeal, ref int healValue) { }

        /// <summary>回的魔量（可写引用）。</summary>
        public virtual void GetHealMana(Item item, Player player, bool quickHeal, ref int healValue) { }

        /// <summary>药水延时（可写引用），在游戏判否决和应用之前。</summary>
        public virtual void ModifyPotionDelay(Item item, Player player, ref int baseDelay) { }

        /// <summary>false：不加计时也不给药水病，回血和消耗照旧。</summary>
        public virtual bool ApplyPotionDelay(Item item, Player player, int potionDelay) => true;

        /// <summary>武器的伤害修正。</summary>
        public virtual void ModifyWeaponDamage(Item item, Player player, ref int damage) { }

        /// <summary>武器的暴击修正（可写引用）。</summary>
        public virtual void ModifyWeaponCrit(Item item, Player player, ref int crit) { }

        /// <summary>武器的击退修正。</summary>
        public virtual void ModifyWeaponKnockback(Item item, Player player, ref float knockback) { }

        /// <summary>物品体积的倍率（可写引用，含手套那层）。</summary>
        public virtual void ModifyItemScale(Item item, Player player, ref float scale) { }

        // 命中整块（CanHitNPC/ModifyHitNPC/OnHitNPC、PvP 三个、近战碰撞/特效、UseItemHitbox）没翻：要的 HitModifiers/HitInfo 本版没有，等管线定形
        /// <summary>装备着时每帧一次（盔甲或饰品）。</summary>
        public virtual void UpdateEquip(Item item, Player player) { }

        /// <summary>饰品装备着时每帧一次；vanity = 在时装栏，hideVisual = 关了显示。</summary>
        public virtual void UpdateAccessory(Item item, Player player, bool vanity, bool hideVisual) { }

        /// <summary>false：这对饰品不能同时装（拖进槽和点着换都问）。</summary>
        public virtual bool CanAccessoryBeEquippedWith(Item equippedItem, Item incomingItem, Player player) => true;

        /// <summary>饰品在时装栏里。</summary>
        public virtual void UpdateVanity(Item item, Player player) { }

        /// <summary>登记盔甲套装一次（在内容就绪阶段）。</summary>
        public virtual void AddArmorSets() { }

        /// <summary>登记一套盔甲（1.4.5 那套）：text 是本地化键或原文，0 = 哪件都行。</summary>
        public void CreateArmorSet(int head, int body, int legs, string text = "ArmorSetBonus.Empty",
                                   ArmorSetBonus.PartType primaryPart = ArmorSetBonus.PartType.None)
        {
            ArmorSetBonuses.Add(VanillaArmorSetEffect(), text, primaryPart, head, body, legs);

            // 游戏的 BuildLookup 已经跑过：新套装要手工挂回每件衣服的包含表
            var set = ArmorSetBonuses.All[ArmorSetBonuses.All.Count - 1];
            var types = new HashSet<int>();
            if (head > 0) types.Add(head);
            if (body > 0) types.Add(body);
            if (legs > 0) types.Add(legs);

            foreach (var type in types)
            {
                var old = ArmorSetBonuses.SetsContaining[type];
                var length = old == null ? 0 : old.Length;
                var grown = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<ArmorSetBonus>(length + 1);
                for (var i = 0; i < length; i++) grown[i] = old[i];
                grown[length] = set;
                ArmorSetBonuses.SetsContaining[type] = grown;
            }
        }

        /// <summary>一次登记一批：三个列表的笛卡尔积。</summary>
        public void CreateArmorSets(int[] heads, int[] bodies, int[] legs, string text = "ArmorSetBonus.Empty",
                                    ArmorSetBonus.PartType primaryPart = ArmorSetBonus.PartType.None)
        {
            heads ??= new[] { 0 };
            bodies ??= new[] { 0 };
            legs ??= new[] { 0 };

            foreach (var head in heads)
                foreach (var body in bodies)
                    foreach (var leg in legs) CreateArmorSet(head, body, leg, text, primaryPart);
        }

        /// <summary>每件穿着的 mod 衣服都会问；true = 这套齐了。</summary>
        public virtual bool IsArmorSet(Item head, Item body, Item legs) => false;

        /// <summary>套装效果；文字写 player 的 setBonus。</summary>
        public virtual void UpdateArmorSet(Item item, Player player) { }

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

        /// <summary>这件衣服画到哪个槽：equipSlot 是槽，robes = true 表示长袍（占腿）。</summary>
        public virtual void SetMatch(bool male, ref int equipSlot, ref bool robes) { }

        /// <summary>穿着这张贴图时每帧一次；type 是 EquipType。</summary>
        public virtual void EquipFrameEffects(Player player, int type) { }

        /// <summary>空中这对翅膀的五个可写引用。</summary>
        public virtual void VerticalWingSpeeds(Item item, Player player, ref float ascentWhenFalling, ref float ascentWhenRising,
                                               ref float maxCanAscendMultiplier, ref float maxAscentMultiplier, ref float constantAscend) { }

        /// <summary>空中平飞的加速度和速度（两个可写引用）。</summary>
        public virtual void HorizontalWingSpeeds(Item item, Player player, ref float speed, ref float acceleration) { }

        /// <summary>true：翅膀由 mod 自己动画（游戏的 WingFrame 不跑）。</summary>
        public virtual bool WingUpdate(Player player, bool inUse) => false;

        /// <summary>这对翅膀的 WingStats（在 SetStaticDefaults 里用，像 TL 的 ExMod）。</summary>
        public void SetWingStats(int flyTime = 100, float flySpeedOverride = -1f, float accelerationMultiplier = 1f,
                                 bool hasHoldDownHoverFeatures = false, float hoverFlySpeedOverride = -1f,
                                 float hoverAccelerationMultiplier = 1f)
        {
            var slot = Item != null ? Item.wingSlot : -1;
            if (slot <= 0) throw new InvalidOperationException($"{GetType().Name}.SetWingStats: 这物品没有翅膀（{TexturePath}）");

            var stats = ArmorIDs.Wing.Sets.Stats[slot];
            stats.FlyTime = flyTime;
            stats.AccRunSpeedOverride = flySpeedOverride;
            stats.AccRunAccelerationMult = accelerationMultiplier;
            stats.HasDownHoverStats = hasHoldDownHoverFeatures;
            stats.DownHoverSpeedOverride = hoverFlySpeedOverride;
            stats.DownHoverAccelerationMult = hoverAccelerationMultiplier;
        }

        /// <summary>每帧，在背包里。</summary>
        public virtual void UpdateInventory(Item item, Player player) { }

        /// <summary>吃近战前缀（有伤害的近战武器）。</summary>
        public virtual bool MeleePrefix(Item item) => item != null && item.melee && !item.noUseGraphic;

        /// <summary>吃武器类前缀（noUseGraphic 的近战物品）。</summary>
        public virtual bool WeaponPrefix(Item item) => item != null && item.melee && item.noUseGraphic;

        /// <summary>吃远程前缀。</summary>
        public virtual bool RangedPrefix(Item item) => item != null && item.ranged;

        /// <summary>吃魔法前缀。</summary>
        public virtual bool MagicPrefix(Item item) => item != null && item.magic;

        /// <summary>吃召唤前缀。</summary>
        public virtual bool SummonPrefix(Item item) => item != null && item.summon;

        /// <summary>掷前缀时强制的那个（&gt; 0），或 -1。</summary>
        public virtual int ChoosePrefix(Item item, UnifiedRandom rand) => -1;

        /// <summary>true 强给，false 拦住，null = 游戏的（pre：-1 制作/箱子，-2 重铸）。</summary>
        public virtual bool? PrefixChance(Item item, int pre, UnifiedRandom rand) => null;

        /// <summary>false：这个前缀不在候选里。</summary>
        public virtual bool AllowPrefix(Item item, int pre) => true;

        /// <summary>前缀的数值之后跑。</summary>
        public virtual void ApplyPrefix(Item item, int pre) { }

        /// <summary>画这个物品用的颜色；返 null = 用原色。地上时 item 是 WorldItem。</summary>
        public virtual Color? GetAlpha(Item item, Color lightColor) => null;

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

        /// <summary>地上时每帧一次（item 是 WorldItem）；光照和尘土放这。</summary>
        public virtual void PostUpdate(WorldItem item) { }

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

        /// <summary>鱼线从哪儿出来、什么颜色（两个可写引用，像素）。</summary>
        public virtual void ModifyFishingLine(Item item, Projectile bobber, ref Vector2 lineOriginOffset, ref Color lineColor) { }

        /// <summary>照抄一个原版物品的字段（游戏这个版本没有 Item.CloneDefaults）。</summary>
        public void CloneDefaults(int type)
        {
            var source = new Item();
            source.SetDefaults(type, null);

            foreach (var property in typeof(Item).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || !property.CanWrite || property.Name == "type") continue;
                if (property.PropertyType.IsArray || typeof(Delegate).IsAssignableFrom(property.PropertyType)) continue;

                try { property.SetValue(Item, property.GetValue(source)); }
                catch (Exception) { }
            }
        }

        /// <summary>使用时间、动画长度、自动连发和最合适的 useStyle。
        /// ⚠ 坑（手机版）：读 item.shoot 定样式——shoot&gt;0 且非消耗品 → Shoot（远程，走 ItemCheck_Shoot 路径，ItemLoader 才能接管）；
        /// 否则 Swing（近战，剑气走别的路径、依赖 ai）。必须先设 item.shoot 再调本方法，顺序反了拿 0 → 误判 Swing → 弹幕永不发射。</summary>
        public void SetDefaultWeaponStyle(int useTime = 30, bool autoReuse = false)
        {
            var item = Item;
            item.useTime = useTime;
            item.useAnimation = useTime;
            item.autoReuse = autoReuse;

            if (item.melee) item.useStyle = (byte)ItemUseStyleID.Swing;
            else if (item.shoot > 0 && !item.consumable) item.useStyle = (byte)ItemUseStyleID.Shoot;
            else item.useStyle = (byte)ItemUseStyleID.Swing;
        }

        /// <summary>伤害、击退、暴击三个一起写。</summary>
        public void SetWeaponValues(int damage = 0, float knockBack = 0f, int crit = 0)
        {
            Item.damage = damage;
            Item.knockBack = knockBack;
            Item.crit = crit;
        }

        /// <summary>稀有度和价格。</summary>
        public void SetShopValues(int rarity = 0, int coinValue = 0)
        {
            Item.rare = rarity;
            Item.value = coinValue;
        }

        /// <summary>一个放方块的物品。</summary>
        public void DefaultToPlaceableTile(int typeToPlace, int styleToPlace = 0)
        {
            var item = Item;
            item.createTile = typeToPlace;
            item.placeStyle = styleToPlace;
            item.useStyle = (byte)ItemUseStyleID.Swing;
            item.useAnimation = 15;
            item.useTime = 10;
            item.maxStack = CommonMaxStack;
            item.useTurn = true;
            item.autoReuse = true;
            item.consumable = true;
        }

        /// <summary>一个放墙的物品。</summary>
        public void DefaultToPlaceableWall(ushort wallToPlace)
        {
            Item.DefaultToPlaceableWall(wallToPlace);
            Item.createWall = wallToPlace;
        }

        /// <summary>游戏的火把（举着、发光、上墙），放的是 mod 的 tile。</summary>
        public void DefaultToTorch(int tileType, int styleToPlace = 0, bool allowWaterPlacement = false)
        {
            Item.DefaultToTorch(styleToPlace, allowWaterPlacement);
            Item.createTile = tileType;
        }

        /// <summary>游戏的音乐盒，放的是 mod 的 tile。</summary>
        public void DefaultToMusicBox(int tileType, int styleToPlace = 0)
        {
            Item.DefaultToMusicBox(styleToPlace);
            Item.createTile = tileType;
            Item.placeStyle = styleToPlace;
        }

        /// <summary>一条鞭子。</summary>
        public void DefaultToWhip(int projType, int damage, float knockBack, float shootSpeed, int animationTime = 30) =>
            Item.DefaultToWhip(projType, damage, knockBack, shootSpeed, animationTime);

        /// <summary>一把长矛。</summary>
        public void DefaultToSpear(int projType, float pushForwardSpeed, int animationTime) =>
            Item.DefaultToSpear(projType, pushForwardSpeed, animationTime);

        /// <summary>一个高尔夫球。</summary>
        public void DefaultToGolfBall(int projType) => Item.DefaultToGolfBall(projType);

        /// <summary>白金/金/银/铜拼出来的卖价（单位铜）。</summary>
        public static int SellPrice(int platinum = 0, int gold = 0, int silver = 0, int copper = 0) =>
            Item.sellPrice(platinum, gold, silver, copper);

        /// <summary>白金/金/银/铜拼出来的买价（单位铜）。</summary>
        public static int BuyPrice(int platinum = 0, int gold = 0, int silver = 0, int copper = 0) =>
            Item.buyPrice(platinum, gold, silver, copper);

        /// <summary>这个编号是不是某个 mod 物品。</summary>
        public static bool IsModType(int type) => ModContent.IsCustom(ContentKind.Item, type);

        /// <summary>这物品是不是某个 mod 物品。</summary>
        public static bool IsModItem(Item item) => item != null && IsModType(item.type);

        /// <summary>这个编号的游戏样表那一份（IsVanitySet 用；没有就是 0 号）。</summary>
        private static Item Sample(int type) =>
            ContentSamples.ItemsByType.TryGetValue(type, out var sample) ? sample : ContentSamples.ItemsByType[0];

        /// <summary>画出来的槽位 → 那件衣服的编号（越界和 0 都按 0 号算）。</summary>
        private static int SlotItemType(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> table, int slot) =>
            table[slot > 0 && slot < table.Length ? slot : 0];

        /// <summary>借游戏自己的套装效果当占位（跟参考里借南瓜套一模一样的做法）。</summary>
        private static ArmorSetBonus.ArmorSetEffect VanillaArmorSetEffect()
        {
            foreach (var set in ArmorSetBonuses.All)
                if (set.Head == ItemID.PumpkinHelmet) return set.Effect;

            throw new InvalidOperationException("找不到游戏自己的南瓜盔甲套装");
        }
    }
}
