using Il2CppMicrosoft.Xna.Framework;
using Pylon.Managed.Mods;
using Terraria;
using Terraria.GameContent;

namespace Pylon.Managed.Hosting;

/// <summary>
/// 内容就绪探测：游戏类型表建好（或兜底帧数到）后触发晚阶段注册。
/// 挂在游戏自己的 Main.Update 之后，只跑一次。
/// </summary>
internal static class GameReadyWatcher
{
    /// <summary>物品类型表建好后应有的长度（Terraria 1.4.5.8.6 原版数量）。</summary>
    private const int VanillaItemCount = 6196;

    /// <summary>兜底：探测一直不成立时的最长等待帧数（约 2 秒）。</summary>
    private const int MaxWaitFrames = 120;

    private static bool _armed;
    private static bool _fired;
    private static bool _diagnosed;
    private static int _frames;

    /// <summary>武装探测（幂等）。</summary>
    public static void Arm()
    {
        if (_armed) return;
        try
        {
            On.Terraria.Main.Update += OnUpdate;
            _armed = true;
            HostLog.Write("已武装内容就绪探测：每帧检查 Terraria.Main::Update");
        }
        catch (Exception e)
        {
            HostLog.Write("武装内容就绪探测失败：" + HostLog.Describe(e));
        }
    }

    private static void OnUpdate(On.Terraria.Main.orig_Update orig, Main self, GameTime gameTime)
    {
        orig(self, gameTime);
        Tick();
    }

    private static void Tick()
    {
        if (_fired) return;
        _frames++;

        // 两条判据先到先触发：① 游戏类型表已建好（内容注入需要的时机）；② 已跑够帧数（晚阶段只要游戏在跑）。
        var tablesReady = GameLooksReady();
        if (!tablesReady && _frames < MaxWaitFrames) return;

        _fired = true;
        HostLog.Write($"进入晚阶段（第 {_frames} 帧，类型表{(tablesReady ? "已" : "未")}就绪）→ 触发延迟注册");
        ModPipeline.RunContentReady();
    }

    private static bool GameLooksReady()
    {
        try
        {
            var items = TextureAssets.Item;
            if (items is null)
            {
                Diagnose("TextureAssets.Item 还是 null（内容表尚未建立）");
                return false;
            }
            if (items.Length < VanillaItemCount)
            {
                Diagnose($"TextureAssets.Item 长度 {items.Length} < {VanillaItemCount}");
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            Diagnose($"就绪探测异常：{e.GetType().Name}: {e.Message}");
            return false;
        }
    }

    private static void Diagnose(string message)
    {
        if (_diagnosed) return;
        _diagnosed = true;
        HostLog.Write("就绪探测：首次未通过 -> " + message);
    }
}
