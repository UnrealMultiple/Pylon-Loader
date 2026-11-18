using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using ReLogic.Content;
using Terraria;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.Core;

namespace PipelineTest.Content;

/// <summary>测试血条：计数每个回调，并故意在 GetIconTexture 里读 Life（验证收集时机）。</summary>
public sealed class TestBossBar : ModBossBar
{
    public static int IconCalls, InfoCalls, PreCalls, PostCalls, DrawCalls;
    public static float IconSeenLife;
    public static Rectangle LastFrame;
    public static bool Hide;

    public override Asset<Texture2D> GetIconTexture(ref Rectangle? iconFrame)
    {
        IconCalls++;
        IconSeenLife = Life;
        iconFrame = new Rectangle(0, 0, 16, 16);
        return Mod.Assets.Request<Texture2D>("assets/bossbars/TestBossBar.png");
    }

    public override bool? ModifyInfo(BossBarInfo info, ref float life, ref float lifeMax, ref float shield, ref float shieldMax)
    {
        InfoCalls++;
        return Hide ? false : null;
    }

    public override bool PreDraw(SpriteBatch spriteBatch, NPC npc, ref BossBarDrawParams drawParams)
    {
        PreCalls++;
        LastFrame = drawParams.IconFrame;
        return true;
    }

    public override void PostDraw(SpriteBatch spriteBatch, NPC npc, BossBarDrawParams drawParams) => PostCalls++;
}
