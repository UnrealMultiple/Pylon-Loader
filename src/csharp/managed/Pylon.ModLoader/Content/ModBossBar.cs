using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;

namespace Pylon.ModLoader;

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
    public virtual Asset<Texture2D> GetIconTexture(ref Rectangle? iconFrame) => null!;

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
}

// 未移植 register 与 SetStaticDefaults 的调用：走 Pylon 自动发现 + Harmony 层，不照抄静态注册表。
