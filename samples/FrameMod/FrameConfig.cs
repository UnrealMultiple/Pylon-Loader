// FrameMod / FrameConfig —— 示例配置（对照 ConfigLoader 声明形态）
using System.Collections.Generic;
using Pylon.ModLoader;

namespace FrameMod
{
    public sealed class FrameConfig : ModConfig
    {
        public static Dictionary<string, object> Options = new()
        {
            ["Title"] = Header(),
            ["EnableThing"] = Toggle(true),
            ["Power"] = Range(50, 0, 100, 5, "%"),
            ["Mode"] = Radio("Normal", new[] { "Normal", "Hard" }),
            ["Link"] = Link("https://example.com"),
        };

        public override void OnLoaded() =>
            Mod.Logger.Info($"[config] 读盘完成：EnableThing={GetBool("EnableThing")} " +
                            $"Power={GetNumber("Power")} Mode={GetString("Mode")}");

        public override void OnChanged(string key) => Mod.Logger.Info($"[config] OnChanged {key} = {this[key]}");

        public override void OnApply() => Mod.Logger.Info("[config] OnApply");
    }
}