using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>血条信息：ModifyInfo 里看/改要瞄哪个 NPC。</summary>
    public sealed class BossBarInfo
    {
        /// <summary>要盯着看的 NPC 下标（初值是游戏正在跟的那个；改成别的可瞄虫子的另一节）。</summary>
        public int npcIndexToAimAt;
    }

    /// <summary>血条这次绘制的参数；PreDraw/PostDraw 里改字段就改了这次的绘制。</summary>
    public sealed class BossBarDrawParams
    {
        /// <summary>这次画的图标贴图。</summary>
        public Texture2D IconTexture;

        /// <summary>从图标上取哪一块。</summary>
        public Rectangle IconFrame;

        /// <summary>生命与最大生命。</summary>
        public float Life;

        /// <summary>生命与最大生命。</summary>
        public float LifeMax;

        /// <summary>护盾当前值与上限（都是 0 就不画盾）。</summary>
        public float Shield;

        /// <summary>护盾当前值与上限（都是 0 就不画盾）。</summary>
        public float ShieldMax;

        /// <summary>条身中心（默认屏幕中下）。</summary>
        public Vector2 BarCenter;

        /// <summary>条身着色（默认白）。</summary>
        public Color BarColor;

        /// <summary>图标着色（默认白）。</summary>
        public Color IconColor;

        /// <summary>图标缩放（默认 1）。</summary>
        public float IconScale;

        /// <summary>显不显血条上的生命文字（默认 true）。</summary>
        public bool ShowText;

        /// <summary>生命文字相对默认位置的偏移。</summary>
        public Vector2 TextOffset;

        /// <summary>条身贴图（默认游戏的 UI_BossBar）。</summary>
        public Texture2D BarTexture;
    }

    /// <summary>自定义 Boss 血条；NPC 在 SetDefaults 里用 ModContent.GetInstance 挂给 ModNpc.BossBar。</summary>
    public abstract class ModBossBar : ModType
    {
        private float _life;
        private float _lifeMax;
        private float _shield;
        private float _shieldMax;

        /// <summary>上一次画的那帧的生命。</summary>
        public float Life => _life;

        /// <summary>上一次画的那帧的最大生命。</summary>
        public float LifeMax => _lifeMax;

        /// <summary>上一次画的那帧的护盾。</summary>
        public float Shield => _shield;

        /// <summary>上一次画的那帧的护盾上限。</summary>
        public float ShieldMax => _shieldMax;

        /// <summary>只放共享数值，不要碰游戏对象。</summary>
        public override void SetStaticDefaults() { }

        /// <summary>图标（Asset&lt;Texture2D&gt;）；<paramref name="iconFrame"/> 可裁一块（null = 整张）；返回 null = 游戏的 "?"。</summary>
        public virtual Asset<Texture2D> GetIconTexture(ref Rectangle? iconFrame) => null;

        /// <summary>每帧决定这条血条：null = 用 NPC 自己的生命（盾仍用你写的值）；true = 全用你写的值；false = 不画。</summary>
        public virtual bool? ModifyInfo(BossBarInfo info, ref float life, ref float lifeMax, ref float shield, ref float shieldMax) => null;

        /// <summary>画之前；false = 不画游戏的条（PostDraw 照样会跑）。</summary>
        public virtual bool PreDraw(SpriteBatch spriteBatch, NPC npc, BossBarDrawParams drawParams) => true;

        /// <summary>画完之后。</summary>
        public virtual void PostDraw(SpriteBatch spriteBatch, NPC npc, BossBarDrawParams drawParams) { }

        /// <summary>框架每帧画这条血条之前把值收进来；Life/LifeMax/Shield/ShieldMax 就是它填的。</summary>
        public virtual void Collected(float life, float lifeMax, float shield, float shieldMax)
        {
            _life = life;
            _lifeMax = lifeMax;
            _shield = shield;
            _shieldMax = shieldMax;
        }

        // 不翻：注册与 SetStaticDefaults 的调用走 Pylon 自动发现 + Harmony 层，不照抄静态注册表。
    }
}