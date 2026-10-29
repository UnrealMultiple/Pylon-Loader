// 层：Mod 框架 / Content —— 自定义 NPC：一个类型一份模板（注册期 new 一次），每只游戏 NPC 一份实例（NpcEntities.Bind）。
// 钩子分派全在 Loaders/NpcLoader.cs，这里只有内容类本身、CloneDefaults 和覆写检测。
// 城镇 NPC 的搬房/名字/聊天/商店由 TownNpcLoader 接线；未翻：Happiness、BossBar、CloneNewInstances、图鉴/旗帜/头像、SpawnChance/SpawnNPC。
using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Bestiary;
using Terraria.GameContent.ItemDropRules;

namespace Pylon.ModLoader
{
    /// <summary>自定义 NPC（敌对/小动物/Boss）：一个类型一份模板，每个实体一份实例。</summary>
    public abstract class ModNpc : ModType
    {
        // ================= 覆写检测 =================

        private static readonly Dictionary<Type, OverridePlan> HookPlans = new();

        /// <summary>这个类相对 <see cref="ModNpc"/> 覆写了哪些钩子；按类算一次。不能用 <see cref="ModType.Plan"/>（基类是 ModType，会把自己声明的方法全算成覆写）；判定基类是本内容类。</summary>
        internal OverridePlan HookPlan
        {
            get
            {
                var type = GetType();
                if (HookPlans.TryGetValue(type, out var plan)) return plan;
                plan = OverridePlan.Build(type, typeof(ModNpc));
                HookPlans[type] = plan;
                return plan;
            }
        }

        /// <summary>这个钩子要不要分派给本实例。</summary>
        internal bool HasHook(string hook) => HookPlan.Has(hook);

        // ================= 字段 =================

        /// <summary>聊天栏上的两个按钮：文本留空 = 没这个按钮（手机最多显示两个）。城镇 NPC 用。</summary>
        public sealed class ChatButtons
        {
            /// <summary>第一个按钮的文字。</summary>
            public string Button = "";

            /// <summary>第二个按钮的文字。</summary>
            public string Button2 = "";
        }

        /// <summary>贴图是竖排帧条，帧数写进 <c>Main.npcFrameCount[Type]</c>。</summary>
        public override string TexturePath => $"assets/npcs/{Name}.png";

        /// <summary>从 Mod 菜单里藏掉。</summary>
        public virtual bool HideFromModMenu => false;

        /// <summary>只在跑游戏 AI 期间把 npc.type 换成它；<c>npc.aiStyle</c> 要另设（只读属性，按类固定）。</summary>
        public virtual int AIType => 0;

        /// <summary>照这个原版 NPC 的 FindFrame 动画（0 = 自定）；在 SetDefaults 里定；覆写了 <see cref="FindFrame"/> 时以自己的为准。</summary>
        public virtual int AnimationType => 0;

        /// <summary>旗帜 ID（代表 NPC 的类型）；0 = 关。旗帜管线（BannerLoader）未迁移。</summary>
        public virtual int Banner => 0;

        /// <summary>旗帜解锁的物品 ID；0 = 关。旗帜管线未迁移。</summary>
        public virtual int BannerItem => 0;

        /// <summary>显示名；空 = 用 <c>NPCName.&lt;Name&gt;</c>（ContentInstaller 写 Lang）。</summary>
        public virtual string DisplayName => "";

        /// <summary>不进图鉴。图鉴管线（BestiaryLoader）未迁移。</summary>
        public virtual bool HideFromBestiary => false;

        /// <summary>图鉴稀有度星数（1~5）；0 = 游戏按生命自己算（ContentInstaller 注册样例时用）。</summary>
        public virtual int BestiaryRarityStars => 0;

        /// <summary>图鉴里的静止图；空 = <c>&lt;Texture&gt;_BestiaryIcon</c>。图鉴管线未迁移。</summary>
        public virtual string BestiaryIcon => "";

        /// <summary>地图与房屋菜单上的头；空 = <c>&lt;Texture&gt;_Head</c>。头像管线未迁移。</summary>
        public virtual string HeadTexture => "";

        /// <summary>微光后的头；空 = <c>&lt;Texture&gt;_Shimmer_Head</c>。头像管线未迁移。</summary>
        public virtual string ShimmerHeadTexture => "";

        /// <summary>首领血条与地图图标；空 = <c>&lt;Texture&gt;_Head_Boss</c>。首领头像管线未迁移。</summary>
        public virtual string BossHeadTexture => "";

        /// <summary>靠近屏幕时放的音乐；-1 = 用游戏的。由 ModMusic 按优先级选择。</summary>
        public virtual int Music => -1;

        /// <summary>原版是 SceneEffectPriority.BossLow = 6；两个都有音乐时高的赢。</summary>
        public virtual int SceneEffectPriority => 6;

        // BossBar 不翻：要一个 ModBossBar 内容类，Pylon 侧还没有（BossBarLoader 未接）。

        // ================= 实体绑定 =================

        /// <summary>当前绑定的游戏实体（SetStaticDefaults 期间为 null）。</summary>
        public NPC NPC => BoundEntity;

        /// <summary>实体实例绑定的游戏 NPC（框架维护）。</summary>
        internal NPC BoundEntity { get; set; }

        // ================= 克隆 =================

        /// <summary>把某个原版（或另一个 mod）NPC 的默认值抄到当前 NPC 上；只能在 <see cref="SetDefaults"/> 里调，身份（type/netID）、玩家参与表、绑定实例与实体级 Global 表都还原。</summary>
        public void CloneDefaults(int type)
        {
            var npc = BoundEntity;
            if (npc == null)
            {
                Safe.Once("npc.clone.none." + Name,
                    $"CloneDefaults：{Name} 没有绑定 NPC（只能在 SetDefaults 里用）", true);
                return;
            }
            // 拒绝条件：类型非法、和自己一样、或是没注册的 mod 类型。
            if (type < 0 || type == npc.type ||
                (type >= VanillaCounts.Npc && !(ModContent.Find(ContentKind.Npc, type) is ModNpc)))
            {
                Safe.Once("npc.clone.bad." + Name + "." + type,
                    $"CloneDefaults：{Name} 的克隆类型 {type} 无效（和自己一样、为负，或不是已注册的 NPC）", true);
                return;
            }
            if (!NpcLoader.StartClone(type))
            {
                Safe.Once("npc.clone.loop." + Name + "." + type,
                    $"CloneDefaults：{Name} 出现循环克隆（类型 {type}）", true);
                return;
            }

            var own = npc.type;
            var netId = npc.netID;
            var interaction = npc.playerInteraction;
            try
            {
                // NPCSpawnParams 留空：Nullable 在 interop 里是引用包装，填非空值等于把托管指针传给原生，只能传"不覆盖"。
                npc.SetDefaults(type, new NPCSpawnParams());
            }
            finally
            {
                npc.type = own;
                npc.netID = netId;
                if (interaction != null) npc.playerInteraction = interaction;
                // 绑定实例与实体级 Global 表也换回来。
                NpcEntities.RebindTo(npc, this);
                GlobalNpcRegistry.Attach(npc, true);
                NpcLoader.EndClone(type);
            }
        }

        // Happiness 不翻：偏好要写进游戏的人物库（PersonalityDatabase），Pylon 侧还没有这个类型。

        // Clone / CloneNewInstances / NewInstance 不翻：实体实例由 NpcEntities.Bind 统一建（每只 NPC 一份）。

        // ================= 生命周期 =================

        /// <summary>只放共享数值，不要碰游戏对象。</summary>
        public override void SetStaticDefaults() { }

        /// <summary>这个类型的每个 NPC 生成时；专家/大师缩放在这之后（finishModNpc）。</summary>
        public virtual void SetDefaults(NPC npc) { }

        /// <summary>SetStaticDefaults 之后。</summary>
        public virtual void PostStaticDefaults() { }

        /// <summary>SetDefaults 与 ApplyBuffImmunity 之后。</summary>
        public virtual void PostSetDefaults(NPC npc) { }

        /// <summary>全部内容就绪后一次。</summary>
        public virtual void PostSetupContent() { }

        /// <summary>SetDefaults 之后；<c>npc.buffImmune[id] = true</c> 放这里。</summary>
        public virtual void ApplyBuffImmunity(NPC npc) { }

        /// <summary>一次（静态默认值之后）：掉落写进游戏库（npcLoot.Add(rule)）。</summary>
        public virtual void ModifyNPCLoot(NPCLoot npcLoot) { }

        /// <summary>一次：图鉴条目（生物群系、时间、文本）。</summary>
        public virtual void SetBestiary(BestiaryDatabase database, BestiaryEntry bestiaryEntry) { }

        // ================= 事件 =================

        /// <summary>每次被打中、在游戏自己的效果之后；<c>npc.life &lt;= 0</c> 是致命的这击。</summary>
        public virtual void HitEffect(NPC npc, int hitDirection, double damage) { }

        /// <summary>生成后一次，收得到原生的生成来源。</summary>
        public virtual void OnSpawn(NPC npc, IEntitySource source) { }

        /// <summary>增益标志重置完、重新生效与跑 AI 之前。</summary>
        public virtual void ResetEffects(NPC npc) { }

        // 命中类钩子（OnHitByItem/ModifyHitPlayer/...）不翻：要 HitModifiers/HurtModifiers/HitInfo/HurtInfo 这些 JS 侧自造类，本版游戏没有对应原生类型。

        /// <summary>npc 对 npc（敌人撞上居民）；false 就是挡下。命中管线（CombatLoader）未迁移，暂不分派。</summary>
        public virtual bool CanHitNPC(NPC npc, NPC target) => true;

        /// <summary>npc 挨 npc 打；false 就是挡下。命中管线未迁移，暂不分派。</summary>
        public virtual bool CanBeHitByNPC(NPC npc, NPC attacker) => true;

        /// <summary>生命 ≤ 0 时；false 阻止死亡（要把生命补回去，否则下一帧还会问）。</summary>
        public virtual bool CheckDead(NPC npc) => true;

        /// <summary>按人数缩放之后；balance 是游戏算的数，bossAdjustment 大师 0.85、其它模式 1。</summary>
        public virtual void ApplyDifficultyAndPlayerScaling(NPC npc, int numPlayers, float balance, float bossAdjustment) { }

        // ================= 绘制 =================

        /// <summary>NPC 中心那格的光照色，在 PreDraw 之前；改它就是改这次的画色。</summary>
        public virtual void DrawEffects(NPC npc, ref Color drawColor) { }

        /// <summary>false 跳过游戏自己的绘制；PostDraw 照样会跑。</summary>
        public virtual bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) => true;

        /// <summary>绘制之后。</summary>
        public virtual void PostDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor) { }

        /// <summary>绘制缓存之后；把 index 加进 <c>Main.instance.DrawCacheNPCs*</c> 就画在那批里。</summary>
        public virtual void DrawBehind(NPC npc, int index) { }

        /// <summary>返回 <c>null</c> 用游戏的；返回颜色就连 alpha 0 一起接管。</summary>
        public virtual Color? GetAlpha(NPC npc, Color drawColor) => null;

        /// <summary>首领图标的槽位（初值是游戏算的）；改成 -1 就隐藏。</summary>
        public virtual void BossHeadSlot(NPC npc, ref int index) { }

        /// <summary>首领图标的旋转（初值是游戏算的）。</summary>
        public virtual void BossHeadRotation(NPC npc, ref float rotation) { }

        /// <summary>首领图标的镜像（初值是游戏算的）。</summary>
        public virtual void BossHeadSpriteEffects(NPC npc, ref SpriteEffects spriteEffects) { }

        // ================= AI / 动画 / 存活 =================

        /// <summary>每帧；false 跳过游戏 AI 与 AI。</summary>
        public virtual bool PreAI(NPC npc) => true;

        /// <summary>每帧。</summary>
        public virtual void AI(NPC npc) { }

        /// <summary>每帧，AI 之后（PreAI 否决了也跑）。</summary>
        public virtual void PostAI(NPC npc) { }

        /// <summary>原生 NPC 同步包发出后，把本 NPC 的附加数据写进去。</summary>
        public virtual void SendExtraAI(NPC npc, NetWriter writer) { }

        /// <summary>收到本 NPC 的附加数据。</summary>
        public virtual void ReceiveExtraAI(NPC npc, NetReader reader) { }

        /// <summary>自定义动画：改 <c>npc.frame.Y</c>；frameHeight = 贴图高 ÷ 帧数；覆写后原版 FindFrame 整段不跑（帧的 X/Width/Height 由框架先填好）。</summary>
        public virtual void FindFrame(NPC npc, int frameHeight) { }

        /// <summary>false：离玩家再远也不消失。</summary>
        public virtual bool CheckActive(NPC npc) => true;

        /// <summary>死亡时；false 取消掉落与原版 NPCLoot。</summary>
        public virtual bool PreKill(NPC npc) => true;

        /// <summary>死亡时，在 PreKill 与游戏掉落之后。</summary>
        public virtual void OnKill(NPC npc) { }

        // ================= 自然生成（未接：Pylon 还没有 SpawnLoader，两个钩子声明保留但不会被调用） =================

        /// <summary>每次自然生成：返回权重（原版重 1，0 = 不生成）。管线未迁移，暂不调用。</summary>
        public virtual float SpawnChance(Player player, int spawnTileX, int spawnTileY, int tileType, int wallType) => 0f;

        /// <summary>被抽中时怎么生成，在生成的格 (tileX, tileY)；返回 NPC 下标。管线未迁移，暂不调用。</summary>
        public virtual int SpawnNPC(int tileX, int tileY) =>
            NPC.NewNPC(NPC.GetSpawnSourceForNaturalSpawn(), tileX * 16 + 8, tileY * 16, Type, 0, 0f, 0f, 0f, 0f, 255);

        // 城镇 NPC 成员（CanTownNPCSpawn/CheckConditions/SetNPCNameList/GetChat/CanChat/SetChatButtons/OnChatButtonClicked/AddShops）由 TownNpcLoader 分派；GetChat/CanChat 是 NpcLoader 公共钩子，已接线。

        /// <summary>隔一阵子问一次（世界里还没有这个类型时）：true 就搬进下一间空房。</summary>
        public virtual bool CanTownNPCSpawn(int numTownNPCs) => false;

        /// <summary>这间房（单位：格）合适吗？</summary>
        public virtual bool CheckConditions(int left, int right, int top, int bottom) => true;

        /// <summary>它的名字表。</summary>
        public virtual string[] SetNPCNameList() => new string[0];

        /// <summary>聊天时说的话；<c>null</c> 用游戏的。Global 之后还能再改（ref）。</summary>
        public virtual string GetChat(NPC npc) => null;

        /// <summary>能不能聊天；<c>null</c> 用游戏的。默认就是 <c>npc.townNPC</c>。</summary>
        public virtual bool? CanChat(NPC npc) => npc.townNPC;

        // TownNPCProfile 不翻：返回值要装进 TownNPCProfiles.Instance，属内容安装管线（城镇）。

        // ModifyActiveShop / ModifyNPCHappiness / AddShops / TownNPCAttack* 不翻：城镇/商店/幸福度管线（要 NPCShop、ShopHelper、HelperInfo 这些原生类型或自造类）。

        /// <summary>头的槽位；-1 = 没头。头像管线未迁移。</summary>
        public virtual int NPCHeadSlot() => -1;

        /// <summary>把本 NPC 的商店加进列表（内容安装时一次）；店自己调 Register()。</summary>
        public virtual void AddShops(List<NpcShop> shops) { }

        /// <summary>聊天的按钮（城镇）。</summary>
        public virtual void SetChatButtons(NPC npc, ChatButtons buttons) { }

        /// <summary>点了按钮（城镇）；返回商店名就打开它。</summary>
        public virtual string OnChatButtonClicked(NPC npc, bool firstButton) => null;

        // ================= 工具 =================

        /// <summary>掉的钱（铜币）：金 10000、银 100、铜 1。</summary>
        public static int NPCValue(int platinum = 0, int gold = 0, int silver = 0, int copper = 0) =>
            platinum * 1000000 + gold * 10000 + silver * 100 + copper;

        // register / isModType / getTypeByName 等不翻：注册与反查走 Pylon 的自动发现 + ModContent（ID 分配在 ModContent.Register）。
    }
}
