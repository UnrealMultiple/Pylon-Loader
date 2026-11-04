using Terraria;
using GameTexture2D = Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D;

namespace Pylon.ModLoader;

/// <summary>一个按钮的逐帧状态。按序号密集存放：每帧每按钮一次 ref 写入，公开对象保持只读。</summary>
internal struct ButtonState
{
    public bool Current;
    public bool Old;
    public bool JustPressed;
    public bool JustReleased;
    public bool Latch;
}

/// <summary>
/// 触屏按钮注册表与状态机。按钮数量是几十级，但更新是每帧每按钮，
/// 所以状态按序号密集存放（ref 取用），元数据留在公开的 TouchButtonEntry 上。
/// </summary>
internal sealed class TouchButtonTable
{
    private TouchButtonEntry[] _entries = new TouchButtonEntry[16];
    private ButtonState[] _state = new ButtonState[16];
    private readonly Dictionary<string, int> _byKey = new(StringComparer.Ordinal);

    private int _count;
    private uint _tick = uint.MaxValue;

    internal int Count => _count;

    internal TouchButtonEntry[] All => _entries;

    internal bool IsEmpty => _count == 0;

    /// <summary>注册（按 'mod/Name' 去重）；返回序号。</summary>
    internal int Add(TouchButtonEntry entry)
    {
        if (_byKey.TryGetValue(entry.Key, out var known)) return known;

        if (_count == _entries.Length)
        {
            var grownEntries = new TouchButtonEntry[_entries.Length * 2];
            Array.Copy(_entries, grownEntries, _entries.Length);
            _entries = grownEntries;

            var grownState = new ButtonState[_state.Length * 2];
            Array.Copy(_state, grownState, _state.Length);
            _state = grownState;
        }

        var index = _count;
        _entries[index] = entry;
        _byKey[entry.Key] = index;
        _count++;
        return index;
    }

    internal ref ButtonState State(int index) => ref _state[index];

    internal TouchButtonEntry? ByKey(string? key) =>
        key is not null && _byKey.TryGetValue(key, out var index) ? _entries[index] : null;

    internal TouchButtonEntry? ByIndex(int index) =>
        (uint)index < (uint)_count ? _entries[index] : null;

    /// <summary>
    /// 每帧一次的状态机。门控靠 GameUpdateCount：作者在任意位置读状态都只算一遍。
    /// </summary>
    internal void Tick(Func<TouchButtonEntry, bool> enabled, Func<TouchButtonEntry, bool, bool> pressed)
    {
        var now = Main.GameUpdateCount;
        if (now == _tick) return;
        _tick = now;
        if (_count == 0) return;

        var active = !Main.gameMenu;

        for (var i = 0; i < _count; i++)
        {
            var entry = _entries[i];
            if (entry is null) continue;

            ref var state = ref _state[i];

            bool held;
            try
            {
                held = pressed(entry, false);
            }
            catch (Exception e)
            {
                Safe.Report(entry.Key + ".Pressed", e);
                held = false;
            }

            var current = active && (state.Latch || held) && enabled(entry);

            state.JustReleased = !current && state.Old;
            state.JustPressed = current && !state.Old;
            state.Latch = false;
            state.Old = state.Current;
            state.Current = current;

            entry.Current = current;
            entry.Old = state.Old;
            entry.JustPressed = state.JustPressed;
            entry.JustReleased = state.JustReleased;
        }
    }

    /// <summary>程序触发：只置 latch，让它在同一帧的状态机里和手指按合并。</summary>
    internal bool Latch(int index)
    {
        if ((uint)index >= (uint)_count) return false;

        ref var state = ref _state[index];
        state.Latch = true;
        return true;
    }
}
