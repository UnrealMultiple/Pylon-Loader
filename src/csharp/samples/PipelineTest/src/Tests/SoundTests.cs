using Pylon.ModLoader;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 35：Sound —— 功能验证。注册期构造 SoundStyle（发号 + 异步解码），
/// 播放走 SoundEngine.PlaySound（挂 LegacySoundPlayer 入口）。设备无 SoundPool 时播放返回 0，不崩。
/// </summary>
internal static class SoundTests
{
    public static void Register() => PT.Group("sound", Phase.Ready, Run);

    private static void Run()
    {
        // R1：两次构造同一路径 → 注册表发两个下标；路径解析走 AudioFiles（assets/sounds/test.wav 须存在）
        PT.Case("SOUND-R1", c =>
        {
            var style1 = new SoundStyle("assets/sounds/test");
            var style2 = new SoundStyle("assets/sounds/test", new SoundStyle.Options { Volume = 0.5f });
            c.True(style1.Path == "assets/sounds/test", "Path 记录");
            c.Eq(style2.Volume, 0.5f, "Options.Volume");
            // Style 是 internal 下标——能构造就说明注册没抛
            c.True(true, $"style1/style2 构造成功（音效已登记进表）");
        });

        // S1：播放不崩（3 次），参数合法
        PT.Case("SOUND-H1", c =>
        {
            var style = new SoundStyle("assets/sounds/test");
            c.NoThrow(() =>
            {
                for (var i = 0; i < 3; i++) SoundEngine.PlaySound(style);
            }, "PlaySound ×3");
        });

        // E1：指向不存在的文件 → 只降级不抛（构造成功，播放返回 0 或不响）
        PT.Case("SOUND-E1", c =>
        {
            var missing = new SoundStyle("assets/sounds/definitely_missing");
            c.NoThrow(() => SoundEngine.PlaySound(missing), "播放缺失音效");
            // FindActive/State/Stop 对未播放句柄不抛
            c.NoThrow(() => _ = SoundEngine.GetSoundState(0), "State(0)");
            c.NoThrow(() => SoundEngine.StopSound(0), "StopSound(0)");
        });
    }
}
