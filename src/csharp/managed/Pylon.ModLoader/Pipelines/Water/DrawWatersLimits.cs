using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// Main.DrawWaters 的两个循环把水样式总数编成了立即数（地址索引版带 +8 数组头，
/// 开头比较版 b.eq 退出），mod 水样式超过原版数后水面不动/不画。
/// </summary>
internal static class DrawWatersLimits
{
    private const int HeaderInFloats = 8;

    private static bool _installed;
    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>WaterPipeline.Build 调（总数定下来后）；没有 mod 样式不动。</summary>
    internal static void Build(IModLog log, int from, int to)
    {
        if (_installed || from <= 0 || to <= from) return;
        _installed = true;

        if (!MethodCode.TryResolve(MethodCode.KlassOf<Main>(), "DrawWaters", 1, out var body, out var end))
        {
            log.Warn("[water] Main.DrawWaters 没解析到——mod 水样式的水面不动/不画");
            return;
        }

        var fade = MethodCode.PatchLoopEnd(body, end, from + HeaderInFloats, to + HeaderInFloats, exitOnEqual: false);
        var draw = MethodCode.PatchLoopEnd(body, end, from, to, exitOnEqual: true);

        if (fade == 1 && draw == 1) log.Info($"[water] DrawWaters 已放开（{from} → {to} 个样式）");
        else log.Warn($"[water] DrawWaters 循环没匹配上（fade={fade}, draw={draw}，各期望 1）——mod 水样式的水面不动/不画");
    }
}
