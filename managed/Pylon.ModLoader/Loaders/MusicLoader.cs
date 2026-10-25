using System;
using System.Collections.Generic;
using System.IO;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>Mod 音乐：注册 Assets/Music 下的曲子拿槽位，给 ModSceneEffect/ModBiome/ModNPC 的 Music 用。</summary>
    public static class MusicLoader
    {
        internal sealed class Track
        {
            internal int Slot;
            internal string File;
            internal int MusicId;        // AndroidAudio 的音乐 id（>0 才有）
            internal float Fade;
            internal float Sent = -1f;
        }

        internal static readonly List<Track> Tracks = new();
        private static readonly Dictionary<string, int> Slots = new(StringComparer.Ordinal);
        private static readonly Dictionary<int, Dictionary<int, int>> MusicBoxes = new();
        private static readonly Dictionary<int, int> MusicBoxItems = new();
        private static int _base = -1;

        /// <summary>原版最后一个音乐槽之后（MusicID.Count）。</summary>
        internal static int Base
        {
            get
            {
                if (_base < 0)
                {
                    try { _base = Terraria.ID.MusicID.Count; }
                    catch { _base = 105; }
                    if (_base <= 0) _base = 105;
                }
                return _base;
            }
        }

        public static int MusicCount => Base + Tracks.Count;

        public static int GetMusicSlot(string path) => FindSlot(null, path);

        public static int GetMusicSlot(Mod mod, string path) => FindSlot(mod, path);

        public static bool MusicExists(string path) => Resolve(null, path) != null;

        public static bool MusicExists(Mod mod, string path) => Resolve(mod, path) != null;

        public static void AddMusicBox(Mod mod, int musicSlot, int itemType, int tileType, int tileFrameY = 0)
        {
            if (musicSlot <= 0 || tileType < 754)
                throw new ArgumentOutOfRangeException(nameof(musicSlot), "音乐盒要 Mod 曲子的槽位与 Mod tile");

            if (!MusicBoxes.TryGetValue(tileType, out var boxes))
                MusicBoxes[tileType] = boxes = new Dictionary<int, int>();
            boxes[tileFrameY] = musicSlot;
            if (itemType > 0) MusicBoxItems[itemType] = musicSlot;
        }

        public static bool IsMusicPlaying(int slot)
        {
            var track = TrackOf(slot);
            return track?.MusicId > 0 && AndroidAudio.MusicState(track.MusicId) == 2;
        }

        /// <summary>把一条 Mod 曲强制回到开头并从 0 淡入。用于 Boss 再次召唤时重置 BGM。</summary>
        public static void RestartMusic(int slot) => ModMusic.Restart(slot);

        private static int FindSlot(Mod mod, string path)
        {
            var file = Resolve(mod, path);
            if (file == null)
            {
                Safe.Once("music:" + path, $"MusicLoader: 找不到 '{path}'（Assets/Music 下的 .ogg/.wav/.mp3）", true);
                return 0;
            }
            if (Slots.TryGetValue(file, out var slot)) return slot;

            slot = Base + Tracks.Count;
            var track = new Track { Slot = slot, File = file, MusicId = AndroidAudio.RegisterMusic(file) };
            Tracks.Add(track);
            Slots[file] = slot;
            AndroidAudio.PrimeMusic(track.MusicId);   // 提前 prepare，召唤时 start 即时
            ModMusic.Install();
            return slot;
        }

        internal static Track TrackOf(int slot)
        {
            var index = slot - Base;
            return index >= 0 && index < Tracks.Count ? Tracks[index] : null;
        }

        internal static int MusicBoxSlot(int tileType, int frameY)
        {
            if (!MusicBoxes.TryGetValue(tileType, out var boxes)) return 0;
            var key = frameY - frameY % 36;
            return boxes.TryGetValue(key, out var slot) ? slot : 0;
        }

        internal static int MusicBoxItemSlot(int itemType) =>
            MusicBoxItems.TryGetValue(itemType, out var slot) ? slot : 0;

        private static string Resolve(Mod mod, string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var relative = path.Replace('\\', '/').TrimStart('/');
            if (!relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                relative = "Assets/" + relative;

            if (mod != null)
                return FindIn(mod.Directory, relative) ??
                       FindIn(mod.Directory, relative.Replace("Assets/", "assets/")) ??
                       FindIn(mod.Directory, LowerDirectory(relative));
            foreach (var candidate in ModLoader.Mods)
            {
                var found = FindIn(candidate.Directory, relative) ??
                            FindIn(candidate.Directory, relative.Replace("Assets/", "assets/")) ??
                            FindIn(candidate.Directory, LowerDirectory(relative));
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
            if (string.IsNullOrEmpty(root)) return null;
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

