using System.Numerics;
using System.Runtime.CompilerServices;

namespace Pylon.ModLoader;

/// <summary>一个类覆写了哪些钩子。纯值类型位集：判定是位运算，热路径无字符串、无分配。</summary>
internal readonly struct HookMask
{
    private const int WordBits = 64;

    /// <summary>位集能表示的钩子上限（4 × 64）。</summary>
    public const int Capacity = WordBits * 4;
    private readonly ulong _w0;
    private readonly ulong _w1;
    private readonly ulong _w2;
    private readonly ulong _w3;

    internal HookMask(ulong w0, ulong w1, ulong w2, ulong w3)
    {
        _w0 = w0;
        _w1 = w1;
        _w2 = w2;
        _w3 = w3;
    }

    public static HookMask Empty => default;

    public bool IsEmpty => (_w0 | _w1 | _w2 | _w3) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(HookId id)
    {
        var index = (int)id;
        var bit = 1UL << (index & (WordBits - 1));
        return (index >> 6) switch
        {
            0 => (_w0 & bit) != 0,
            1 => (_w1 & bit) != 0,
            2 => (_w2 & bit) != 0,
            _ => (_w3 & bit) != 0,
        };
    }

    /// <summary>把置位的钩子写进缓冲，返回写入个数（构建订阅表时用，不在分派路径上）。</summary>
    public int Collect(Span<HookId> buffer)
    {
        var written = 0;
        written = Write(_w0, 0, buffer, written);
        written = Write(_w1, WordBits, buffer, written);
        written = Write(_w2, WordBits * 2, buffer, written);
        return Write(_w3, WordBits * 3, buffer, written);
    }

    private static int Write(ulong word, int offset, Span<HookId> buffer, int written)
    {
        while (word != 0 && written < buffer.Length)
        {
            buffer[written++] = (HookId)(offset + BitOperations.TrailingZeroCount(word));
            word &= word - 1;
        }
        return written;
    }

    /// <summary>构建期的可变累加器。</summary>
    internal struct Builder
    {
        private ulong _w0;
        private ulong _w1;
        private ulong _w2;
        private ulong _w3;

        public void Add(HookId id)
        {
            var index = (int)id;
            var bit = 1UL << (index & (WordBits - 1));
            switch (index >> 6)
            {
                case 0: _w0 |= bit; break;
                case 1: _w1 |= bit; break;
                case 2: _w2 |= bit; break;
                default: _w3 |= bit; break;
            }
        }

        public HookMask Build() => new(_w0, _w1, _w2, _w3);
    }
}
