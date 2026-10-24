using System;
using System.Collections.Generic;

namespace Pylon.ModLoader
{
    /// <summary>
    /// Mod 音频的 Android 层：经内核 JNI 反射桥调 Java（SoundPool / MediaPlayer），不碰 Unity 的
    /// AndroidJavaObject（这版 IL2CPP 泛型 Call&lt;T&gt; 未实例化会抛 "not fully instantiated"）。
    /// 音效：SoundPool 异步解码（play 返回 0 = 未就绪/被拒）；音乐：MediaPlayer 异步准备（getDuration 轮询）。
    /// 全部调用在游戏线程（hook 都在游戏线程）；内核自动 attach。
    /// </summary>
    internal static class AndroidAudio
    {
        private static readonly string SoundPoolBuilderClass = "android/media/SoundPool$Builder";
        private static readonly string MediaPlayerClass = "android/media/MediaPlayer";
        private static readonly string AudioAttributesBuilderClass = "android/media/AudioAttributes$Builder";

        // AudioAttributes 常量：usage GAME、音效 SONIFICATION、音乐 MUSIC。
        private const int UsageGame = 14;              // AudioAttributes.USAGE_GAME
        private const int ContentSonification = 4;     // AudioAttributes.CONTENT_TYPE_SONIFICATION
        private const int ContentMusic = 2;            // AudioAttributes.CONTENT_TYPE_MUSIC

        private static bool _tried;
        private static bool _ok;
        private static JniObject _pool;

        private static readonly Dictionary<string, int> SoundIds = new(StringComparer.Ordinal);

        internal static bool SoundAvailable
        {
            get { EnsurePool(); return _ok; }
        }

        /// <summary>AudioAttributes（USAGE_GAME + SONIFICATION）→ SoundPool.Builder（32 路）；
        /// 不设 attributes 时部分 ROM 会把声音塞进通话/系统流，音量条管不到（真机"没声音"）。</summary>
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
                    Safe.Once("audio.pool.attrs", "AndroidAudio: AudioAttributes 建不起来（" + Jni.LastError + "）", true);
                    return;
                }
                using var builder = JniObject.New(SoundPoolBuilderClass, "()V");
                if (builder == null)
                {
                    attributes.Dispose();
                    _ok = false;
                    Safe.Once("audio.pool.builder", "AndroidAudio: SoundPool$Builder 建不起来（" + Jni.LastError + "）", true);
                    return;
                }
                builder.Call("setMaxStreams", "(I)V", 32);
                builder.Call("setAudioAttributes", "(Landroid/media/AudioAttributes;)V", attributes.AsArg());
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

        /// <summary>new AudioAttributes$Builder().setUsage(usage).setContentType(kind).build()；用完 Dispose。</summary>
        private static JniObject BuildAudioAttributes(int contentType)
        {
            var builder = JniObject.New(AudioAttributesBuilderClass, "()V");
            if (builder == null) return null;
            builder.Call("setUsage", "(I)V", UsageGame);
            builder.Call("setContentType", "(I)V", contentType);
            var attributes = builder.CallObject("build", "()Landroid/media/AudioAttributes;");
            builder.Dispose();
            return attributes;
        }

        // ---------------- 音效 ----------------

        /// <summary>开始异步解码；返回 SoundPool 的 id（0 = 不支持的路径/建不起来）。同一文件只加载一次。</summary>
        internal static int LoadSound(string file)
        {
            EnsurePool();
            if (!_ok || string.IsNullOrEmpty(file)) return 0;
            if (SoundIds.TryGetValue(file, out var known)) return known;
            try
            {
                using var path = Jni.NewString(file);
                var id = _pool.CallInt("load", "(Ljava/lang/String;I)I", path.AsArg(), 1);
                if (id <= 0)
                {
                    Safe.Once("audio.load:" + file, "AndroidAudio: SoundPool.load 拒绝 " + file + "：" + Jni.LastError, true);
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

        /// <summary>播放；返回 stream id（0 = 还没解码好 / 被拒）。音量 0..1，rate 0.5..2。</summary>
        internal static int PlaySound(int id, float volume, float rate)
        {
            if (!_ok || id <= 0) return 0;
            try
            {
                volume = Math.Clamp(volume, 0f, 1f);
                rate = Math.Clamp(rate, 0.5f, 2f);
                return _pool.CallInt("play", "(IFFIIF)I", id, volume, volume, 1, 0, rate);
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
            try { _pool.Call("stop", "(I)V", stream); }
            catch (Exception e) { Safe.Report("AndroidAudio.StopSound", e); }
        }

        /// <summary>时长（毫秒）：SoundPool 查不了时长，固定 1000 让 stream 至少活 1 秒，够 SoundLoader 估算。</summary>
        internal static int SoundDuration(int id) => 1000;

        internal static void PauseAll()
        {
            if (!_ok) return;
            try { _pool.Call("autoPause", "()V"); } catch { }
            PauseMusicAll();
        }

        internal static void ResumeAll()
        {
            if (!_ok) return;
            try { _pool.Call("autoResume", "()V"); } catch { }
            ResumeMusicAll();
        }

        // ---------------- 音乐 ----------------

        private sealed class Track
        {
            internal int Id;
            internal string File;
            internal JniObject Player;
            internal bool Prepared;
            internal bool Failed;
            internal bool PausedByApp;
            internal float Volume;
            internal int Waited;
        }

        private static readonly Dictionary<int, Track> Tracks = new();
        private static int _nextTrack = 1;

        /// <summary>登记一条音乐（还不读文件）。返回 id（>0）。</summary>
        internal static int RegisterMusic(string file)
        {
            var id = _nextTrack++;
            Tracks[id] = new Track { Id = id, File = file };
            return id;
        }

        /// <summary>音乐当前音量（0..1，fade 已乘进去）：>0 准备/播放，=0 暂停并回到开头；准备异步，就绪用 getDuration 轮询。</summary>
        internal static void SetMusicVolume(int id, float volume)
        {
            if (!Tracks.TryGetValue(id, out var track)) return;
            track.Volume = Math.Clamp(volume, 0f, 1f);
            Apply(track);
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
                // prepareAsync 没回调：getDuration 在准备好之前是 0（或抛，内核已清异常返回 0）。
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
                            "AndroidAudio: 音乐 5 秒还没准备好：" + track.File + "（" + Jni.LastError + "）", true);
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
                    Safe.Once("audio.music.new:" + track.Id, "AndroidAudio: 音乐对象建不起来：" + Jni.LastError, true);
                    return;
                }
                // 先设 AudioAttributes（USAGE_GAME + MUSIC）再 setDataSource。
                var attributes = BuildAudioAttributes(ContentMusic);
                if (attributes != null)
                {
                    player.Call("setAudioAttributes", "(Landroid/media/AudioAttributes;)V", attributes.AsArg());
                    attributes.Dispose();
                }
                using var path = Jni.NewString(track.File);
                player.Call("setDataSource", "(Ljava/lang/String;)V", path.AsArg());
                player.Call("setLooping", "(Z)V", JValue.Of(true));
                player.Call("prepareAsync", "()V");
                track.Player = player;   // 所有权转移给 track；音量归零时 Apply 会 pause+seek，生命周期跟随会话
            }
            catch (Exception e)
            {
                track.Failed = true;
                Safe.Once("audio.music.prepare:" + track.Id, "AndroidAudio: 音乐准备失败：" + e.Message, true);
            }
        }

        /// <summary>-1 失败、0 停、1 准备中、2 播放中。</summary>
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
                catch { }
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
}