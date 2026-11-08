using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using ReLogic.Content;
using Terraria;

namespace PipelineTest.Content;

/// <summary>测试血条：计数每个回调，并故意在 GetIconTexture 里读 Life（验证 Collected 的时机）。</summary>
public sealed class TestBossBar : ModBossBar
{
    public static int CollectedCalls, IconCalls, InfoCalls, PreCalls, PostCalls, DrawCalls;
    public static float CollectedLife, CollectedLifeMax;
    public static float IconSeenLife;
    public static Rectangle LastFrame;
    public static bool Hide;

    public override Asset<Texture2D> GetIconTexture(ref Rectangle? iconFrame)
    {
        IconCalls++;
        IconSeenLife = Life;
        iconFrame = new Rectangle(0, 0, 16, 16);
        return ModContent.Request(Mod, "assets/bossbars/TestBossBar.png");
    }

    public override bool? ModifyInfo(BossBarInfo info, ref float life, ref float lifeMax, ref float shield, ref float shieldMax)
    {
        InfoCalls++;
        return Hide ? false : null;
    }

    public override bool PreDraw(SpriteBatch spriteBatch, NPC npc, BossBarDrawParams drawParams)
    {
        PreCalls++;
        LastFrame = drawParams.IconFrame;
        return true;
    }

    public override void PostDraw(SpriteBatch spriteBatch, NPC npc, BossBarDrawParams drawParams) => PostCalls++;

    public override void Collected(float life, float lifeMax, float shield, float shieldMax)
    {
        CollectedCalls++;
        CollectedLife = life;
        CollectedLifeMax = lifeMax;
        base.Collected(life, lifeMax, shield, shieldMax);
    }
}
