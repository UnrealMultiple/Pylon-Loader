#nullable enable
using Pylon.ModLoader;
using Terraria.Localization;

namespace PipelineTest.Content;

/// <summary>客户端配置：字段绑定读写/类型转换/OnChanged 计数。</summary>
public sealed class TestClientConfig : ModConfig
{
    public static int ChangedCount;
    public static int LoadedCount;

    public override ConfigScope Mode => ConfigScope.ClientSide;

    protected override void DeclareOptions(ConfigOptions options)
    {
        options.Add("EnableFeature", ModConfig.Toggle(true));
        options.Add("Volume", ModConfig.Range(0.5, 0, 1, 0.1));
        options.Add("Mode", ModConfig.Radio("easy", "easy", "normal", "hard"));
        options.Add("Title", ModConfig.Dropdown("A", "A", "B", "C"));
        options.Add("Accent", ModConfig.Toggle(false));
        options.Add("Separator", ModConfig.Header("—— 分隔线 ——"));
    }

    public override void OnLoaded() => LoadedCount++;

    public override void OnChanged() => ChangedCount++;
}

/// <summary>服务器配置：单机可验 Mode 值与 AcceptClientChanges 的裁决形状；真同步要多人。</summary>
public sealed class TestServerConfig : ModConfig
{
    public static int AcceptCalls;
    public static bool AcceptResult = true;

    public override ConfigScope Mode => ConfigScope.ServerSide;

    protected override void DeclareOptions(ConfigOptions options)
    {
        options.Add("MaxHomes", ModConfig.Range(2, 1, 10, 1));
    }

    public override bool AcceptClientChanges(ModConfig pendingConfig, int whoAmI, ref NetworkText message)
    {
        AcceptCalls++;
        if (!AcceptResult) message = NetworkText.FromLiteral("测试拒绝");
        return AcceptResult;
    }
}
