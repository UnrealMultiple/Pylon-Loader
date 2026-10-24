using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;

namespace Pylon.ModLoader.Effects;

/// <summary>屏幕效果的异常上报口：按"谁 + 异常"记一条，不让一个效果打断整帧。</summary>
internal static class ScreenReport
{
    public static void Report(string what, Exception e)
    {
        try { LoaderHost.Current.Log.Error($"[fx] {what}：{Safe.Describe(e)}"); }
        catch { }
    }
}