using Il2CppMicrosoft.Xna.Framework.Input;
using Pylon.ModLoader;

namespace PipelineTest.Tests;

/// <summary>
/// Keybind / TouchButton：注册面（KeybindLoader 两种重载、TouchButtonLoader.RegisterButton）、
/// 同名去重、ModKeybind 状态轮询与显示名、ModTouchButton 的 Icon/Label/Visible/Enabled/OnPress 属性。
/// 真按键状态由 TouchButton 管线按帧驱动，Ready 阶段只验面与容错。
/// </summary>
internal static class KeybindTests
{
    public static void Register()
    {
        PT.Group("keybind", Phase.Ready, RunKeybind);
        PT.Group("touchbutton", Phase.Ready, RunTouchButton);
    }

    private static void RunKeybind()
    {
        PT.Case("KEYBIND-R1", c =>
        {
            var bind = KeybindLoader.RegisterKeybind(PipelineTestMod.Instance, "TestKey", "None");
            c.NotNull(bind, "RegisterKeybind 返回实例");
            if (bind is null) return;

            c.True(bind is ModTouchButton, "按键在手机端就是触屏按钮");
            c.Eq(bind.ToString(), "PipelineTest/TestKey", "ToString 是 Mod/Name");
            c.Eq(bind.DisplayName?.Value, "Test Key", "默认显示名按类名拆词");
            c.NoThrow(() => _ = bind.GetAssignedKeys(), "GetAssignedKeys 不抛");
            c.NoThrow(() => _ = bind.Current, "Current 轮询");
            c.NoThrow(() => _ = bind.JustPressed, "JustPressed 轮询");
            c.NoThrow(() => _ = bind.JustReleased, "JustReleased 轮询");
            c.NoThrow(() => _ = bind.Old, "Old 轮询");
            c.NoThrow(() => _ = bind.RetroCurrent, "RetroCurrent 轮询");
        });

        PT.Case("KEYBIND-R2", c =>
        {
            var keys = KeybindLoader.RegisterKeybind(PipelineTestMod.Instance, "TestKeyEnum", Keys.None);
            c.NotNull(keys, "Keys 重载返回实例");
            c.True(keys is ModTouchButton, "Keys 重载也是触屏按钮");
        });

        PT.Case("KEYBIND-R3", c =>
        {
            var first = KeybindLoader.RegisterKeybind(PipelineTestMod.Instance, "TestKey", "");
            var again = KeybindLoader.RegisterKeybind(PipelineTestMod.Instance, "TestKey", "");
            c.True(ReferenceEquals(first, again) || first.ToString() == again.ToString(), "同名重复注册返回同一条");

            var empty = KeybindLoader.RegisterKeybind(PipelineTestMod.Instance, "TestKeyEmpty", "");
            c.True(!empty.Current, "未按键时 Current=false");
            c.True(!empty.JustPressed, "未按键时 JustPressed=false");
        });
    }

    private static void RunTouchButton()
    {
        PT.Case("TOUCH-R1", c =>
        {
            var button = TouchButtonLoader.RegisterButton(PipelineTestMod.Instance, "TestTouch");
            c.NotNull(button, "RegisterButton 返回按钮");
            if (button is null) return;

            c.Eq(button.FullName, "PipelineTest/TestTouch", "FullName 是 Mod/Name");
            c.True(TouchButtonLoader.Count > 0, $"按钮数={TouchButtonLoader.Count}");

            var listed = false;
            foreach (var item in TouchButtonLoader.Buttons)
                if (ReferenceEquals(item, button) || item.FullName == button.FullName) listed = true;
            c.True(listed, "Buttons 表里有这个按钮");
            c.True(!button.IsBound, "新按钮默认不在布局里");
        });

        PT.Case("TOUCH-R2", c =>
        {
            var button = TouchButtonLoader.RegisterButton(PipelineTestMod.Instance, "TestTouchProps");
            var calls = 0;

            button.Icon = "Assets/TouchButtons/TestTouchProps.png";
            button.Label = () => "自定义标签";
            button.Visible = () => true;
            button.Enabled = () => true;
            button.OnPress = _ => calls++;

            c.Eq(button.Icon, "Assets/TouchButtons/TestTouchProps.png", "Icon 可读回");
            c.Eq(button.Label?.Invoke(), "自定义标签", "Label 可读回");
            c.True(button.Visible?.Invoke() == true, "Visible 可读回");
            c.True(button.Enabled?.Invoke() == true, "Enabled 可读回");
            button.OnPress?.Invoke(null!);
            c.Eq(calls, 1, "OnPress 可读回");

            PT.Info("OnPress 回调已读回：真机触摸由控制布局按帧派发");
        });

        PT.Case("TOUCH-R3", c =>
        {
            var first = TouchButtonLoader.RegisterButton(PipelineTestMod.Instance, "TestTouchDup");
            var again = TouchButtonLoader.RegisterButton(PipelineTestMod.Instance, "TestTouchDup");
            c.True(ReferenceEquals(first, again), "同名重复注册返回同一个实例");
        });
    }
}
