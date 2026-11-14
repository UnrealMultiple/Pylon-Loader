using Pylon.NaviteAPI.Host;
using Terraria.GameContent.UI.Chat;

namespace Pylon.ModLoader;

/// <summary>
/// 聊天标签：[i:类型]/[ct:类型,控制] 的类型上限 6195 编进 movz 立即数，mod 物品只能显示原文；
/// 顺带修 PrintInline 的颜色判断（tst #0xff00 只留蓝色通道，蓝色为 0 的物品被顶成默认蓝）。
/// </summary>
internal static class ChatTagPatches
{
    // 颜色判断三连 + cbz w8 掩码 + nop。
    private const uint TstBlue = 0x72181EDF;  // tst w22, #0xff00
    private const uint TstRgb = 0x72185EDF;   // tst w22, #0xffffff00
    private const uint LsrRed = 0x53187EC8;   // lsr w8, w22, #24
    private const uint AndGreen = 0x12101EC8; // and w8, w22, #0xff0000
    private const uint Nop = 0xD503201F;

    private static bool _installed;
    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>颜色修复无条件装（原版物品也受益）；上限补丁有 mod 物品才动。</summary>
    internal static void Build(IModLog log)
    {
        if (_installed) return;
        _installed = true;

        FixColor(log);

        var count = ModContent.Count(ContentKind.Item);
        if (count <= 0) return;

        var from = (ushort)(VanillaCounts.Item - 1);
        var to = (ushort)(VanillaCounts.Item + count - 1);

        var measure = PatchTag(MethodCode.KlassOf<ItemTagHandler>(), "MeasureInline", 4, from, to);
        var print = PatchTag(MethodCode.KlassOf<ItemTagHandler>(), "PrintInline", 8, from, to);
        var touchMeasure = PatchTag(MethodCode.KlassOf<ControlsTouchTagHandler>(), "MeasureInline", 4, from, to);
        var touchPrint = PatchTag(MethodCode.KlassOf<ControlsTouchTagHandler>(), "PrintInline", 8, from, to);
        if (measure == 1 && print == 1 && touchMeasure == 2 && touchPrint == 2)
        {
            log.Info($"[item] 聊天标签已放开（[i]/[ct] 类型 ≤ {to}）；物品颜色判断已修");
        }
        else
        {
            log.Warn($"[item] 聊天标签上限没全匹配（[i] Measure={measure}/Print={print}，" +
                     $"[ct] Measure={touchMeasure}/Print={touchPrint}）——mod 物品在聊天显示原文");
        }
    }

    private static int PatchTag(IntPtr klass, string method, int argc, ushort from, ushort to)
    {
        if (!MethodCode.TryResolve(klass, method, argc, out var body, out var end)) return -1;
        return MethodCode.PatchMovzImm16(body, end, from, to);
    }

    /// <summary>tst 掩码放宽到全 RGB、两条 cbz nop——作者色非黑就照画；第一处命中即收手。</summary>
    private static unsafe void FixColor(IModLog log)
    {
        if (!MethodCode.TryResolve(MethodCode.KlassOf<ItemTagHandler>(), "PrintInline", 8, out var body, out var end))
        {
            log.Warn("[item] ItemTagHandler.PrintInline 没解析到，颜色判断不修");
            return;
        }

        var last = (uint*)end;
        for (var p = (uint*)body; p + 6 <= last; p++)
        {
            if (p[0] != TstBlue || p[2] != LsrRed || p[4] != AndGreen) continue;
            if (!IsCbzW8(p[3]) || !IsCbzW8(p[5])) continue;

            var ok = PatchNative.PatchInstruction((IntPtr)p, 0, TstBlue, TstRgb) == 0 &&
                     PatchNative.PatchInstruction((IntPtr)(p + 3), 0, p[3], Nop) == 0 &&
                     PatchNative.PatchInstruction((IntPtr)(p + 5), 0, p[5], Nop) == 0;
            if (!ok) log.Warn("[item] PrintInline 颜色判断改写被内核拒绝");
            return;
        }

        log.Warn("[item] PrintInline 颜色判断模式没找到（游戏版本变了）");
    }

    private static bool IsCbzW8(uint insn) => (insn & 0xFF00001Fu) == 0x34000008u;
}
