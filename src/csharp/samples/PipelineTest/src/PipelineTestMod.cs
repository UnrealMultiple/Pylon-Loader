using System;
using System.Collections.Generic;
using System.IO;
using Pylon.ModLoader;
using Terraria;
using PipelineTest.Content;
using AuthorLayer = Pylon.ModLoader.GameInterfaceLayer;

namespace PipelineTest;

public sealed class PipelineTestMod : Mod
{
    public static PipelineTestMod Instance { get; private set; } = null!;

    public PipelineTestMod() => Instance = this;

    public override void ModifyInterfaceLayers(List<AuthorLayer> layers) =>
        TestInterfaceProbe.OnModifyInterfaceLayers(layers);

    public override void PostDrawTiles() => TestInterfaceProbe.OnPostDrawTiles();

    public override void PostDrawInterface(Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
    {
        TestInterfaceProbe.OnPostDrawInterface(spriteBatch);
        Tests.FishingTests.Draw();
    }

    public override void ModifySunLightColor(ref Il2CppMicrosoft.Xna.Framework.Graphics.Color tileColor,
                                             ref Il2CppMicrosoft.Xna.Framework.Graphics.Color backgroundColor) =>
        Tests.EnvironmentTests.Sky(ref tileColor, ref backgroundColor);

    public override void ModifyLightingBrightness(ref float scale) => Tests.EnvironmentTests.Light(ref scale);

    public override void PreUpdateProjectiles() => Tests.EnvironmentTests.Projectiles();

    public override void Load()
    {
        PT.Bind(Logger);

        LoadGroupSwitch();

        Tests.HostTests.Register();
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
        Tests.ProjectileHitTests.Register();
        Tests.DustTests.Register();
        Tests.GoreTests.Register();
        Tests.PrefixTests.Register();
        Tests.NpcTests.Register();
        Tests.NpcHeadTests.Register();
        Tests.ArrayInteropTests.Register();
        Tests.NpcHitTests.Register();
        Tests.NpcContactTests.Register();
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
        Tests.PlayerUpdateTests.Register();
        Tests.PlayerHurtTests.Register();
        Tests.PlayerDamageSourceTests.Register();
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

    public static bool TooltipSweep;
    private static int _sweepFrame;

    private const int SweepColumns = 16;
    private const int SweepRows = 14;
    private const float SweepWidthRatio = 0.45f;
    private const float SweepTopRatio = 0.24f;
    private const float SweepHeightRatio = 0.36f;

    public static int SweepFrames => SweepColumns * SweepRows;

    public override void PostUpdateEverything()
    {
        if (!TooltipSweep) return;

        var width = Math.Max(Main.screenWidth, 1);
        var height = Math.Max(Main.screenHeight, 1);
        var column = _sweepFrame % SweepColumns;
        var row = (_sweepFrame / SweepColumns) % SweepRows;
        Main.mouseX = (int)(column * (width * SweepWidthRatio) / SweepColumns);
        Main.mouseY = (int)((SweepTopRatio * height) + row * (height * SweepHeightRatio) / SweepRows);
        _sweepFrame++;
    }

    public override void AddRecipes()
    {
        Tests.RecipeTests.AddRecipes();
    }

    private void LoadGroupSwitch()
    {
        try
        {
            var path = Path.Combine(Directory, "tests.txt");
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
}
