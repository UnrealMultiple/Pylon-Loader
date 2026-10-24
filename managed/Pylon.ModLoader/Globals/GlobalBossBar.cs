using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>作用于所有 Boss 血条（游戏的和 ModBossBar 的）的横切类；一个类一个实例，钩子收到 NPC 自己挑。</summary>
    public abstract class GlobalBossBar : ModType
    {
        /// <summary>只放共享数值，不要碰游戏对象。</summary>
        public override void SetStaticDefaults() { }

        /// <summary>所有内容安装完成之后一次（跨 Mod 引用放这里）。</summary>
        public virtual void PostSetupContent() { }

        /// <summary>画之前；false = 这条血条不画（PostDraw 照旧跑）。</summary>
        public virtual bool PreDraw(SpriteBatch spriteBatch, NPC npc, BossBarDrawParams drawParams) => true;

        /// <summary>画完之后。</summary>
        public virtual void PostDraw(SpriteBatch spriteBatch, NPC npc, BossBarDrawParams drawParams) { }

        // 不翻（register）：注册与钩子挂接（globalBossBars.Register + BossBarLoader.WantGlobals）走 Pylon 的自动发现 + Harmony 层。
    }
}