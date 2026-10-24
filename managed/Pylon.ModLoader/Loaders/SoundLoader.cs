using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;

namespace Pylon.ModLoader
{
    /// <summary>Mod 音效：在 Mod 目录找 ogg/wav/mp3，用 Android SoundPool 播（这版 Unity 造不出 AudioClip）。
    /// SoundId=1000 + Style 下标认出 Mod 声音，同路径同选项复用下标；注册时即异步解码。</summary>
    internal static class SoundLoader
    {
        private const int ModSoundId = 1000;

        private sealed class Stream
        {
            internal int Id;          // SoundPool stream
            internal long StartedMs;
            internal int DurationMs;
        }

        private sealed class Entry
        {
            internal SoundStyle Style;
            internal string File;
            internal int SoundId;         // SoundPool id
            internal readonly List<Stream> Active = new();
        }

        private static readonly List<Entry> Sounds = new();
        private static readonly Dictionary<string, int> ByKey = new(StringComparer.Ordinal);
        private static readonly Dictionary<int, Entry> ByHandle = new();
        private static int _nextHandle = 1;
        private static bool _installed;
        private static int _tickAge;

        internal static int Register(SoundStyle style)
        {
            if (style == null) return 0;
            var key = style.Path + "|" + style.Volume + "|" + style.Pitch + "|" + style.PitchVariance + "|" +
                      style.MaxInstances + "|" + style.SoundLimitBehavior;
            if (ByKey.TryGetValue(key, out var index)) return index;
            index = Sounds.Count;
            var entry = new Entry { Style = style, File = Resolve(style.Path) };
            if (entry.File != null) entry.SoundId = AndroidAudio.LoadSound(entry.File);   // 注册即开始解码
            Sounds.Add(entry);
            ByKey[key] = index;
            return index;
        }

        internal static void Install(Harmony harmony, ModLogger log)
        {
            if (_installed) return;
            _installed = true;
            GameHooks.Patch(harmony, log, typeof(SoundLoader), typeof(LegacySoundPlayer), "PlaySound",
                new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(float), typeof(float) },
                prefixHandler: nameof(PlayPrefix));
            // 每帧收尾：过期的 stream 从活动表里摘掉（SoundPool 没有结束通知，按时长估算）。
            GameHooks.Patch(harmony, log, typeof(SoundLoader), typeof(Main), "UpdateAudio", Type.EmptyTypes,
                postfixHandler: nameof(UpdatePostfix));
        }

        internal static int Play(SoundStyle style, Vector2? position)
        {
            if (style == null) return 0;
            var entry = Sounds[Register(style)];
            return Play(entry, position, 1f, 0f);
        }

        private static int Play(Entry entry, Vector2? position, float volumeScale, float pitchOffset)
        {
            if (entry.File == null)
            {
                Safe.Once("sound.missing:" + entry.Style.Path,
                    $"SoundLoader: 找不到 '{entry.Style.Path}'（Assets/ 下的 .ogg/.wav/.mp3）", true);
                return 0;
            }
            if (!AndroidAudio.SoundAvailable)
            {
                Safe.Once("sound.pool", "SoundLoader: Android SoundPool 不可用，Mod 音效无法播放", true);
                return 0;
            }

            var max = entry.Style.MaxInstances;
            Prune(entry);
            if (max > 0 && entry.Active.Count >= max)
            {
                if (entry.Style.SoundLimitBehavior == 0) return 0;   // IgnoreNew
                StopStream(entry, entry.Active[0]);
            }

            var volume = Math.Clamp(entry.Style.Volume * volumeScale, 0f, 1f) * Math.Clamp(Main.soundVolume, 0f, 1f);
            var pitch = entry.Style.Pitch + (float)(Random.Shared.NextDouble() * 2.0 - 1.0) * entry.Style.PitchVariance + pitchOffset;
            var rate = Math.Clamp((float)Math.Pow(2.0, pitch), 0.5f, 2f);

            // SoundPool 没有"解码完成"的查询：play 返回 0 就是还没就绪（同样拒绝，返回 0）。
            var streamId = AndroidAudio.PlaySound(entry.SoundId, volume, rate);
            if (streamId == 0)
            {
                Safe.Once("sound.notready:" + entry.Style.Path,
                    $"SoundLoader: '{entry.Style.Path}' 还没解码好/被拒，这次先不播", false);
                return 0;
            }

            var stream = new Stream
            {
                Id = streamId,
                StartedMs = Environment.TickCount64,
                DurationMs = Math.Max(1, AndroidAudio.SoundDuration(entry.SoundId)),
            };
            entry.Active.Add(stream);

            var handle = _nextHandle++;
            ByHandle[handle] = entry;
            return handle;
        }

        private static void UpdatePostfix()
        {
            if (Sounds.Count == 0) return;
            if (++_tickAge < 10) return;
            _tickAge = 0;
            foreach (var entry in Sounds) Prune(entry);
        }

        private static void Prune(Entry entry)
        {
            for (var i = entry.Active.Count - 1; i >= 0; i--)
            {
                var stream = entry.Active[i];
                if (Environment.TickCount64 - stream.StartedMs < stream.DurationMs) continue;
                entry.Active.RemoveAt(i);
            }
        }

        private static void StopStream(Entry entry, Stream stream)
        {
            try { AndroidAudio.StopSound(stream.Id); }
            catch (Exception e) { Safe.Report("SoundLoader.Stop", e); }
            entry.Active.Remove(stream);
        }

        internal static void StopHandle(int handle)
        {
            if (!ByHandle.TryGetValue(handle, out var entry)) return;
            ByHandle.Remove(handle);
            foreach (var stream in entry.Active.ToArray()) StopStream(entry, stream);
        }

        internal static int FindActive(SoundStyle style)
        {
            if (style == null) return 0;
            var entry = Sounds[Register(style)];
            Prune(entry);
            return entry.Active.Count;
        }

        internal static int State(int handle)
        {
            if (!ByHandle.TryGetValue(handle, out var entry)) return 0;
            Prune(entry);
            return entry.Active.Count > 0 ? 2 : 0;
        }

        /// <summary>App 进后台：音效和音乐都停，回来再继续。</summary>
        internal static void PauseAll() => AndroidAudio.PauseAll();

        internal static void ResumeAll() => AndroidAudio.ResumeAll();

        private static bool PlayPrefix(int type, int x, int y, int Style, float volumeScale, float pitchOffset,
                                       ref SoundEffectInstance __result)
        {
            if (type != ModSoundId || Style < 0 || Style >= Sounds.Count) return true;
            Play(Sounds[Style], x >= 0 && y >= 0 ? new Vector2(x, y) : null, volumeScale, pitchOffset);
            __result = null;
            return false;
        }

        private static string Resolve(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (Path.IsPathRooted(path) && File.Exists(path)) return path;

            var relative = path.Replace('\\', '/').TrimStart('/');
            if (!relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                relative = "Assets/" + relative;

            foreach (var mod in ModLoader.Mods)
            {
                if (string.IsNullOrEmpty(mod.Directory)) continue;
                var found = FindIn(mod.Directory, relative) ??
                            FindIn(mod.Directory, relative.Replace("Assets/", "assets/")) ??
                            FindIn(mod.Directory, LowerDirectory(relative));
                if (found != null) return found;
            }
            return null;
        }

        private static string LowerDirectory(string path)
        {
            var slash = path.LastIndexOf('/');
            return slash < 0 ? path.ToLowerInvariant()
                 : path.Substring(0, slash).ToLowerInvariant() + path.Substring(slash);
        }

        private static string FindIn(string root, string relative)
        {
            var fullRoot = Path.GetFullPath(root);
            foreach (var extension in new[] { "", ".ogg", ".wav", ".mp3" })
            {
                var full = Path.GetFullPath(Path.Combine(fullRoot, relative + extension));
                if (!full.StartsWith(fullRoot, StringComparison.Ordinal)) continue;
                if (File.Exists(full)) return full;
            }
            return null;
        }
    }
}
