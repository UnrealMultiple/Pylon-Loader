#nullable enable
using System.Collections.Generic;
using Pylon.ModLoader;

namespace PipelineTest.Content;

/// <summary>客户端配置：字段绑定读写/类型转换/OnChanged 计数。</summary>
public sealed class TestClientConfig : ModConfig
{
    public static int ChangedCount;
    public static string LastChangedKey = "";
    public static int ApplyCount;
    public static int LoadedCount;

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

    public override void OnChanged(string key)
    {
        ChangedCount++;
        LastChangedKey = key;
    }

    public override void OnApply() => ApplyCount++;
}

/// <summary>服务器配置：单机可验 Scope 值与 AcceptClientChanges 默认；真同步要多人。</summary>
public sealed class TestServerConfig : ModConfig
{
    public static int AcceptCalls;
    public static bool AcceptResult = true;

    public override ConfigScope Scope => ConfigScope.ServerSide;

    protected override void DeclareOptions(ConfigOptions options)
    {
        options.Add("MaxHomes", ModConfig.Range(2, 1, 10, 1));
    }

    public override bool AcceptClientChanges(int fromPlayer, Dictionary<string, object> requested, ref string message)
    {
        AcceptCalls++;
        return AcceptResult;
    }
}
