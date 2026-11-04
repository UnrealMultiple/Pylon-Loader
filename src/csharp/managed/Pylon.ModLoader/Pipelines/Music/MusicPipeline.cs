using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>
/// 音乐管线：每帧选曲、Mod 曲淡入淡出、把原版音乐压掉，以及前后台暂停。
/// 旧的 ModMusic 模块已并入这里：选曲与淡出共用同一份状态，不再跨类读表。
/// </summary>
internal sealed class MusicPipeline : ILoaderModule
{
    public static MusicPipeline Instance { get; } = new();

    private const float FadeStep = 0.005f;

    /// <summary>听得见的阈值：低于它就算"还没起来"，此时不压原版音乐。</summary>
    private const float Audible = 0.25f;

    /// <summary>下发音量的死区：必须远小于每帧增量，否则音量会卡在第一帧（在播但听不见）。</summary>
    private const float VolumeEpsilon = 0.0005f;

    private const int ChooseInterval = 10;

    private readonly MusicTable _table = new();

    private int _chosen = -1;
    private int _lastChosen = -1;
    private int _chooseIn;
    private bool _silenced;
    private bool _stopped;
    private bool _gamePaused;
    private float _savedSilenceFade;

    public string Name => "music";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        VerifyVanillaCount();
        On.Terraria.Main.UpdateAudio += UpdateAudio;

        On.XNAUnityRunner.OnApplicationPause += OnApplicationPause;

        Log.Info("[music] 已挂（每 10 帧选曲/每帧淡出；后台暂停 MediaPlayer）");
        return true;
    }

    internal int MusicCount => _table.Base + _table.Count;

    /// <summary>槽位基数是钉住的常量；运行时值不同说明游戏换了版本，槽位会错位，必须喊出来。</summary>
    private static void VerifyVanillaCount()
    {
        try
        {
            var actual = MusicID.Count;
            if (actual != MusicTable.VanillaCount)
            {
                Log.Warn($"[music] 原版音乐槽数 {actual} 与本版常量 {MusicTable.VanillaCount} 不一致：" +
                         "Mod 曲槽位可能与原版撞车，请更新 MusicTable.VanillaCount");
            }
        }
        catch (Exception e)
        {
            Log.Warn("[music] 读不到 MusicID.Count：" + Safe.Describe(e));
        }
    }

    internal int GetSlot(Mod? mod, string path) => _table.Register(mod, path);

    internal bool MusicExists(Mod? mod, string path) => _table.Exists(mod, path);

    internal void AddMusicBox(int slot, int itemType, int tileType, int frameY) =>
        _table.AddBox(slot, itemType, tileType, frameY);

    internal bool IsPlaying(int slot)
    {
        var index = _table.IndexOf(slot);
        if (index < 0) return false;

        ref var track = ref _table.TrackAt(index);
        return track.MusicId > 0 && AndroidAudio.MusicState(track.MusicId) == 2;
    }

    /// <summary>Boss 再次召唤：回到开头、立即满音量淡入。由作者在生成时调用。</summary>
    internal void Restart(int slot)
    {
        var index = _table.IndexOf(slot);
        if (index < 0) return;

        ref var track = ref _table.TrackAt(index);
        AndroidAudio.RestartMusic(track.MusicId);
        track.Fade = 1f;
        track.Sent = -1f;
        _lastChosen = slot;
        _chosen = slot;
        _stopped = false;
        Log.Info($"[music] 显式重置 slot={slot}（{_table.PathAt(index)}）");
    }

    /// <summary>Boss 死亡：淡出并停住；死亡动画期间 NPC 还 active，不挡住的话会再被选中。</summary>
    internal void Stop()
    {
        _stopped = true;
        _chosen = -1;
        _lastChosen = -1;
        FadeTo(-1);
    }

    private void UpdateAudio(On.Terraria.Main.orig_UpdateAudio orig, Main self)
    {
        orig(self);

        try
        {
            Tick();
        }
        catch (Exception e)
        {
            Safe.Report("music.tick", e);
        }
    }

    private void Tick()
    {
        if (Main.gameMenu || _stopped)
        {
            FadeTo(-1);
            return;
        }

        // 回来再继续，避免暂停期间音乐继续走、回来对不上节拍。
        if (Main.gamePaused)
        {
            if (!_gamePaused)
            {
                _gamePaused = true;
                AndroidAudio.PauseMusicAll();
            }
            return;
        }

        if (_gamePaused)
        {
            _gamePaused = false;
            AndroidAudio.ResumeMusicAll();
        }

        if (--_chooseIn <= 0)
        {
            _chooseIn = ChooseInterval;
            _chosen = Choose();
        }

        FadeTo(_chosen);
    }

    /// <summary>选曲：音乐盒 &gt; 场景效果（Priority 高者）&gt; NPC（Priority 高者）；同分先注册的赢。</summary>
    private int Choose()
    {
        var best = -1;
        var bestPriority = -1;

        var scenes = ModContent.GetContent<ModSceneEffect>();
        for (var i = 0; i < scenes.Count; i++)
        {
            var scene = scenes[i];
            int music;

            try
            {
                if (!scene.IsSceneEffectActive(Main.LocalPlayer)) continue;
                music = scene.Music;
            }
            catch (Exception e)
            {
                Safe.Report(scene.Name + ".IsSceneEffectActive", e);
                continue;
            }

            if (music < 0) continue;
            if (scene.Priority > bestPriority)
            {
                best = music;
                bestPriority = scene.Priority;
            }
        }

        // 原版 NPC 直接跳过：这一层只服务 Mod NPC，省掉每帧几百次内容表查找。
        var npcBase = ContentKindTable.BaseCount(ContentKind.Npc);
        const float margin = 5000f;
        var screen = Main.screenPosition;
        var npcs = Main.npc;
        for (var i = 0; i < npcs.Length; i++)
        {
            var npc = npcs[i];
            if (npc is null || !npc.active || npc.type < npcBase) continue;
            if (ModContent.Find(ContentKind.Npc, npc.type) is not ModNpc modNpc) continue;

            int music;
            try
            {
                music = modNpc.Music;
            }
            catch (Exception e)
            {
                Safe.Report(modNpc.Name + ".Music", e);
                continue;
            }

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

        var box = ActiveBox();
        if (box > 0) return box;
        return best >= _table.Base ? best : -1;
    }

    /// <summary>开着的音乐盒：屏幕内的方块优先，其次是饰品/时装栏里的音乐盒道具。</summary>
    private int ActiveBox()
    {
        if (_table.BoxCount == 0) return 0;

        var tileBase = ContentKindTable.BaseCount(ContentKind.Tile);
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
                if (tile.type < tileBase || tile.frameX < 36) continue;

                var slot = _table.SlotForTile(tile.type, tile.frameY);
                if (slot > 0) return slot;
            }
        }

        // 3~19 是饰品与社交饰品位；10~12 是时装护甲，跳过（与原版音乐盒的播放位一致）。
        var armor = Main.LocalPlayer.armor;
        if (armor is null) return 0;

        var limit = Math.Min(armor.Length, 20);
        for (var i = 3; i < limit; i++)
        {
            if (i is >= 10 and < 13) continue;

            var item = armor[i];
            if (item is null) continue;

            var music = _table.SlotForItem(item.type);
            if (music > 0) return music;
        }

        return 0;
    }

    private void FadeTo(int slot)
    {
        var index = _table.IndexOf(slot);

        // 从"没有 Mod 曲"或换曲时回到开头：MediaPlayer 可能停在中间，不 seek 就会接着放。
        if (slot != _lastChosen)
        {
            if (index >= 0)
            {
                ref var track = ref _table.TrackAt(index);
                AndroidAudio.RestartMusic(track.MusicId);
                track.Fade = 0f;
                track.Sent = -1f;
                Log.Info($"[music] 切到 slot={slot}（{_table.PathAt(index)}），从头播放");
            }

            _lastChosen = slot;
        }

        var mainAudible = index >= 0
            ? _table.TrackAt(index).Fade > Audible
            : VanillaAudible();

        var volume = Main.musicVolume;
        for (var i = 0; i < _table.Count; i++)
        {
            ref var track = ref _table.TrackAt(i);
            if (i == index) track.Fade = Math.Min(1f, track.Fade + FadeStep);
            else if (mainAudible) track.Fade = Math.Max(0f, track.Fade - FadeStep);

            if (track.MusicId <= 0) continue;

            var target = track.Fade * volume;
            if (Math.Abs(target - track.Sent) < VolumeEpsilon) continue;

            try { AndroidAudio.SetMusicVolume(track.MusicId, target); }
            catch (Exception e) { Safe.Once("music.volume." + track.Slot, e.Message); }

            track.Sent = target;
        }

        SetSilence(index >= 0 && _table.TrackAt(index).Fade > Audible);
    }

    private static bool VanillaAudible()
    {
        var current = Main.curMusic;
        if (current <= 0) return true;

        var fade = Main.musicFade;
        return fade is null || (uint)current >= (uint)fade.Length || fade[current] > Audible;
    }

    /// <summary>Mod 曲听得见时把原版音乐压掉：只设 newMusic = 0 不会让正在播的原版曲淡出，会叠在一起。</summary>
    private void SetSilence(bool on)
    {
        var fade = Main.musicFade;
        if (fade is null || fade.Length == 0) return;

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

    /// <summary>真正切到后台（Home / 切换应用）时 Unity 会暂停游戏循环，必须在这里停掉 MediaPlayer。</summary>
    private void OnApplicationPause(On.XNAUnityRunner.orig_OnApplicationPause orig, XNAUnityRunner self, bool paused)
    {
        orig(self, paused);

        try
        {
            if (paused)
            {
                AndroidAudio.PauseMusicAll();
                return;
            }

            AndroidAudio.ResumeMusicAll();
            _chooseIn = 0;
        }
        catch (Exception e)
        {
            Safe.Report("music.pause", e);
        }
    }
}
