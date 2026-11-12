using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace PipelineTest.Tests;

internal static class NpcHeadTests
{
    public static void Register()
    {
        PT.Group("npc-head", Phase.Ready, Ready);
    }

    private static void Ready()
    {
        PT.Case("NPC-HEAD1", c =>
        {
            var type = ModContent.NpcType<Content.TestTownNpc>();
            var slot = NPC.TypeToDefaultHeadIndex(type);
            c.True(slot >= 0 && slot < TextureAssets.NpcHead.Length, $"城镇头像槽={slot}");
            if (slot >= 0 && slot < TextureAssets.NpcHead.Length) c.NotNull(TextureAssets.NpcHead[slot], "城镇头像贴图");
            var npc = new NPC();
            npc.SetDefaults(type, new NPCSpawnParams());
            c.Eq(TownNPCProfiles.GetHeadIndexSafe(npc), slot, "实体头像查询");
            var states = NPCID.Sets.ShimmerTownTransform;
            var previous = states[type];
            var previousVariation = npc.townNpcVariationIndex;
            try
            {
                states[type] = true;
                npc.townNpcVariationIndex = 1;
                c.True(npc.IsShimmerVariant, "实体读取微光状态");
                var shimmer = TownNPCProfiles.GetHeadIndexSafe(npc);
                c.True(shimmer >= 0 && shimmer != slot, $"微光头像槽={shimmer}");
                if (shimmer >= 0 && shimmer < TextureAssets.NpcHead.Length) c.NotNull(TextureAssets.NpcHead[shimmer], "微光头像贴图");
            }
            finally
            {
                states[type] = previous;
                npc.townNpcVariationIndex = previousVariation;
            }
        });
        PT.Case("NPC-HEAD2", c =>
        {
            var npc = new NPC();
            npc.SetDefaults(ModContent.NpcType<Content.TestBossNpc>(), new NPCSpawnParams());
            var slot = npc.GetBossHeadTextureIndex();
            c.True(slot >= 0 && slot < TextureAssets.NpcHeadBoss.Length, $"首领头像槽={slot}");
            if (slot >= 0 && slot < TextureAssets.NpcHeadBoss.Length) c.NotNull(TextureAssets.NpcHeadBoss[slot], "首领头像贴图");
            c.Eq(NPCID.Sets.BossHeadTextures[npc.type], slot, "首领头像映射表");
        });
        PT.Case("NPC-HEAD3", c =>
        {
            c.True(NPC.TypeToDefaultHeadIndex(NPCID.Guide) >= 0, "原版向导头像直通");
            c.Eq(NPC.TypeToDefaultHeadIndex(ModContent.NpcType<Content.TestNpc>()), -1, "无头像资源不冒用槽 0");
        });
        PT.Info("NPC 头像查询验证不代替视觉验收：需在小地图、叠加地图、全屏地图分别检查测试商人与测试 Boss 图标。");
    }
}
