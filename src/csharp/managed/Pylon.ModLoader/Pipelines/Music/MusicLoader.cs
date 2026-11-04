namespace Pylon.ModLoader;

/// <summary>
/// 公开的音乐入口：作者用 GetMusicSlot 拿槽位、AddMusicBox 登记音乐盒。
/// 状态在 <see cref="MusicPipeline"/>；这里只转发，因为作者是按静态 API 编译的。
/// </summary>
public static class MusicLoader
{
    /// <summary>原版音乐槽之后的槽位总数（含已登记的 Mod 曲）。</summary>
    public static int MusicCount => MusicPipeline.Instance.MusicCount;

    public static int GetMusicSlot(string path) => MusicPipeline.Instance.GetSlot(null, path);

    public static int GetMusicSlot(Mod mod, string path) => MusicPipeline.Instance.GetSlot(mod, path);

    public static bool MusicExists(string path) => MusicPipeline.Instance.MusicExists(null, path);

    public static bool MusicExists(Mod mod, string path) => MusicPipeline.Instance.MusicExists(mod, path);

    /// <summary>把 Mod 曲子绑到音乐盒方块（按 frameY 区分样式）或音乐盒道具（itemType &gt; 0 时）。</summary>
    public static void AddMusicBox(Mod mod, int musicSlot, int itemType, int tileType, int tileFrameY = 0) =>
        MusicPipeline.Instance.AddMusicBox(musicSlot, itemType, tileType, tileFrameY);

    /// <summary>这首 Mod 曲现在是否真的在出声（MediaPlayer 正在播）。</summary>
    public static bool IsMusicPlaying(int slot) => MusicPipeline.Instance.IsPlaying(slot);

    /// <summary>把一条 Mod 曲强制回到开头并从 0 淡入。用于 Boss 再次召唤时重置 BGM。</summary>
    public static void RestartMusic(int slot) => MusicPipeline.Instance.Restart(slot);

    /// <summary>Boss 死亡：淡出并停掉当前 Mod 曲，避免死亡动画期间 BGM 还在响。</summary>
    public static void StopMusic() => MusicPipeline.Instance.Stop();
}
