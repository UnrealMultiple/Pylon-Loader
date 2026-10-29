using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.ItemDropRules;

namespace Pylon.ModLoader
{
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

        // 不翻（命中那套共 14 个钩子）：要 HitModifiers/HurtModifiers/HitInfo/HurtInfo，是 JS 侧自造类，
        // 本版没有对应原生类型（NPC.StrikeNPC 与 Player.Hurt 还是 int/float 参数），和 ModNpc 同一批。

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

        // 不翻（ModifyNPCHappiness）：要 ShopHelper 与 HelperInfo，属幸福度管线（和 ModNpc 的同一个一起等）。

        // 不翻（ModifyShop、SetupShop）：参数是自造的 NPCShop 类（js/mod/NPCShop.js 自己占 Main.shop 的槽位），本版没有原生对应物。

        /// <summary>每次开这家店时，物品已经备好；shopName 是店名，items 是这次摆出来的（原生数组）。</summary>
        public virtual void ModifyActiveShop(NPC npc, string shopName, Item[] items) { }

        /// <summary>旅行商人的店（<c>Main.travelShop</c> 的类型表）；nextSlot 是指向第一个空位的可写引用。</summary>
        public virtual void SetupTravelShop(int[] shop, ref int nextSlot) { }

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

        // 不翻（EditSpawnFlags）：参数是自造的 NPCSpawnInfo 类（js/mod/NPCSpawnInfo.js，包着游戏的 NPC.Spawner，只有包着才能按字段读写：noWorms、invaders、ZoneCorrupt...）。

        // 不翻（EditSpawnRate、EditSpawnRange）：Player 之外的参数签名要靠 NPCSpawnInfo 那条管线定形，同上。

        // 不翻（EditSpawnInfo）：同 EditSpawnFlags，参数是自造的 NPCSpawnInfo。

        // 不翻（EditSpawnPool）：pool 是自造的 SpawnPool（js/mod/SpawnPool.js），spawnInfo 是 NPCSpawnInfo。

        // 不翻（SpawnNPC）：只有搭配 SpawnChance/EditSpawnPool 那套自造类才有意义（ModNpc.SpawnNPC 已翻，是另一个入口）。

        // 不翻（register）：注册走 Pylon 的自动发现 + ModContent，不照抄静态注册表。
    }
}
