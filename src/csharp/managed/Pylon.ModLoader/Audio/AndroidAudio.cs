using Pylon.ModLoader.Jni;
using Pylon.ModLoader.Logging;

namespace Pylon.ModLoader.Audio;
/// <summary>
/// 全部调用在游戏线程（hook 都在游戏线程）；Jni 在每线程第一次调用时 attach。
/// </summary>
internal static class AndroidAudio
{
    private static readonly string SoundPoolBuilderClass = "android/media/SoundPool$Builder";
    private static readonly string MediaPlayerClass = "android/media/MediaPlayer";
    private static readonly string AudioAttributesBuilderClass = "android/media/AudioAttributes$Builder";

    private const int UsageGame = 14;
    private const int ContentSonification = 4;
    private const int ContentMusic = 2;

    private static bool _tried;
    private static bool _ok;
    private static JniObject? _pool;

    private static readonly Dictionary<string, int> SoundIds = new(StringComparer.Ordinal);

    internal static bool SoundAvailable
    {
        get { EnsurePool(); return _ok; }
    }

    private static void EnsurePool()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            var attributes = BuildAudioAttributes(ContentSonification);
            if (attributes == null)
            {
                _ok = false;
                Safe.Once("audio.pool.attrs", "AndroidAudio: AudioAttributes 建不起来（" + JniBridge.LastError + "）", true);
                return;
            }
            using var builder = JniObject.New(SoundPoolBuilderClass, "()V");
            if (builder == null)
            {
                attributes.Dispose();
                _ok = false;
                Safe.Once("audio.pool.builder", "AndroidAudio: SoundPool$Builder 建不起来（" + JniBridge.LastError + "）", true);
                return;
            }
            builder.Chain("setMaxStreams", "(I)Landroid/media/SoundPool$Builder;", 32);
            builder.Chain("setAudioAttributes", "(Landroid/media/AudioAttributes;)Landroid/media/SoundPool$Builder;", attributes.AsArg());
            _pool = builder.CallObject("build", "()Landroid/media/SoundPool;");
            attributes.Dispose();
            _ok = _pool != null;
        }
        catch (Exception e)
        {
            _ok = false;
            Safe.Once("audio.pool", "AndroidAudio: SoundPool 建不起来：" + e.Message, true);
        }
    }

    private static JniObject? BuildAudioAttributes(int contentType)
    {
        var builder = JniObject.New(AudioAttributesBuilderClass, "()V");
        if (builder == null) return null;
        builder.Chain("setUsage", "(I)Landroid/media/AudioAttributes$Builder;", UsageGame);
        builder.Chain("setContentType", "(I)Landroid/media/AudioAttributes$Builder;", contentType);
        var attributes = builder.CallObject("build", "()Landroid/media/AudioAttributes;");
        builder.Dispose();
        return attributes;
    }

    internal static int LoadSound(string file)
    {
        EnsurePool();
        if (!_ok || string.IsNullOrEmpty(file)) return 0;
        if (SoundIds.TryGetValue(file, out var known)) return known;
        try
        {
            using var path = JniBridge.NewString(file);
            var id = _pool!.CallInt("load", "(Ljava/lang/String;I)I", path.AsArg(), 1);
            if (id <= 0)
            {
                Safe.Once("audio.load:" + file, "AndroidAudio: SoundPool.load 拒绝 " + file + "：" + JniBridge.LastError, true);
                return 0;
            }
            SoundIds[file] = id;
            return id;
        }
        catch (Exception e)
        {
            Safe.Once("audio.load:" + file, "AndroidAudio: 加载失败 " + file + "：" + e.Message, true);
            return 0;
        }
    }

    internal static int PlaySound(int id, float volume, float rate)
    {
        if (!_ok || id <= 0) return 0;
        try
        {
            volume = Math.Clamp(volume, 0f, 1f);
            rate = Math.Clamp(rate, 0.5f, 2f);
            return _pool!.CallInt("play", "(IFFIIF)I", id, volume, volume, 1, 0, rate);
        }
        catch (Exception e)
        {
            Safe.Once("audio.play:" + id, "AndroidAudio: 播放失败：" + e.Message, true);
            return 0;
        }
    }

    internal static void StopSound(int stream)
    {
        if (!_ok || stream <= 0) return;
        try { _pool!.Call("stop", "(I)V", stream); }
        catch (Exception e) { Safe.Report("AndroidAudio.StopSound", e); }
    }

    /// <summary>时长（毫秒）：SoundPool 查不了时长，固定 1000 让 stream 至少活 1 秒，够 SoundPipeline 估算。</summary>
    internal static int SoundDuration(int id) => 1000;

    internal static void PauseAll()
    {
        if (!_ok) return;
        try { _pool!.Call("autoPause", "()V"); }
        catch (Exception e) { Safe.Once("audio.pause", "AndroidAudio: autoPause 失败：" + e.Message, true); }
        PauseMusicAll();
    }

    internal static void ResumeAll()
    {
        if (!_ok) return;
        try { _pool!.Call("autoResume", "()V"); }
        catch (Exception e) { Safe.Once("audio.resume", "AndroidAudio: autoResume 失败：" + e.Message, true); }
        ResumeMusicAll();
    }

    // ---------------- 音乐 ----------------

    private sealed class Track
    {
        internal int Id;
        internal string File = string.Empty;
        internal JniObject? Player;
        internal bool Prepared;
        internal bool Failed;
        internal bool PausedByApp;
        internal float Volume;
        internal int Waited;
    }

    private static readonly Dictionary<int, Track> Tracks = new();
    private static int _nextTrack = 1;

    internal static int RegisterMusic(string file)
    {
        var id = _nextTrack++;
        Tracks[id] = new Track { Id = id, File = file };
        return id;
    }

    internal static void PrimeMusic(int id)
    {
        if (!Tracks.TryGetValue(id, out var track) || track.Failed) return;
        if (track.Player == null) CreatePlayer(track);
    }

    internal static void SetMusicVolume(int id, float volume)
    {
        if (!Tracks.TryGetValue(id, out var track)) return;
        track.Volume = Math.Clamp(volume, 0f, 1f);
        Apply(track);
    }


    internal static void RestartMusic(int id)
    {
        if (!Tracks.TryGetValue(id, out var track)) return;
        track.PausedByApp = false;
        if (track.Player == null || !track.Prepared || track.Failed) return;
        try
        {
            if (track.Player.CallBool("isPlaying", "()Z")) track.Player.Call("pause", "()V");
            track.Player.Call("seekTo", "(I)V", 0);
            track.Volume = 0f;
            track.PausedByApp = false;
        }
        catch (Exception e) { Safe.Once("audio.music.restart:" + id, "AndroidAudio: 音乐重置失败：" + e.Message); }
    }

    private static void Apply(Track track)
    {
        if (track.Failed) return;
        if (track.Player == null)
        {
            if (track.Volume <= 0f) return;
            CreatePlayer(track);
            if (track.Player == null) return;
        }

        if (!track.Prepared)
        {
            var duration = 0;
            try { duration = track.Player.CallInt("getDuration", "()I"); }
            catch (Exception e)
            {
                track.Failed = true;
                Safe.Once("audio.music.duration:" + track.Id, "AndroidAudio: 音乐读时长失败：" + e.Message, true);
                return;
            }
            if (duration <= 0)
            {
                if (++track.Waited >= 300)
                {
                    track.Failed = true;
                    Safe.Once("audio.music.wait:" + track.Id,
                        "AndroidAudio: 音乐 5 秒还没准备好：" + track.File + "（" + JniBridge.LastError + "）", true);
                }
                return;
            }
            track.Prepared = true;
        }

        try
        {
            if (track.Volume > 0f)
            {
                track.Player.Call("setVolume", "(FF)V", track.Volume, track.Volume);
                if (!track.Player.CallBool("isPlaying", "()Z") && !track.PausedByApp)
                    track.Player.Call("start", "()V");
            }
            else if (track.Player.CallBool("isPlaying", "()Z"))
            {
                track.Player.Call("pause", "()V");
                track.Player.Call("seekTo", "(I)V", 0);
            }
        }
        catch (Exception e)
        {
            track.Failed = true;
            Safe.Once("audio.music.apply:" + track.Id, "AndroidAudio: 音乐控制失败：" + e.Message, true);
        }
    }

    private static void CreatePlayer(Track track)
    {
        try
        {
            var player = JniObject.New(MediaPlayerClass, "()V");
            if (player == null)
            {
                track.Failed = true;
                Safe.Once("audio.music.new:" + track.Id, "AndroidAudio: 音乐对象建不起来：" + JniBridge.LastError, true);
                return;
            }
            var attributes = BuildAudioAttributes(ContentMusic);
            if (attributes != null)
            {
                player.Call("setAudioAttributes", "(Landroid/media/AudioAttributes;)V", attributes.AsArg());
                attributes.Dispose();
            }
            using var path = JniBridge.NewString(track.File);
            player.Call("setDataSource", "(Ljava/lang/String;)V", path.AsArg());
            player.Call("setLooping", "(Z)V", JValue.Of(true));
            player.Call("prepareAsync", "()V");
            track.Player = player;
        }
        catch (Exception e)
        {
            track.Failed = true;
            Safe.Once("audio.music.prepare:" + track.Id, "AndroidAudio: 音乐准备失败：" + e.Message, true);
        }
    }

    internal static int MusicState(int id)
    {
        if (!Tracks.TryGetValue(id, out var track)) return -1;
        if (track.Failed) return -1;
        if (track.Player == null) return 0;
        if (!track.Prepared) return 1;
        try { return track.Player.CallBool("isPlaying", "()Z") ? 2 : 0; }
        catch { return -1; }
    }

    internal static void PauseMusicAll()
    {
        foreach (var track in Tracks.Values)
        {
            if (track.Player == null || !track.Prepared) continue;
            try
            {
                if (track.Player.CallBool("isPlaying", "()Z"))
                {
                    track.Player.Call("pause", "()V");
                    track.PausedByApp = true;
                }
            }
            catch (Exception e)
            {
                Safe.Once("audio.music.pause:" + track.Id, "AndroidAudio: 暂停失败：" + e.Message, true);
            }
        }
    }

    internal static void ResumeMusicAll()
    {
        foreach (var track in Tracks.Values)
        {
            if (!track.PausedByApp) continue;
            track.PausedByApp = false;
            Apply(track);
        }
    }
}
