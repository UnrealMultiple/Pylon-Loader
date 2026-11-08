using Pylon.ModLoader;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 18：Keybind —— 功能验证。KeybindLoader.RegisterKeybind 返回 ModTouchButton，
/// 轮询它的 Current/JustPressed 不抛即通过（真按键在 Tick 阶段由 TouchButton 管线驱动）。
/// </summary>
internal static class KeybindTests
{
    public static void Register() => PT.Group("keybind", Phase.Ready, Run);

    private static void Run()
    {
        PT.Case("KEYBIND-R1", c =>
        {
            var bind = KeybindLoader.RegisterKeybind(PipelineTestMod.Instance, "TestKey");
            c.NotNull(bind, "RegisterKeybind 返回 null");
            if (bind is { } b)
            {
                c.True(b.FullName.Length > 0, $"FullName={b.FullName}（应为空才对——FullName 是注册键）");
                c.NoThrow(() => _ = b.Current, "Current 轮询");
                c.NoThrow(() => _ = b.JustPressed, "JustPressed 轮询");
                c.NoThrow(() => _ = b.JustReleased, "JustReleased 轮询");
                c.NoThrow(() => _ = b.IsBound, "IsBound 轮询");
            }
        });

        // 重复注册同名：第二次调用应返回可用实例（覆盖行为）或抛——两者都行，关键是不崩
        PT.Case("KEYBIND-R2", c =>
        {
            c.NoThrow(() => KeybindLoader.RegisterKeybind(PipelineTestMod.Instance, "TestKey"), "重复注册同名");
        });

        // E1：轮询一个没注册任何键位的全新 ModTouchButton 也不崩（覆盖轮询路径容错）
        PT.Case("KEYBIND-E1", c =>
        {
            var bind = KeybindLoader.RegisterKeybind(PipelineTestMod.Instance, "TestKeyEmpty");
            if (bind is { } b)
            {
                c.True(!b.Current, "未按键时 Current=false");
                c.True(!b.JustPressed, "未按键时 JustPressed=false");
            }
        });
    }
}
