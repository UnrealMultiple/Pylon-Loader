using Pylon.ModLoader;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 22：Music —— 功能验证。MusicLoader.GetMusicSlot(mod, path) 发槽（原版 105 之后），
/// 缺文件返回 0；播放选曲依赖 SceneEffect/NPC（B4 联动），这里验证槽注册与 API 面不崩。
/// </summary>
internal static class MusicTests
{
    public static void Register()
    {
        PT.Group("music", Phase.Ready, Run);
        PT.Group("music-w", Phase.World, RunWorld);
    }

    /// <summary>听感验证：直接播放 mod 槽，应能听到 Tone.ogg 的音调。</summary>
    private static void RunWorld()
    {
        PT.Case("MUSIC-W1", c =>
        {
            var slot = MusicLoader.GetMusicSlot(PipelineTestMod.Instance, "assets/music/test");
            c.True(slot > 0, $"slot={slot}");

            c.NoThrow(() => MusicLoader.RestartMusic(slot), "RestartMusic(slot)");
            PT.Info($"正在播放槽 {slot} 的测试音乐（assets/music/test.ogg），应能听到循环音调");
        });
    }

    private static void Run()
    {
        PT.Case("MUSIC-R1", c =>
        {
            var slot = MusicLoader.GetMusicSlot(PipelineTestMod.Instance, "assets/music/test");
            if (slot == 0)
            {
                // 文件缺失时返回 0——这是合法降级；改用"槽位 API 面"验证
                var missing = MusicLoader.GetMusicSlot(PipelineTestMod.Instance, "assets/music/definitely_missing");
                c.Eq(missing, 0, "缺文件 GetMusicSlot 返回 0");
            }
            else
            {
                c.True(slot >= 105, $"slot={slot}（原版 105 个：0..104，mod 从 105 起）");
            }
        });

        PT.Case("MUSIC-R2", c =>
        {
            c.NoThrow(() => _ = MusicLoader.MusicExists(PipelineTestMod.Instance, "assets/music/test"), "MusicExists 查询");
            c.NoThrow(() => MusicLoader.StopMusic(), "StopMusic 空表调用");
            c.NoThrow(() => MusicLoader.RestartMusic(999), "RestartMusic 未注册槽");
        });

        // E1：槽 0 = 无音乐
        PT.Case("MUSIC-E1", c =>
        {
            c.NoThrow(() => _ = MusicLoader.IsMusicPlaying(0), "IsMusicPlaying(0)");
        });
    }
}