using System;
using System.Collections.Generic;
using System.IO;
using Pylon.ModLoader;
using PipelineTest.Content;
using Pylon.ModLoader.Content;

namespace PipelineTest;

public sealed class PipelineTestMod : Mod
{
    public static PipelineTestMod Instance { get; private set; } = null!;

    public static ManualInstallContent? ManualContent { get; private set; }

    public static int TownHeadSlot { get; private set; } = -1;

    public static int BossHeadSlot { get; private set; } = -1;

    public PipelineTestMod() => Instance = this;

    public override void Load()
    {
        PT.Bind(Logger);

        LoadGroupSwitch();
        DrawSiteProbe.Install();

        AchievementsHelper.OnNPCKilled += (player, npcId) =>
        {
            if (npcId == Terraria.ID.NPCID.BlueSlime) AchievementProbe.Kills++;
        };
        AchievementsHelper.OnItemPickup += (player, itemId, count) =>
        {
            if (itemId == Terraria.ID.ItemID.Gel) AchievementProbe.Pickups += count;
        };

        ManualContent = new ManualInstallContent(7);
        AddContent(ManualContent);
        TownHeadSlot = AddNPCHeadTexture(ModContent.NPCType<TestTownNpc>(), "assets/npcs/TestTownNpc_Head.png");
        BossHeadSlot = AddBossHeadTexture("assets/npcs/TestBossNpc_Head_Boss.png", ModContent.NPCType<TestBossNpc>());

        Tests.HostTests.Register();
        Tests.InstallTests.Register();
        Tests.RarityTests.Register();
        Tests.DamageClassTests.Register();
        Tests.KeybindTests.Register();
        Tests.SoundTests.Register();
        Tests.MusicTests.Register();
        Tests.CloudTests.Register();
        Tests.WaterTests.Register();
        Tests.EnvironmentTests.Register();
        Tests.HairTests.Register();
        Tests.ItemTests.Register();
        Tests.ItemTests2.Register();
        Tests.ProjectileTests.Register();
        Tests.ProjectileApiTests.Register();
        Tests.ProjectileHitTests.Register();
        Tests.DustTests.Register();
        Tests.GoreTests.Register();
        Tests.PrefixTests.Register();
        Tests.NpcTests.Register();
        Tests.NpcHeadTests.Register();
        Tests.ArrayInteropTests.Register();
        Tests.NpcHitTests.Register();
        Tests.NpcContactTests.Register();
        Tests.NpcApiTests.Register();
        Tests.TownAttackTests.Register();
        Tests.BuffTests.Register();
        Tests.BuffInteractionTests.Register();
        Tests.LootTests.Register();
        Tests.ShopTests.Register();
        Tests.ShopHookTests.Register();
        Tests.BannerTests.Register();
        Tests.BossBarTests.Register();
        Tests.TileTests.Register();
        Tests.TileSoundTests.Register();
        Tests.TileInteractionTests.Register();
        Tests.MountTests.Register();
        Tests.RecipeTests.Register();
        Tests.WallTests.Register();
        Tests.TileEntityTests.Register();
        Tests.EquipTests.Register();
        Tests.TreeTests.Register();
        Tests.PylonTests.Register();
        Tests.AchievementTests.Register();
        Tests.EmoteBubbleTests.Register();
        Tests.BiomeTests.Register();
        Tests.SceneEffectTests.Register();
        Tests.PlayerDrawLayerTests.Register();
        Tests.PlayerDrawInfoTests.Register();
        Tests.InterfaceTests.Register();
        Tests.PlayerTests.Register();
        Tests.PlayerApiTests.Register();
        Tests.PlayerUpdateTests.Register();
        Tests.PlayerHurtTests.Register();
        Tests.PlayerDamageSourceTests.Register();
        Tests.SystemTests.Register();
        Tests.ItemPvpTests.Register();
        Tests.AmmoTests.Register();
        Tests.HealTests.Register();
        Tests.FishingTests.Register();
        Tests.CommandTests.Register();
        Tests.ConfigTests.Register();
        Tests.NetTests.Register();
        Tests.TooltipTests.Register();
        Tests.MenuTests.Register();
        Tests.ResourceOverlayTests.Register();
        Tests.BackgroundTests.Register();
        Tests.ItemDrawTests.Register();
        Tests.JumpTests.Register();

        PT.Boot();
        Logger.Info("[PT] 组开关: " + (PT.Enabled is { } e ? string.Join(",", e) : "全部"));
        PT.RunPhase(Phase.Load);
    }

    public override void PostSetupContent()
    {
        PT.RunPhase(Phase.Ready);
        PT.End();
    }

    public override void AddRecipes()
    {
        Tests.RecipeTests.AddRecipes();
    }

    private void LoadGroupSwitch()
    {
        try
        {
            var path = Path.Combine(Folder, "tests.txt");
            if (!File.Exists(path)) return;

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(path))
            {
                var text = line.Trim();
                if (text.Length == 0 || text.StartsWith("#")) continue;
                names.Add(text);
            }
            if (names.Count > 0) PT.Enabled = names;
        }
        catch (Exception e)
        {
            Logger.Warn("[PT] tests.txt 读取失败：" + e.Message);
        }
    }

    public sealed class ManualInstallContent(int marker) : ModType
    {
        public int Marker { get; } = marker;
    }
}
