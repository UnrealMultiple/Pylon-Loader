using System;
using System.Collections.Generic;
using System.IO;
using Pylon.ModLoader;

namespace PipelineTest;

public sealed class PipelineTestMod : Mod
{
    /// <summary>测试用：静态拿到本 Mod 实例（读 Name/Types/Logger）。</summary>
    public static PipelineTestMod Instance { get; private set; } = null!;

    public PipelineTestMod() => Instance = this;

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
        Tests.HairTests.Register();
        Tests.ItemTests2.Register();
        Tests.ProjectileTests.Register();
        Tests.DustTests.Register();
        Tests.GoreTests.Register();
        Tests.PrefixTests.Register();
        Tests.NpcTests.Register();
        Tests.BuffTests.Register();
        Tests.LootTests.Register();
        Tests.ShopTests.Register();
        Tests.ShopHookTests.Register();
        Tests.BannerTests.Register();
        Tests.BossBarTests.Register();
        Tests.TileTests.Register();
        Tests.MountTests.Register();

        PT.Boot();
        Logger.Info("[PT] 组开关: " + (PT.Enabled is { } e ? string.Join(",", e) : "全部"));
        PT.RunPhase(Phase.Load);
    }

    public override void PostSetupContent()
    {
        PT.RunPhase(Phase.Ready);
        PT.End();
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