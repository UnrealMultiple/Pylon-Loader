using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Audio;
using Pylon.ModLoader.Audio;
using Pylon.ModLoader.Core;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;
using Terraria;
using Terraria.Audio;
using SoundStyle = Pylon.ModLoader.Audio.SoundStyle;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 音效管线：SoundStyle 发号 + LegacySoundPlayer 的 Mod 标记分派 + 过期流回收。
/// 状态在实例（表与回收计时器），static 只有常量与日志出口。
/// </summary>
internal sealed class SoundPipeline : ILoaderModule
{
    public static SoundPipeline Instance { get; } = new();

    private const int PruneInterval = 10;

    /// <summary>SoundPool 解码未就绪的播放请求：排队重试（上限内），解码好就补播。</summary>
    private const int PendingLimit = 24;
    private const int PendingMaxAge = 45;

    private readonly SoundTable _table = new();
    private readonly List<PendingPlay> _pending = [];
    private int _ticks;

    private sealed class PendingPlay
    {
        public int Index;
        public float VolumeScale;
        public float PitchOffset;
        public int Age;
    }

    private enum PlayReject
    {
        None,
        Missing,
        PoolUnavailable,
        NotReady,
        Full,
    }

    public string Name => "sound";

    private static IModLog Log => LoaderHost.Current.Log;

    /// <summary>sound 管线的安装门：播放与回收钩子在首个自定义音效注册时惰性挂上。</summary>
    internal static class Gates
    {
        internal struct Playback : ILazyGate
        {
            public static bool Wanted() => false;

            public static void Install() => Instance.InstallHooks();
        }
    }

    public bool Install() => GateHub<SoundPipeline>.Run();

    private void InstallHooks()
    {
        On.Terraria.Audio.LegacySoundPlayer.PlaySound += PlayMarker;
        On.Terraria.Main.UpdateAudio += UpdateAudio;
        Log.Info("[sound] 已挂（LegacySoundPlayer.PlaySound / 每 10 帧回收过期流）");
    }

    internal int Register(SoundStyle style)
    {
        var index = _table.Register(style);
        GateHub<SoundPipeline>.Trigger<Gates.Playback>();
        return index;
    }

    internal int Play(SoundStyle? style, Vector2? position)
    {
        if (style is null) return 0;

        // position 只是接口占位：AndroidAudio 只暴露单音量，没有声像/距离入口（见文档 4.3）。
        return Play(style.EnsureRegistered(), 1f, 0f);
    }

    internal int FindActive(SoundStyle? style)
    {
        if (style is null) return 0;
        var index = style.EnsureRegistered();
        if ((uint)index >= (uint)_table.Count) return 0;
        Prune(index);
        ref var entry = ref _table.Entry(index);
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
        var handle = TryPlay(index, volumeScale, pitchOffset, out var reject);
        if (handle > 0) return handle;

        // SoundPool 解码是异步的：首播常常还没就绪。排队重试，就绪后补播，
        // 否则"第一次放技能没声音"（音效有时有有时没有的主因）。
        if (reject == PlayReject.NotReady)
        {
            if (_pending.Count < PendingLimit)
                _pending.Add(new PendingPlay { Index = index, VolumeScale = volumeScale, PitchOffset = pitchOffset });
        }
        return 0;
    }

    private int TryPlay(int index, float volumeScale, float pitchOffset, out PlayReject reject)
    {
        reject = PlayReject.None;
        if ((uint)index >= (uint)_table.Count)
        {
            reject = PlayReject.Missing;
            return 0;
        }

        ref var entry = ref _table.Entry(index);
        var ids = entry.SoundIds;
        if (ids.Length == 0)
        {
            Safe.Once("sound.missing:" + _table.PathOf(index),
                $"sound: '{_table.PathOf(index)}' 没有可播的 SoundPool 条目", true);
            reject = PlayReject.Missing;
            return 0;
        }

        if (!AndroidAudio.SoundAvailable)
        {
            Safe.Once("sound.pool", "sound: SoundPool 不可用，Mod 音效无法播放", true);
            reject = PlayReject.PoolUnavailable;
            return 0;
        }

        Prune(index);

        if (entry.MaxInstances > 0 && entry.Active >= entry.MaxInstances)
        {
            if (entry.LimitBehavior == 0)
            {
                reject = PlayReject.Full;   // IgnoreNew
                return 0;
            }
            Stop(entry.Head);
        }

        var volume = Math.Clamp(entry.Volume * volumeScale, 0f, 1f) * Math.Clamp(Main.soundVolume, 0f, 1f);
        var pitch = entry.Pitch + ((float)Random.Shared.NextDouble() - 0.5f) * entry.PitchVariance + pitchOffset;
        var rate = Math.Clamp((float)Math.Pow(2.0, pitch), 0.5f, 2f);

        var soundId = ids.Length == 1 ? ids[0] : ids[Random.Shared.Next(ids.Length)];

        // SoundPool 没有"解码完成"的查询：play 返回 0 就是还没就绪（同样拒绝，返回 0）。
        var streamId = AndroidAudio.PlaySound(soundId, volume, rate);
        if (streamId == 0)
        {
            Safe.Once("sound.notready:" + _table.PathOf(index),
                $"sound: '{_table.PathOf(index)}' 还没解码好/被拒，这次先不播", false);
            reject = PlayReject.NotReady;
            return 0;
        }

        if (!_table.TryAlloc(index, streamId, AndroidAudio.SoundDuration(soundId), out var handle))
        {
            try { AndroidAudio.StopSound(streamId); }
            catch (Exception e) { Safe.Report("sound.stop", e); }
            Safe.Once("sound.full", "sound: 活动流到上限，拒绝这次播放", true);
            reject = PlayReject.Full;
            return 0;
        }

        return handle;
    }

    /// <summary>每 tick 清一次待播队列：就绪即补播，超时（约 0.75s）放弃。</summary>
    private void DrainPending()
    {
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            var item = _pending[i];
            item.Age++;
            var handle = TryPlay(item.Index, item.VolumeScale, item.PitchOffset, out var reject);
            if (handle > 0 || reject != PlayReject.NotReady || item.Age >= PendingMaxAge)
                _pending.RemoveAt(i);
        }
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

        if (_pending.Count > 0) DrainPending();

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
            ContentErrors.Line("[sound] 播放异常：" + Safe.Dump(e));
        }

        // 原版契约要求交回一个实例，但 Mod 音效在 SoundPool 里没有实例；调用方本来就按可空处理。
        return null!;
    }
}
