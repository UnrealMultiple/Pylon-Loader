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
        Tests.ItemTests.Register();
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

    /// <summary>tooltip 测试的光标扫描:开时每帧把一个采样点喂给游戏光标,光栅覆盖屏幕左上区域。</summary>
    public static bool TooltipSweep;
    private static int _sweepFrame;

    // 真机实测(渲染面 1920x882)热键栏不在屏幕左上角:第 1 格约在 125..220 x 305..425,
    // 原来的 (34..63, 34..41) 差了约 300 像素的 y,而且格宽约 75 像素、原扫描范围只有 30 像素。
    // 改成覆盖左上区域的光栅,步长(约 54 x 23 像素)明显小于格宽,保证落进格子内部。
    // 只移动光标、不按左键:悬停本身就会让游戏设置 Main.HoverItem 并走 tooltip 入口,
    // 而按住左键会把物品抓到光标上,破坏后面的组。
    private const int SweepColumns = 16;
    private const int SweepRows = 14;
    private const float SweepWidthRatio = 0.45f;
    private const float SweepTopRatio = 0.24f;
    private const float SweepHeightRatio = 0.36f;

    /// <summary>一轮光栅扫完需要的帧数;用例据此安排断言时机。</summary>
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
