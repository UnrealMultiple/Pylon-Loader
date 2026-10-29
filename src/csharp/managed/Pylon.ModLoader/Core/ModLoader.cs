using System;
using System.Collections.Generic;

namespace Pylon.ModLoader
{
    /// <summary>跨 Mod 查找（照参考的 ModLoader）：按 id 拿别的 Mod 实例，做可选依赖用 TryGetMod/HasMod。</summary>
    public static class ModLoader
    {
        // 参考的 ModRegistry；加载时登记，顺序即加载顺序。
        private static readonly Dictionary<string, Mod> Registry = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Mod> Ordered = new();

        /// <summary>Mod 加载时由管线登记（Mod.IPylonMod.Load 里调）。</summary>
        internal static void Register(Mod mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.Name)) return;
            if (Registry.ContainsKey(mod.Name)) return;

            Registry[mod.Name] = mod;
            Ordered.Add(mod);
        }

        /// <summary>按 id 找 Mod；找到就写进 result 并返回 true，没找到返回 false（result = null）。</summary>
        public static bool TryGetMod(string name, out Mod result)
        {
            result = Find(name);
            return result != null;
        }

        /// <summary>按 id 找 Mod；没有就抛（可选依赖请用 TryGetMod/HasMod）。</summary>
        public static Mod GetMod(string name) =>
            Find(name) ?? throw new InvalidOperationException(
                $"ModLoader.GetMod: 没有加载 '{name}' 这个 Mod（可选依赖请用 TryGetMod）");

        /// <summary>这个 id 的 Mod 在不在。</summary>
        public static bool HasMod(string name) => Find(name) != null;

        /// <summary>全部已加载的 Mod，按加载顺序。</summary>
        public static IReadOnlyList<Mod> Mods => Ordered;

        private static Mod Find(string name) =>
            !string.IsNullOrEmpty(name) && Registry.TryGetValue(name, out var mod) ? mod : null;
    }
}
