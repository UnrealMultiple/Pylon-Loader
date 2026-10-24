using System;
using System.Collections.Generic;
using System.IO;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using ReLogic.Content;

namespace Pylon.ModLoader
{
    /// <summary>内容种类；顺序决定占号次序（跨设备必须一致）。</summary>
    public enum ContentKind
    {
        Buff = 0, Player, Mount, Prefix, Rarity, Npc, Projectile, Item, Tile, Wall, Global,
    }

    /// <summary>原版数量 = 自定义 ID 起点（游戏里是编译期常量，只能写死；Terraria 1.4.5.8.6）。</summary>
    internal static class VanillaCounts
    {
        public const int Buff = 401;
        public const int Npc = 697;
        public const int Projectile = 1136;
        public const int Item = 6196;
        public const int Tile = 754;
        public const int Wall = 367;
        // 坐骑没有编译期常量：MountID.Count 在运行时读（MountLoader.BaseCount 捕获首次值）。
    }

    /// <summary>注册表与 ID 分配：ID = 原版数量 + 注册序号。</summary>
    public static class ModContent
    {
        private sealed class KindState
        {
            public int NextType;
            public readonly List<ModType> ById = new();
        }

        private static readonly Dictionary<ContentKind, KindState> States = new();
        private static readonly Dictionary<Type, ModType> ByClass = new();

        internal static ContentKind KindOf(ModType type)
        {
            switch (type)
            {
                case ModItem _: return ContentKind.Item;
                case ModTile _: return ContentKind.Tile;
                case ModWall _: return ContentKind.Wall;
                case ModProjectile _: return ContentKind.Projectile;
                case ModNpc _: return ContentKind.Npc;
                case ModBuff _: return ContentKind.Buff;
                case ModMount _: return ContentKind.Mount;
                case ModPrefix _: return ContentKind.Prefix;
                case ModRarity _: return ContentKind.Rarity;
                default: return ContentKind.Global;
            }
        }

        /// <summary>占号：先按种类，再按类型全名序数。</summary>
        internal static void Register(Mod mod, List<ModType> types)
        {
            var ordered = new List<ModType>(types);
            ordered.Sort((a, b) =>
            {
                var byKind = KindOf(a).CompareTo(KindOf(b));
                return byKind != 0 ? byKind : string.CompareOrdinal(a.GetType().FullName, b.GetType().FullName);
            });

            foreach (var type in ordered)
            {
                var kind = KindOf(type);
                if (!States.TryGetValue(kind, out var state))
                {
                    state = new KindState { NextType = BaseCount(kind) };
                    States[kind] = state;
                }

                if (state.NextType >= short.MaxValue)
                {
                    mod.Logger.Error($"{type.GetType().FullName} 拿不到 ID（{kind} 已到 {state.NextType}）");
                    continue;
                }

                type.Type = state.NextType;
                type.Registered = true;
                // 坐骑/前缀在 register 时就把游戏的 Count 顶上去（不等 setup），游戏中途重建表时才按此长度给 mod 留槽。
                if (kind == ContentKind.Mount)
                    Terraria.ID.MountID.Count = Math.Max(Terraria.ID.MountID.Count, type.Type + 1);
                if (kind == ContentKind.Prefix)
                    Terraria.ID.PrefixID.Count = Math.Max(Terraria.ID.PrefixID.Count, type.Type + 1);
                // 前缀的分类表与名字在注册期建好（PrefixLoader.Register）：否则前缀进不了任何分类、永远掷不出来。
                if (kind == ContentKind.Prefix && type is ModPrefix prefix)
                    PrefixLoader.Register(prefix);
                state.ById.Add(type);
                ByClass[type.GetType()] = type;
                if (kind != ContentKind.Global)
                {
                    state.NextType++;
                    GameContent.Pending++;
                }
            }
        }

        /// <summary>该种类的下一个可用 ID（内容注入时用来算表长）。</summary>
        public static int NextType(ContentKind kind) =>
            States.TryGetValue(kind, out var state) ? state.NextType : BaseCount(kind);

        public static int ItemType<T>() where T : ModItem => TypeOf<T>();
        public static int TileType<T>() where T : ModTile => TypeOf<T>();
        public static int WallType<T>() where T : ModWall => TypeOf<T>();
        public static int ProjectileType<T>() where T : ModProjectile => TypeOf<T>();
        public static int NpcType<T>() where T : ModNpc => TypeOf<T>();
        public static int BuffType<T>() where T : ModBuff => TypeOf<T>();
        public static int MountType<T>() where T : ModMount => TypeOf<T>();
        public static int PrefixType<T>() where T : ModPrefix => TypeOf<T>();
        public static int RarityType<T>() where T : ModRarity => TypeOf<T>();
        // 前缀/坐骑/稀有度等种类：我们还没有它们的 ID 空间（占号管线没建），等内容安装落地再补 PrefixType<T>() 等。

        public static int TypeOf<T>()
        {
            if (ByClass.TryGetValue(typeof(T), out var type)) return type.Type;
            throw new InvalidOperationException($"{typeof(T).FullName} 没有注册（要派生对应 ModType 且被自动发现）");
        }

        /// <summary>按类取已注册的实例（没注册会拿到 null）。</summary>
        public static T GetInstance<T>() where T : ModType =>
            ByClass.TryGetValue(typeof(T), out var instance) ? (T)instance : null;

        /// <summary>全部已注册、且可赋给 T 的实例。</summary>
        public static List<T> GetContent<T>() where T : ModType
        {
            var result = new List<T>();
            foreach (var instance in ByClass.Values)
            {
                if (instance is T typed) result.Add(typed);
            }
            return result;
        }

        /// <summary>按全名（&lt;mod id&gt;/&lt;类名&gt;）找实例；找不到抛错。</summary>
        public static T Find<T>(string fullName) where T : ModType
        {
            var found = TryFind<T>(fullName, out var instance);
            if (!found) throw new InvalidOperationException($"ModContent.Find: 没有 '{fullName}'");
            return instance;
        }

        /// <summary>按全名（&lt;mod id&gt;/&lt;类名&gt;）找实例；找不到返回 false。</summary>
        public static bool TryFind<T>(string fullName, out T instance) where T : ModType
        {
            instance = null;
            foreach (var pair in ByClass)
            {
                if (pair.Value.Mod != null && pair.Value.Mod.Name + "/" + pair.Value.Name == fullName)
                {
                    instance = (T)pair.Value;
                    return true;
                }
            }
            return false;
        }

        /// <summary>按路径取已加载的 Asset&lt;Texture2D&gt;（tModLoader 式按名取，同一路径只解码一次并缓存）。只能游戏线程调。</summary>
        public static Asset<Texture2D> Request(Mod mod, string path)
        {
            var full = PylonTexture.Resolve(mod, path);
            if (!RequestCache.TryGetValue(full, out var cached))
            {
                cached = PylonTexture.LoadAsset(mod, path);
                RequestCache[full] = cached;
            }
            return cached;
        }

        /// <summary>按路径取已加载的游戏 Texture2D（ModContent.Request 的 Value）。</summary>
        public static Texture2D Texture(Mod mod, string path) => Request(mod, path).Value;

        /// <summary>Mod 目录里有没有这张贴图。</summary>
        public static bool HasAsset(Mod mod, string path) =>
            File.Exists(PylonTexture.Resolve(mod, path));

        /// <summary>建一个 SoundStyle（ModContent.SoundStyle(path, options) 的等价物）。</summary>
        public static SoundStyle SoundStyle(string path, SoundStyle.Options options = null) =>
            new SoundStyle(path, options);

        private static readonly Dictionary<string, Asset<Texture2D>> RequestCache = new(StringComparer.Ordinal);

        public static ModType Find(ContentKind kind, int type)
        {
            if (!States.TryGetValue(kind, out var state)) return null;
            var index = type - BaseCount(kind);
            return index >= 0 && index < state.ById.Count ? state.ById[index] : null;
        }

        public static bool IsCustom(ContentKind kind, int type) => Find(kind, type) != null;

        /// <summary>这个种类注册了多少个（每帧门控用；不要拿 GetContent 在热路径里分配）。</summary>
        internal static int Count(ContentKind kind) =>
            States.TryGetValue(kind, out var state) ? state.ById.Count : 0;

        /// <summary>Mods/_data/&lt;id&gt;：与 Mod 目录同级，升级 Mod 不清数据。</summary>
        internal static string DataDirectoryFor(string modDirectory, string id)
        {
            var parent = Path.GetDirectoryName(modDirectory.TrimEnd(Path.DirectorySeparatorChar, '/'));
            return Path.Combine(string.IsNullOrEmpty(parent) ? modDirectory : parent, "_data", id);
        }

        private static int BaseCount(ContentKind kind)
        {
            switch (kind)
            {
                case ContentKind.Buff: return VanillaCounts.Buff;
                case ContentKind.Mount: return MountLoader.BaseCount();
                case ContentKind.Prefix: return PrefixLoader.BaseCount();
                case ContentKind.Rarity: return RarityLoader.VanillaCount;
                case ContentKind.Npc: return VanillaCounts.Npc;
                case ContentKind.Projectile: return VanillaCounts.Projectile;
                case ContentKind.Item: return VanillaCounts.Item;
                case ContentKind.Tile: return VanillaCounts.Tile;
                case ContentKind.Wall: return VanillaCounts.Wall;
                default: return 0;   // System/Global 不占游戏 ID
            }
        }
    }
}