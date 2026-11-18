namespace Pylon.ModLoader;

/// <summary>发型总数与 DrawBackHair 表的门面：游戏本体没有 HairID，mod 发型默认不画后发，作者按类型号写 true。</summary>
public static class HairIdTml
{
    public const int Count = 228;

    public static class Sets
    {
        public static bool[] DrawBackHair = Vanilla();

        private static bool[] Vanilla()
        {
            var set = new bool[Count];
            for (var hair = 0; hair < Count; hair++)
            {
                set[hair] = hair > 50 && (hair < 56 || hair > 63) && (hair < 74 || hair > 77) && (hair < 88 || hair > 89) &&
                            hair != 94 && hair != 100 && hair != 104 && hair != 112 && hair < 116;
                if (hair is 133 or 134 or 146 or 162 or 6) set[hair] = true;
            }
            return set;
        }

        internal static void Resize(int count)
        {
            if (DrawBackHair.Length < count) Array.Resize(ref DrawBackHair, count);
        }
    }
}
