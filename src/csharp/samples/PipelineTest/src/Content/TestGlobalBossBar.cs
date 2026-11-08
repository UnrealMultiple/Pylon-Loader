using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.ID;

namespace PipelineTest.Content;

/// <summary>横切血条：mod 条和原版条都应该跑到它。</summary>
public sealed class TestGlobalBossBar : GlobalBossBar
{
    public static int PreCalls, PostCalls, VanillaPreCalls;

    public override bool PreDraw(SpriteBatch spriteBatch, NPC npc, BossBarDrawParams drawParams)
    {
        PreCalls++;
        if (npc.type == NPCID.KingSlime) VanillaPreCalls++;
        return true;
    }

    public override void PostDraw(SpriteBatch spriteBatch, NPC npc, BossBarDrawParams drawParams) => PostCalls++;
}
