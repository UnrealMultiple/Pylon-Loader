using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Audio;
using Terraria;
using Terraria.Audio;

namespace Pylon.ModLoader;

/// <summary>
/// 音效管线：SoundStyle 发号 + LegacySoundPlayer 的 Mod 标记分派 + 过期流回收。
/// 状态在实例（表与回收计时器），static 只有常量与日志出口。
/// </summary>
internal sealed class SoundPipeline : ILoaderModule
{
    public static SoundPipeline Instance { get; } = new();

    private const int PruneInterval = 10;

    private readonly SoundTable _table = new();
    private int _ticks;

    public string Name => "sound";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Audio.LegacySoundPlayer.PlaySound += PlayMarker;
        On.Terraria.Main.UpdateAudio += UpdateAudio;
        Log.Info("[sound] 已挂（LegacySoundPlayer.PlaySound / 每 10 帧回收过期流）");
        return true;
    }

    internal int Register(SoundStyle style) => _table.Register(style);

    internal int Play(SoundStyle? style, Vector2? position)
    {
        if (style is null) return 0;

        // position 只是接口占位：AndroidAudio 只暴露单音量，没有声像/距离入口（见文档 4.3）。
        return Play(style.Style, 1f, 0f);
    }

    internal int FindActive(SoundStyle? style)
    {
        if (style is null || (uint)style.Style >= (uint)_table.Count) return 0;
        Prune(style.Style);
        ref var entry = ref _table.Entry(style.Style);
        return entry.Active;
    }

    internal int State(int handle)
    {
        if (!_table.TryResolve(handle, out var slot)) return 0;

        // 先记下所属音效：Prune 可能把这一格回收掉，回收后 ref 仍有效但 Entry 会变成 -1。
        var entryIndex = _table.Stream(slot).Entry;
        Prune(entryIndex);
        return _table.TryResolve(handle, out _) ? 2 : 0;
    }

    internal void StopHandle(int handle)
    {
        if (!_table.TryResolve(handle, out var slot)) return;
        Stop(slot);
    }

    private int Play(int index, float volumeScale, float pitchOffset)
    {
        if ((uint)index >= (uint)_table.Count) return 0;

        ref var entry = ref _table.Entry(index);
        if (entry.SoundId <= 0)
        {
            Safe.Once("sound.missing:" + _table.PathOf(index),
                $"sound: '{_table.PathOf(index)}' 没有可播的 SoundPool 条目", true);
            return 0;
        }

        if (!AndroidAudio.SoundAvailable)
        {
            Safe.Once("sound.pool", "sound: SoundPool 不可用，Mod 音效无法播放", true);
            return 0;
        }

        Prune(index);

        if (entry.MaxInstances > 0 && entry.Active >= entry.MaxInstances)
        {
            if (entry.LimitBehavior == 0) return 0;   // IgnoreNew
            Stop(entry.Head);
        }

        var volume = Math.Clamp(entry.Volume * volumeScale, 0f, 1f) * Math.Clamp(Main.soundVolume, 0f, 1f);
        var pitch = entry.Pitch + (float)(Random.Shared.NextDouble() * 2.0 - 1.0) * entry.PitchVariance + pitchOffset;
        var rate = Math.Clamp((float)Math.Pow(2.0, pitch), 0.5f, 2f);

        // SoundPool 没有"解码完成"的查询：play 返回 0 就是还没就绪（同样拒绝，返回 0）。
        var streamId = AndroidAudio.PlaySound(entry.SoundId, volume, rate);
        if (streamId == 0)
        {
            Safe.Once("sound.notready:" + _table.PathOf(index),
                $"sound: '{_table.PathOf(index)}' 还没解码好/被拒，这次先不播", false);
            return 0;
        }

        if (!_table.TryAlloc(index, streamId, AndroidAudio.SoundDuration(entry.SoundId), out var handle))
        {
            try { AndroidAudio.StopSound(streamId); }
            catch (Exception e) { Safe.Report("sound.stop", e); }
            Safe.Once("sound.full", "sound: 活动流到上限，拒绝这次播放", true);
            return 0;
        }

        return handle;
    }

    private void Prune(int index)
    {
        ref var entry = ref _table.Entry(index);
        var slot = entry.Head;
        while (slot >= 0)
        {
            ref var stream = ref _table.Stream(slot);
            var next = stream.Next;
            if (Environment.TickCount64 - stream.StartedMs >= stream.DurationMs) Stop(slot);
            slot = next;
        }
    }

    private void Stop(int slot)
    {
        if (slot < 0) return;

        ref var stream = ref _table.Stream(slot);
        if (!stream.Live) return;

        try { AndroidAudio.StopSound(stream.StreamId); }
        catch (Exception e) { Safe.Report("sound.stop", e); }

        _table.Release(slot);
    }

    private void UpdateAudio(On.Terraria.Main.orig_UpdateAudio orig, Main self)
    {
        orig(self);

        if (_table.Count == 0) return;
        if (++_ticks < PruneInterval) return;
        _ticks = 0;

        try
        {
            for (var i = 0; i < _table.Count; i++) Prune(i);
        }
        catch (Exception e)
        {
            Safe.Report("sound.prune", e);
        }
    }

    private SoundEffectInstance PlayMarker(
        On.Terraria.Audio.LegacySoundPlayer.orig_PlaySound orig, LegacySoundPlayer self,
        int type, int x, int y, int Style, float volumeScale, float pitchOffset)
    {
        if (type != SoundStyle.MarkerSoundId || (uint)Style >= (uint)_table.Count)
            return orig(self, type, x, y, Style, volumeScale, pitchOffset);

        try
        {
            Play(Style, volumeScale, pitchOffset);
        }
        catch (Exception e)
        {
            ContentErrors.Report("[sound] 播放异常：" + Safe.Describe(e));
        }

        // 原版契约要求交回一个实例，但 Mod 音效在 SoundPool 里没有实例；调用方本来就按可空处理。
        return null!;
    }
}
