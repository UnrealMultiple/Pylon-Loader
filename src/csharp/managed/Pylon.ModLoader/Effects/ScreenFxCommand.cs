using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;

namespace Pylon.ModLoader.Effects;

/// <summary>聊天命令 <c>/fx</c>：列效果、生成、结束、热重载、开关与统计。</summary>
internal sealed class ScreenFxCommand : ModCommand
{
    private static readonly Color ErrorColor = new(255, 25, 25, 255);

    public override string Command => "fx";

    public override CommandType Type => CommandType.Chat;

    public override string Usage => "/fx list | spawn <Effect> [radius] [ticks] | kill [key|Effect] | reload | watch | debug | on | off | stats";

    public override string Description => "屏幕效果调试";

    public override void Action(CommandCaller caller, string input, string[] args)
    {
        string what = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        switch (what)
        {
            case "list":
            {
                var active = ScreenEffects.Active;
                if (active.Count == 0)
                {
                    caller.Reply("当前没有屏幕效果。");
                    return;
                }
                foreach (ModScreenEffect fx in active) caller.Reply(ScreenDebug.Describe(fx));
                return;
            }
            case "spawn":
            {
                Type? type = args.Length > 1
                    ? ScreenEffects.Classes.FirstOrDefault(t => t.Name.Equals(args[1], StringComparison.OrdinalIgnoreCase))
                    : null;
                if (type == null)
                {
                    caller.Reply("没有这个效果。可用：" + string.Join(", ", ScreenEffects.Classes.Select(t => t.Name)), ErrorColor);
                    return;
                }
                float radius = args.Length > 2 && float.TryParse(args[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float r) && r > 0 ? r : -1;
                float ticks = args.Length > 3 && float.TryParse(args[3], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float t) && t >= 0 ? t : -1;
                ScreenEffects.Spawn(type, caller.Player, fx =>
                {
                    if (radius > 0) fx.Radius = radius;
                    if (ticks >= 0) fx.Duration = ticks;
                });
                caller.Reply($"已生成 {type.Name}。");
                return;
            }
            case "kill":
            {
                string? target = args.Length > 1 ? args[1].ToLowerInvariant() : null;
                int killed = 0;
                foreach (ModScreenEffect fx in ScreenEffects.Active)
                {
                    string key = fx.Key?.ToLowerInvariant() ?? "";
                    if (target == null || target == "all" || key == target || fx.GetType().Name.ToLowerInvariant() == target)
                    {
                        fx.Kill();
                        killed++;
                    }
                }
                caller.Reply($"已结束 {killed} 个效果。");
                return;
            }
            case "reload":
            {
                int files = ScreenGraphics.Shader.Files.Count;
                caller.Reply($"热重载：{ScreenEffects.Reload()} 个 shader 变了（共 {files} 个文件）。");
                return;
            }
            case "watch":
                ScreenEffects.HotReload = !ScreenEffects.HotReload;
                caller.Reply(ScreenEffects.HotReload ? "热重载：开（每 30 帧检查一次）" : "热重载：关");
                return;
            case "debug":
                ScreenEffects.Debug = !ScreenEffects.Debug;
                caller.Reply(ScreenEffects.Debug ? "效果调试显示：开" : "效果调试显示：关");
                return;
            case "on":
            case "off":
                ScreenEffectsSetting.Set(what == "on");
                caller.Reply(what == "on" ? "屏幕效果：开" : "屏幕效果：关");
                return;
            case "stats":
            {
                ScreenStats s = ScreenEffects.Stats;
                caller.Reply($"GPU {(s.GpuSamples > 0 ? $"{s.GpuAverageMs:0.00} ms" : "-")} · pass {s.Passes} · 帧 {s.Frames} · marker {(s.MarkerReady ? "ok" : "-")}");
                return;
            }
            default:
                throw new UsageException();
        }
    }
}