using System.Numerics;
using System.Runtime.CompilerServices;

namespace Pylon.ModLoader;

/// <summary>覆写位集保持纯值类型，分派只做下标与位运算，不为扩容引入热路径分配。</summary>
internal readonly struct HookMask
{
    private const int WordBits = 64;
    public const int Capacity = WordBits * 8;
    private readonly ulong _w0, _w1, _w2, _w3, _w4, _w5, _w6, _w7;

    internal HookMask(ulong w0, ulong w1, ulong w2, ulong w3,
                      ulong w4 = 0, ulong w5 = 0, ulong w6 = 0, ulong w7 = 0)
    {
        _w0 = w0; _w1 = w1; _w2 = w2; _w3 = w3;
        _w4 = w4; _w5 = w5; _w6 = w6; _w7 = w7;
    }

    public static HookMask Empty => default;
    public bool IsEmpty => (_w0 | _w1 | _w2 | _w3 | _w4 | _w5 | _w6 | _w7) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(HookId id)
    {
        var index = (int)id;
        if ((uint)index >= Capacity) return false;
        var bit = 1UL << (index & (WordBits - 1));
        return (index >> 6) switch
        {
            0 => (_w0 & bit) != 0,
            1 => (_w1 & bit) != 0,
            2 => (_w2 & bit) != 0,
            3 => (_w3 & bit) != 0,
            4 => (_w4 & bit) != 0,
            5 => (_w5 & bit) != 0,
            6 => (_w6 & bit) != 0,
            7 => (_w7 & bit) != 0,
            _ => false,
        };
    }

    /// <summary>只在构建订阅表时枚举置位项，缓冲不足则停止写入。</summary>
    public int Collect(Span<HookId> buffer)
    {
        var written = Write(_w0, 0, buffer, 0);
        written = Write(_w1, WordBits, buffer, written);
        written = Write(_w2, WordBits * 2, buffer, written);
        written = Write(_w3, WordBits * 3, buffer, written);
        written = Write(_w4, WordBits * 4, buffer, written);
        written = Write(_w5, WordBits * 5, buffer, written);
        written = Write(_w6, WordBits * 6, buffer, written);
        return Write(_w7, WordBits * 7, buffer, written);
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

    internal struct Builder
    {
        private ulong _w0, _w1, _w2, _w3, _w4, _w5, _w6, _w7;

        public void Add(HookId id)
        {
            var index = (int)id;
            // 未知编号不能折叠到最后一个字中，否则会意外开启无关回调。
            if ((uint)index >= Capacity) throw new ArgumentOutOfRangeException(nameof(id));
            var bit = 1UL << (index & (WordBits - 1));
            switch (index >> 6)
            {
                case 0: _w0 |= bit; break;
                case 1: _w1 |= bit; break;
                case 2: _w2 |= bit; break;
                case 3: _w3 |= bit; break;
                case 4: _w4 |= bit; break;
                case 5: _w5 |= bit; break;
                case 6: _w6 |= bit; break;
                case 7: _w7 |= bit; break;
            }
        }

        public HookMask Build() => new(_w0, _w1, _w2, _w3, _w4, _w5, _w6, _w7);
    }
}
