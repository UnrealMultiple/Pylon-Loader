#nullable enable
using System.ComponentModel;
using Pylon.ModLoader;
using Terraria.Localization;
using Pylon.ModLoader.Content;

namespace PipelineTest.Content;

/// <summary>客户端配置：字段即真值（特性驱动），OnChanged 计数。</summary>
public sealed class TestClientConfig : ModConfig
{
    public static int ChangedCount;
    public static int LoadedCount;

    public override ConfigScope Mode => ConfigScope.ClientSide;

    [DefaultValue(true)]
    public bool EnableFeature = true;

    [DefaultValue(0.5)]
    [Range(0, 1)]
    [Increment(0.1f)]
    public double Volume = 0.5;

    [DefaultValue("easy")]
    public TestDifficulty PlayMode = TestDifficulty.Easy;

    [DefaultValue("A")]
    [OptionStrings(new[] { "A", "B", "C" })]
    public string Title = "A";

    [Header("Separator")]
    public bool Accent = false;

    public override void OnLoaded() => LoadedCount++;

    public override void OnChanged() => ChangedCount++;
}

public enum TestDifficulty
{
    Easy,
    Normal,
    Hard,
}

/// <summary>服务器配置：单机可验 Mode 值与 AcceptClientChanges 的裁决形状；真同步要多人。</summary>
public sealed class TestServerConfig : ModConfig
{
    public static int AcceptCalls;
    public static bool AcceptResult = true;

    public override ConfigScope Mode => ConfigScope.ServerSide;

    [DefaultValue(2)]
    [Range(1, 10)]
    [Increment(1)]
    public int MaxHomes = 2;

    public override bool AcceptClientChanges(ModConfig pendingConfig, int whoAmI, ref NetworkText message)
    {
        AcceptCalls++;
        if (!AcceptResult) message = NetworkText.FromLiteral("测试拒绝");
        return AcceptResult;
    }
}

