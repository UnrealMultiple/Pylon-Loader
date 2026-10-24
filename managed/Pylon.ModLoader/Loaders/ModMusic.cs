using System;
using HarmonyLib;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>Mod 曲子的选择与淡入淡出：场景/NPC 优先级高过原版时接管，手机用 MediaPlayer 播。</summary>
    internal static class ModMusic
    {
        private const float FadeStep = 0.005f;
        private const float Audible = 0.25f;

        private static bool _installed;
        private static int _chosen = -1;
        private static int _chooseIn;
        private static bool _silenced;
        private static float _savedSilenceFade;
        private static Harmony _harmony;
        private static ModLogger _log;

        internal static void Configure(Harmony harmony, ModLogger log)
        {
            _harmony = harmony;
            _log = log;
        }

        internal static void Install()
        {
            if (_installed || _harmony == null) return;
            _installed = true;
            GameHooks.Patch(_harmony, _log, typeof(ModMusic), typeof(Main), "UpdateAudio", Type.EmptyTypes,
                postfixHandler: nameof(UpdateAudioPostfix));
        }

        private static void UpdateAudioPostfix()
        {
            if (Main.gameMenu)
            {
                FadeTo(-1);
                return;
            }

            if (--_chooseIn <= 0)
            {
                _chooseIn = 10;
                _chosen = Choose();
            }
            FadeTo(_chosen);
        }

        private static int Choose()
        {
            var best = -1;
            var bestPriority = -1;

            foreach (var scene in ModContent.GetContent<ModSceneEffect>())
            {
                bool active;
                try { active = scene.IsSceneEffectActive(Main.LocalPlayer); }
                catch (Exception e) { Safe.Report(scene.Name + ".IsSceneEffectActive", e); continue; }
                if (!active) continue;
                var music = scene.Music;
                if (music < 0) continue;
                if (scene.Priority > bestPriority)
                {
                    best = music;
                    bestPriority = scene.Priority;
                }
            }

            var margin = 5000f;
            var screen = Main.screenPosition;
            foreach (var npc in Main.npc)
            {
                if (npc == null || !npc.active) continue;
                var modNpc = ModContent.Find(ContentKind.Npc, npc.type) as ModNpc;
                if (modNpc == null) continue;
                var music = modNpc.Music;
                if (music < 0) continue;

                var center = npc.Center;
                if (center.X < screen.X - margin || center.X > screen.X + Main.screenWidth + margin) continue;
                if (center.Y < screen.Y - margin || center.Y > screen.Y + Main.screenHeight + margin) continue;

                if (modNpc.SceneEffectPriority > bestPriority)
                {
                    best = music;
                    bestPriority = modNpc.SceneEffectPriority;
                }
            }

            var box = MusicBox();
            if (box > 0) return box;
            return best >= MusicLoader.Base ? best : -1;
        }

        private static int MusicBox()
        {
            var screen = Main.screenPosition;
            var x0 = Math.Max(0, (int)(screen.X / 16f));
            var y0 = Math.Max(0, (int)(screen.Y / 16f));
            var x1 = Math.Min(Main.maxTilesX - 1, x0 + (int)(Main.screenWidth / 16f) + 2);
            var y1 = Math.Min(Main.maxTilesY - 1, y0 + (int)(Main.screenHeight / 16f) + 2);
            for (var x = x0; x <= x1; x++)
            {
                for (var y = y0; y <= y1; y++)
                {
                    var tile = Main.tile[x, y];
                    if (tile.type < 754 || tile.frameX < 36) continue;
                    var slot = MusicLoader.MusicBoxSlot(tile.type, tile.frameY);
                    if (slot > 0) return slot;
                }
            }
            return 0;
        }

        private static void FadeTo(int slot)
        {
            var main = MusicLoader.TrackOf(slot);
            var current = Main.curMusic;
            var mainAudible = main != null
                ? main.Fade > Audible
                : current <= 0 || Main.musicFade[current] > Audible;
            var volume = Main.musicVolume;

            foreach (var track in MusicLoader.Tracks)
            {
                if (track == main) track.Fade = Math.Min(1f, track.Fade + FadeStep);
                else if (mainAudible) track.Fade = Math.Max(0f, track.Fade - FadeStep);

                // 音量交给 Android 层：>0 才准备/播放，=0 暂停回开头。阈值必须远小于每帧 fade 增量（≈0.00375），
                // 否则音量卡在第一帧（MediaPlayer 在播但人耳听不见）——"playing=True 却无声"的根因。
                if (track.MusicId <= 0) continue;
                var target = track.Fade * volume;
                if (Math.Abs(target - track.Sent) < 0.0005f) continue;

                try { AndroidAudio.SetMusicVolume(track.MusicId, target); }
                catch (Exception e) { Safe.Once("music.volume." + track.Slot, e.Message); }
                track.Sent = target;
            }

            // 自己的曲子听得见时把游戏音乐压到 0：只设 newMusic=0 不会让正在播的原版曲淡出（会叠在一起），
            // 先把 musicFade[0] 抬到 1 再让 curMusic=0。
            SetSilence(main != null && main.Fade > Audible);
        }

        private static void SetSilence(bool on)
        {
            var fade = Main.musicFade;
            if (fade == null || fade.Length == 0) return;
            if (on)
            {
                if (!_silenced)
                {
                    _savedSilenceFade = fade[0];
                    _silenced = true;
                }
                fade[0] = 1f;
                Main.newMusic = 0;
            }
            else if (_silenced)
            {
                fade[0] = _savedSilenceFade;
                _silenced = false;
            }
        }
    }
}

