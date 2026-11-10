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
    /// <summary>测试用：静态拿到本 Mod 实例（读 Name/Types/Logger）。</summary>
    public static PipelineTestMod Instance { get; private set; } = null!;

    public PipelineTestMod() => Instance = this;

    // 界面层/系统绘制探针的路由：这三个时机属于 Mod 本身，覆写挂在 Mod 上。
    public override void ModifyInterfaceLayers(List<AuthorLayer> layers) =>
        TestInterfaceProbe.OnModifyInterfaceLayers(layers);

    public override void PostDrawTiles() => TestInterfaceProbe.OnPostDrawTiles();

    public override void PostDrawInterface(Il2CppMicrosoft.Xna.Framework.Graphics.SpriteBatch spriteBatch) =>
        TestInterfaceProbe.OnPostDrawInterface(spriteBatch);

    public override void ModifySunLightColor(ref Il2CppMicrosoft.Xna.Framework.Graphics.Color tileColor,
                                             ref Il2CppMicrosoft.Xna.Framework.Graphics.Color backgroundColor) =>
        Tests.EnvironmentTests.Sky(ref tileColor, ref backgroundColor);

    public override void ModifyLightingBrightness(ref float scale) => Tests.EnvironmentTests.Light(ref scale);

    public override void PreUpdateProjectiles() => Tests.EnvironmentTests.Projectiles();

    public override void Load()
    {
        PT.Bind(Logger);

        LoadGroupSwitch();

        // 组注册：一条管线一组（见 docs/testing/03-harness.md 第 1 节）
        Tests.HostTests.Register();
        Tests.RarityTests.Register();
        Tests.DamageClassTests.Register();
        Tests.KeybindTests.Register();
        // Tests.KeybindTests.Register();
        Tests.SoundTests.Register();
        Tests.MusicTests.Register();
        Tests.CloudTests.Register();
        Tests.WaterTests.Register();
        Tests.EnvironmentTests.Register();
        Tests.HairTests.Register();
        Tests.ItemTests2.Register();
        Tests.ProjectileTests.Register();
        Tests.ProjectileHitTests.Register();
        Tests.DustTests.Register();
        Tests.GoreTests.Register();
        Tests.PrefixTests.Register();
        Tests.NpcTests.Register();
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
        Tests.CommandTests.Register();
        Tests.ConfigTests.Register();
        Tests.NetTests.Register();
        Tests.TooltipTests.Register();
        Tests.MenuTests.Register();
        Tests.ResourceOverlayTests.Register();
        Tests.BackgroundTests.Register();

        PT.Boot();
        Logger.Info("[PT] 组开关: " + (PT.Enabled is { } e ? string.Join(",", e) : "全部"));
        PT.RunPhase(Phase.Load);
    }

    public override void PostSetupContent()
    {
        PT.RunPhase(Phase.Ready);
        PT.End();
    }

    /// <summary>tooltip 测试的光标扫描：开时每帧把触摸位置扫过热键栏第 1 格。</summary>
    public static bool TooltipSweep;
    private static int _sweepFrame;

    public override void PostUpdateEverything()
    {
        if (!TooltipSweep) return;
        // 热键栏第 1 格约在 (20..90, 20..90)（手机 UI 无缩放偏移）；逐帧小幅移动提高命中
        Main.mouseX = 34 + _sweepFrame % 30;
        Main.mouseY = 34 + (_sweepFrame / 30) % 30;
        Main.mouseLeft = true;
        _sweepFrame++;
    }

    public override void AddRecipes()
    {
        Tests.RecipeTests.AddRecipes();
    }

    /// <summary>读 Mod 目录的 tests.txt（一行一个组名，# 注释）；没有则跑全部。</summary>
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