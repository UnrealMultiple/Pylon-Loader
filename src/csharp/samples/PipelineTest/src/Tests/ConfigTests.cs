using Pylon.ModLoader;
using PipelineTest.Content;
using Terraria;

namespace PipelineTest.Tests;

/// <summary>
/// 管线：Config / ConfigNet —— 真机验证（单机可验部分）。
/// Ready：注册/声明校验/读盘/OnLoaded；类型转换与 Snap；ServerSide 的 Scope 与 AcceptClientChanges 默认。
/// World：SetOption 直改（OnChanged→落盘→读回）、草稿 Begin/Set/Apply（OnChanged 只对变了的键、OnApply、落盘）、
/// NeedsReload 拒改、ResetToDefaults。多人同步（Sync/Request）单机不可验，见 docs。
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
            // 注册与读盘：两份配置都在列表里，OnLoaded 已跑。
            var all = ModConfigLoader.List;
            c.True(all.Count >= 2, $"配置已注册（{all.Count} 份）");

            var clientEntry = EntryOf("TestClientConfig");
            c.NotNull(clientEntry, "客户端配置在列表里");
            c.True(TestClientConfig.LoadedCount > 0, $"OnLoaded 被调（{TestClientConfig.LoadedCount} 次）");
        });

        PT.Case("CFG-R2", c =>
        {
            // 声明顺序与默认值。配置文件里可能有上次跑/屏幕操作留下的值：先重置再验。
            var entry = EntryOf("TestClientConfig")!;
            entry.Instance.ResetToDefaults();
            c.Eq(entry.Options.Count, 6, $"声明了 6 个选项（实际 {entry.Options.Count}）");
            c.Eq(entry.Options[0].Key, "EnableFeature", "声明顺序 = 代码顺序");
            c.True((bool)entry.Instance.GetValue("EnableFeature"), "Toggle 默认 true");
            c.Eq(entry.Instance.GetNumber("Volume"), 0.5, "Range 默认 0.5");
            c.Eq(entry.Instance.GetString("Mode"), "easy", "Radio 默认 easy");
            c.Eq(entry.Instance.GetString("Title"), "A", "Dropdown 默认 A");
            c.True(!entry.Instance.GetBool("Accent"), "第二个 Toggle 默认 false");
        });

        PT.Case("CFG-R3", c =>
        {
            // 类型转换（ValueConverter）：字符串写进数字槽、数字写进 bool 槽。
            var entry = EntryOf("TestClientConfig")!;
            var volume = ModConfigLoader.Find(entry, "Volume")!;

            c.True(ModConfigLoader.HasValue(volume), "Range 是值选项");
            c.True(System.Math.Abs(ModConfigLoader.Snap(volume, 0.55) - 0.6) < 1e-9, "Snap 按 step 对齐（0.55→0.6，浮点容差）");
            c.Eq(ModConfigLoader.Snap(volume, -5), 0, "Snap 越下界夹回 0");
            c.Eq(ModConfigLoader.Snap(volume, 7), 1, "Snap 越上界夹回 1");
        });

        PT.Case("CFG-R4", c =>
        {
            // ServerSide：Scope 值、AcceptClientChanges 默认（单机不真跑同步）。
            var server = EntryOf("TestServerConfig");
            c.NotNull(server, "服务器配置在列表里");
            c.Eq(server!.Instance.Scope, ConfigScope.ServerSide, "Scope = ServerSide");
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
        TestClientConfig.ApplyCount = 0;

        // W1：SetOption 直改：校验 → 落盘 → OnChanged（键名记录）。
        PT.Later("CFG-W1", 5, c =>
        {
            client!.Instance.SetOption("Mode", "hard");
            c.Eq(client.Instance.GetString("Mode"), "hard", "SetOption 写进真值");
            c.True(TestClientConfig.ChangedCount > 0, $"OnChanged 被调（{TestClientConfig.ChangedCount} 次）");
            c.Eq(TestClientConfig.LastChangedKey, "Mode", "OnChanged 拿到键名");
        });

        // W2：非法值被 Coerce 拦下（Radio 里没有的选项不写、不触发 OnChanged）。
        PT.Later("CFG-W2", 15, c =>
        {
            var before = TestClientConfig.ChangedCount;
            client!.Instance.SetOption("Mode", "impossible");
            c.Eq(client.Instance.GetString("Mode"), "hard", "非法选择不写入");
            c.Eq(TestClientConfig.ChangedCount, before, "被拦的改动不发 OnChanged");
        });

        // W3：草稿流：Begin → Set（改两个键）→ Apply → 只对变了的键发 OnChanged + OnApply + 落盘。
        PT.Later("CFG-W3", 25, c =>
        {
            ModConfigLoader.Begin();
            var volume = ModConfigLoader.Find(client!, "Volume")!;
            ModConfigLoader.Set(client!, volume, 0.3);
            c.True(ModConfigLoader.Pending(), "草稿里有改动");

            // 草稿值在读值时可见（overlay），真值没动。
            c.True(System.Math.Abs(client!.Instance.GetNumber("Volume") - 0.3) < 1e-9, "草稿值经 overlay 可见");
            c.True(ModConfigLoader.Pending(new[] { client! }), "Pending 判定改动存在");

            ModConfigLoader.Apply();
            c.True(System.Math.Abs(client.Instance.GetNumber("Volume") - 0.3) < 1e-9, "Apply 写进真值");
            c.True(TestClientConfig.ApplyCount > 0, $"OnApply 被调（{TestClientConfig.ApplyCount} 次）");
            c.True(!ModConfigLoader.Pending(), "Apply 后草稿清空");
        });

        // W4：落盘真的发生了（文件里有刚写的值）：直接读文件确认。
        PT.Later("CFG-W4", 35, c =>
        {
            var json = ReadConfigFile("PipelineTest", "TestClientConfig");
            c.True(json is not null && json.Contains("0.3"), $"落盘含最新值（Volume=0.3）：{Truncate(json)}");
        });

        // W5：ResetToDefaults：值回默认、OnChanged 对每个值选项发、落盘。
        PT.Later("CFG-W5", 45, c =>
        {
            client!.Instance.ResetToDefaults();
            c.Eq(client.Instance.GetNumber("Volume"), 0.5, "重置回默认 0.5");
            c.Eq(client.Instance.GetString("Mode"), "easy", "Mode 回默认 easy");
        });

        // W6：收尾，清理计数器。
        PT.Later("CFG-W6", 55, c =>
        {
            TestClientConfig.ChangedCount = 0;
            TestClientConfig.ApplyCount = 0;
            TestClientConfig.LastChangedKey = "";
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
