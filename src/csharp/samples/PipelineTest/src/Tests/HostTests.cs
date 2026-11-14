using System;
using System.IO;
using Pylon.ModLoader;

namespace PipelineTest.Tests;

/// <summary>
/// B0：测试 Mod 与框架自身的自检。这组全绿才说明后面的失败是管线的问题，不是脚手架的问题。
/// </summary>
internal static class HostTests
{
    public static void Register()
    {
        // 空组先于 host 注册:这样它在本轮 Load 阶段就会先跑一遍,后面的用例才能断言它已完成。
        PT.Group("host-empty", Phase.Load, static () => { });
        PT.Group("host", Phase.Load, HostLoad);
        // 安装完成的断言必须放世界阶段：Mod.PostSetupContent 本身就是安装管线的第 74 步，
        // 那一刻 GameContent.Installed 必然还是 false。
        PT.Group("host-ready", Phase.World, HostReady);
    }

    // ---- T0：Load 阶段能做的自检 ----

    private static void HostLoad()
    {
        PT.Case("HOST-01", c =>
        {
            // LoaderHost 是 internal，Mod 侧只能从可观察面验证：内容被注册了、Logger 可用。
            c.True(PipelineTestMod.Instance.Name == "PipelineTest", "PipelineTestMod.Instance.Name=" + PipelineTestMod.Instance.Name);
            c.True(PipelineTestMod.Instance.Types.Count > 0, $"PipelineTestMod.Instance.Types.Count={PipelineTestMod.Instance.Types.Count}");
            var log = PipelineTestMod.Instance.Logger;
            c.NotNull(log, "Logger");
        });

        PT.Case("HOST-02", c =>
        {
            // BOOT 行已在 PT.Boot() 打过；这里验证内容发现按 FullKey 回查。
            var found = ModContent.TryFind<Pylon.ModLoader.ModType>("PipelineTest/NotExist", out var _);
            c.True(!found, "不存在的 FullKey 不应命中");
            c.True(PipelineTestMod.Instance.Types.Count > 0, $"内容发现 {PipelineTestMod.Instance.Types.Count} 个");
        });

        PT.Case("HOST-03", c =>
        {
            // GetContent 收集 == 本 Mod 注册的内容数。
            var all = ModContent.GetContent<Pylon.ModLoader.ModType>();
            var mine = PipelineTestMod.Instance.GetContent<Pylon.ModLoader.ModType>();
            c.Eq(mine.Count, PipelineTestMod.Instance.Types.Count, "Mod.GetContent 数");
            c.True(all.Count >= mine.Count, $"全局 {all.Count} >= 本 Mod {mine.Count}");
        });

        PT.Case("HOST-05", c =>
        {
            // 日志格式：本用例自身就是一行 [PipelineTest] [PT] START/PASS —— 能被 Show-Log 过滤到。
            // 桌面无法自动验证 logcat；这里验证 Logger 不抛即可。
            c.NoThrow(static () => PipelineTestMod.Instance.Logger.Info("[PT] 日志格式自检"), "PipelineTestMod.Instance.Logger.Info");
        });

        PT.Case("HOST-06", c =>
        {
            // 组开关：tests.txt 只写 host 时其他组不 START —— 由运行判定脚本核对。
            // 这里只验证开关对象已被加载（文件存在时）。
            if (PT.Enabled is { } enabled)
            {
                c.True(enabled.Count > 0, "tests.txt 已读，组数=" + enabled.Count);
            }
            else
            {
                c.True(true, "无 tests.txt（跑全部）");
            }
        });

        PT.Case("HOST-07", c =>
        {
            // 空组(0 用例)不崩,且必须真的被执行过:注册在 Register() 里,
            // 注册进用例体内的组落在本轮快照之外,会让 [PT] END 永不打印。
            var pending = PT.PendingGroups();
            c.True(!pending.Contains("host-empty"), "空组已在 Load 阶段执行(pending=[" + pending + "])");
        });

        PT.Case("HOST-08", c =>
        {
            // 异常不连坐：故意抛异常的用例记 FAIL，后续用例继续跑。
            // 用例框架的兜底由 PT.Case 的 try/catch 保证；这里验证 Case 出异常后计数正确。
            var failBefore = PT.Fail;
            PT.Case("HOST-08-INNER", _ => throw new InvalidOperationException("故意抛出"));
            c.True(PT.Fail == failBefore + 1, $"内层 FAIL 已计数（{PT.Fail - failBefore}）");
            c.True(true, "外层用例仍在继续（未连坐）");
        });
    }

    // ---- T1：进世界后（安装管线必然跑完） ----

    private static void HostReady()
    {
        PT.Case("HOST-04", c =>
        {
            // 安装完成：GameContent.Installed 为真，且没有 UnavailableReason。
            c.True(Pylon.ModLoader.GameContent.Installed,
                $"GameContent.Installed={Pylon.ModLoader.GameContent.Installed}");
            c.True(Pylon.ModLoader.GameContent.UnavailableReason is null,
                "UnavailableReason=" + Pylon.ModLoader.GameContent.UnavailableReason);
        });
    }
}
