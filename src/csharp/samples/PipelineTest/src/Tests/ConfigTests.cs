using Pylon.ModLoader;
using PipelineTest.Content;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 管线：Config / ConfigNet —— 真机验证（单机可验部分）。
/// Ready：注册/反射选项面/读盘/OnLoaded；Snap 与 Coerce；Mode 与 AcceptClientChanges 的裁决形状；Clone/NeedsReload。
/// World：字段直改 + SaveChanges（OnChanged→落盘→读回）、草稿 Begin/Set/Apply（有值变了发 OnChanged、落盘）、
/// Defaults+Apply 回默认。多人同步（Sync/Request）单机不可验，见 docs。
/// </summary>
internal static class ConfigTests
{
    public static void Register()
    {
        PT.Group("config", Phase.Ready, RunReady);
        PT.Group("config-w", Phase.World, RunWorld);
    }

    private static void RunReady()
    {
        PT.Case("CFG-R1", c =>
        {
            var all = ModConfigLoader.List;
            c.True(all.Count >= 2, $"配置已注册（{all.Count} 份）");

            var clientEntry = EntryOf("TestClientConfig");
            c.NotNull(clientEntry, "客户端配置在列表里");
            c.True(TestClientConfig.LoadedCount > 0, $"OnLoaded 被调（{TestClientConfig.LoadedCount} 次）");
        });

        PT.Case("CFG-R2", c =>
        {
            var entry = EntryOf("TestClientConfig")!;
            c.Eq(entry.Options.Count, 5, $"反射收了 5 个字段（实际 {entry.Options.Count}）");
            c.Eq(entry.Options[0].Name, "EnableFeature", "顺序 = 声明深度 + 元数据序");
            c.Eq(entry.Options[0].Kind, ConfigOptionKind.Toggle, "bool 字段 → Toggle");
            c.Eq(entry.Options[1].Kind, ConfigOptionKind.Range, "double 字段 + Range → Range");
            c.True(entry.Options[1].Integer == false && System.Math.Abs(entry.Options[1].Min) < 1e-9 &&
                   System.Math.Abs(entry.Options[1].Max - 1) < 1e-9 && System.Math.Abs(entry.Options[1].Step - 0.1) < 1e-6,
                "Range 的 Min/Max/Step 来自特性（float 步长按 double 容差比）");
            c.Eq(entry.Options[2].Kind, ConfigOptionKind.Choice, "枚举字段 → Choice");
            c.Eq(entry.Options[2].Choices.Length, 3, "枚举 Choices 全值");
            c.Eq(entry.Options[3].Kind, ConfigOptionKind.Choice, "OptionStrings → Choice");
            c.Eq(entry.Options[3].Choices.Length, 3, "OptionStrings 三个选择");
            c.True(entry.Options[4].ReloadRequired == false, "默认无 ReloadRequired");
        });

        PT.Case("CFG-R3", c =>
        {
            var entry = EntryOf("TestClientConfig")!;
            var volume = ModConfigLoader.Find(entry, "Volume")!;

            c.True(System.Math.Abs(volume.Snap(0.55) - 0.6) < 1e-6, "Snap 按 step 对齐（0.55 → 半格以上进到 0.6）");
            c.True(System.Math.Abs(volume.Snap(0.6) - 0.6) < 1e-6, "Snap 命中 0.6 格");
            c.Eq(volume.Snap(-5), 0, "Snap 越下界夹回 0");
            c.Eq(volume.Snap(7), 1, "Snap 越上界夹回 1");
            c.Eq(volume.Coerce("0.4"), 0.4, "Coerce 字符串→double");
            c.True(volume.Coerce("bad") is null, "Coerce 坏值返回 null");
            c.Eq(ModConfigLoader.Find(entry, "PlayMode")!.Coerce("hard"), TestDifficulty.Hard, "Coerce 字符串→枚举");
            c.True(ModConfigLoader.Find(entry, "PlayMode")!.Coerce("impossible") is null, "非法选择 Coerce 返回 null");
        });

        PT.Case("CFG-R4", c =>
        {
            var server = EntryOf("TestServerConfig");
            c.NotNull(server, "服务器配置在列表里");
            c.Eq(server!.Instance.Mode, ConfigScope.ServerSide, "Mode = ServerSide");
            c.Eq(EntryOf("TestClientConfig")!.Instance.Mode, ConfigScope.ClientSide, "客户端配置 Mode = ClientSide");

            var pending = server.Instance.Clone();
            var message = Terraria.Localization.NetworkText.Empty;
            TestServerConfig.AcceptResult = true;
            c.True(server.Instance.AcceptClientChanges(pending, 0, ref message), "AcceptClientChanges 默认接受");
            TestServerConfig.AcceptResult = false;
            c.True(!server.Instance.AcceptClientChanges(pending, 0, ref message), "拒绝时返回 false；message 带原因");
            TestServerConfig.AcceptResult = true;
        });

        PT.Case("CFG-R5", c =>
        {
            var entry = EntryOf("TestClientConfig")!;
            var clone = (TestClientConfig)entry.Instance.Clone();
            clone.Volume = 0.9;
            c.True(System.Math.Abs(((TestClientConfig)entry.Instance).Volume - 0.9) > 1e-9, "Clone（MemberwiseClone）的值不影响原件");
            c.True(!entry.Instance.NeedsReload(clone), "无 ReloadRequired 字段时 NeedsReload 恒 false");
        });
    }

    private static void RunWorld()
    {
        var client = EntryOf("TestClientConfig");
        if (client is null)
        {
            PT.SkipCase("CFG-W*", "客户端配置没注册上");
            return;
        }

        TestClientConfig.ChangedCount = 0;

        // W1：字段直改 + SaveChanges：落盘 → OnChanged。
        PT.Later("CFG-W1", 5, c =>
        {
            ((TestClientConfig)client!.Instance).Volume = 0.2;
            var result = client.Instance.SaveChanges();
            c.Eq(result, ConfigSaveResult.Success, "SaveChanges 返回 Success");
            c.True(TestClientConfig.ChangedCount > 0, $"OnChanged 被调（{TestClientConfig.ChangedCount} 次）");
        });

        // W2：管线 Coerce 拒非法值（Choice 里没有的选项进不了草稿）。
        PT.Later("CFG-W2", 15, c =>
        {
            var mode = ModConfigLoader.Find(client!, "PlayMode")!;
            c.True(mode.Coerce("impossible") is null, "非法选择 Coerce 返回 null（校验在管线，不在字段写）");
            c.Eq(((TestClientConfig)client!.Instance).PlayMode, TestDifficulty.Easy, "字段真值未被碰");
        });

        // W3：草稿流：Begin → Set → Apply → 有值变了发 OnChanged + 落盘。
        PT.Later("CFG-W3", 25, c =>
        {
            ModConfigLoader.Begin();
            var volume = ModConfigLoader.Find(client!, "Volume")!;
            ModConfigLoader.Set(client!, volume, 0.3);
            c.True(ModConfigLoader.Pending(), "草稿里有改动");
            c.True(System.Math.Abs((double)(ModConfigLoader.Read(client!, volume) ?? 0) - 0.3) < 1e-9, "草稿值经 Read 可见");
            c.True(System.Math.Abs(((TestClientConfig)client!.Instance).Volume - 0.2) < 1e-9, "真值字段还没动");

            var before = TestClientConfig.ChangedCount;
            ModConfigLoader.Apply();
            c.True(System.Math.Abs(((TestClientConfig)client.Instance).Volume - 0.3) < 1e-9, "Apply 写进字段");
            c.True(TestClientConfig.ChangedCount > before, $"Apply 后 OnChanged 被调（{before}→{TestClientConfig.ChangedCount}）");
            c.True(!ModConfigLoader.Pending(), "Apply 后草稿清空");
        });

        // W4：落盘真的发生了（文件里有刚写的值）：直接读文件确认。
        PT.Later("CFG-W4", 35, c =>
        {
            var json = ReadConfigFile("PipelineTest", "TestClientConfig");
            c.True(json is not null && json.Contains("0.3"), $"落盘含最新值（Volume=0.3）：{Truncate(json)}");
        });

        // W5：Defaults + Apply 回默认：值回默认、落盘。
        PT.Later("CFG-W5", 45, c =>
        {
            ModConfigLoader.Defaults(new[] { client! });
            ModConfigLoader.Apply();
            c.True(System.Math.Abs(((TestClientConfig)client.Instance).Volume - 0.5) < 1e-9, "重置回默认 0.5");
            c.Eq(((TestClientConfig)client.Instance).PlayMode, TestDifficulty.Easy, "Mode 回默认 Easy");
        });

        // W6：收尾，清理计数器。
        PT.Later("CFG-W6", 55, c =>
        {
            TestClientConfig.ChangedCount = 0;
        });
    }

    private static ModConfigEntry? EntryOf(string name)
    {
        var all = ModConfigLoader.List;
        for (var i = 0; i < all.Count; i++)
        {
            if (all[i].Name == name) return all[i];
        }
        return null;
    }

    private static string? ReadConfigFile(string mod, string config)
    {
        try
        {
            var path = System.IO.Path.Combine(Main.SavePath, "ModConfigs", $"{mod}_{config}.json");
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string? text) =>
        string.IsNullOrEmpty(text) ? "null" : text.Length > 120 ? text[..120] + "…" : text;
}



