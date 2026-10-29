using System;
using System.Collections.Generic;
using Terraria;

namespace Pylon.ModLoader
{
    /// <summary>自然生成抽选（EditSpawnPool 的池子）：NPC 类型 -> 权重；0 是游戏自己的生成（权重 1），mod 的经 SpawnChance 进来。</summary>
    public class SpawnPool
    {
        /// <summary>条目本体：类型 -> 权重。</summary>
        private readonly Dictionary<int, float> _entries = new();

        /// <summary>抽选里有几条（Object.keys 的长度）。</summary>
        public int Count => _entries.Count;

        /// <summary>抽选里的类型；每次访问新建一份（Object.keys 的结果）。</summary>
        public List<int> Keys
        {
            get
            {
                var keys = new List<int>(_entries.Count);
                foreach (var type in _entries.Keys) keys.Add(type);
                return keys;
            }
        }

        /// <summary>加一条：类型已在抽选里就抛异常（参考抛的是 Error）。</summary>
        public void Add(int type, float weight)
        {
            if (_entries.ContainsKey(type)) throw new ArgumentException($"SpawnPool.Add: 类型 {type} 已经在抽选里");
            _entries[type] = weight;
        }

        /// <summary>类型在不在抽选里（hasOwnProperty）。</summary>
        public bool ContainsKey(int type) => _entries.ContainsKey(type);

        /// <summary>删掉一条；返回删没删到（本来就没有也返回 false）。</summary>
        public bool Remove(int type) => _entries.Remove(type);

        /// <summary>清空全部条目。</summary>
        public void Clear() => _entries.Clear();

        /// <summary>用 Main.rand 抽一个类型；总权重 0 返回 null（什么都不生成），负权重按 0 算。</summary>
        public int? Choose()
        {
            double total = 0;
            var entries = new List<KeyValuePair<int, float>>();
            foreach (var entry in _entries)
            {
                // 参考 Math.max(0, w)：负权重（和 NaN）按 0 算。
                var weight = entry.Value > 0 ? entry.Value : 0f;
                entries.Add(new KeyValuePair<int, float>(entry.Key, weight));
                total += weight;
            }
            var r = Utils.NextFloat(Main.rand) * total;
            foreach (var entry in entries)
            {
                if (r < entry.Value) return entry.Key;
                r -= entry.Value;
            }
            return null;
        }

        // 故意没翻：JS 的 pool[type] 下标语法不是成员，C# 用 Add/Remove/ContainsKey 表达。
    }
}