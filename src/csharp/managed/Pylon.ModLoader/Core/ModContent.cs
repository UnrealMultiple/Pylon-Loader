using System.Diagnostics.CodeAnalysis;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using ReLogic.Content;

namespace Pylon.ModLoader;

/// <summary>内容注册表与 ID 分配：ID = 原版数量 + 注册序号。</summary>
public static class ModContent
{
    private sealed class KindState
    {
        public int NextType;
        public readonly List<ModType> ById = [];
    }

    private static readonly Dictionary<ContentKind, KindState> States = [];
    private static readonly Dictionary<Type, ModType> ByClass = [];
    private static readonly Dictionary<string, ModType> ByQualifiedName = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Asset<Texture2D>> RequestCache = new(StringComparer.Ordinal);

    /// <summary>注册一个 Mod 的全部内容：先按种类、再按类型全名序数排序，顺序即占号顺序。</summary>
    internal static void Register(Mod mod, List<ModType> types)
    {
        var ordered = new List<ModType>(types);
        ordered.Sort(CompareForRegistration);
        foreach (var type in ordered) RegisterOne(mod, type);
    }

    private static int CompareForRegistration(ModType left, ModType right)
    {
        var byKind = KindOf(left).CompareTo(KindOf(right));
        return byKind != 0 ? byKind : string.CompareOrdinal(left.GetType().FullName, right.GetType().FullName);
    }

    private static void RegisterOne(Mod mod, ModType type)
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
            return;
        }

        type.Type = state.NextType;
        type.Registered = true;

        // 坐骑/前缀在注册期就把游戏的 Count 顶上去：游戏中途重建表时按这个长度给 Mod 留槽。
        switch (type)
        {
            case ModMount:
                Terraria.ID.MountID.Count = Math.Max(Terraria.ID.MountID.Count, type.Type + 1);
                break;
            case ModPrefix prefix:
                Terraria.ID.PrefixID.Count = Math.Max(Terraria.ID.PrefixID.Count, type.Type + 1);
                PrefixLoader.Register(prefix);
                break;
        }

        state.ById.Add(type);
        ByClass[type.GetType()] = type;
        ByQualifiedName[QualifiedName(type)] = type;

        if (kind is ContentKind.Global) return;
        state.NextType++;
        GameContent.Pending++;
    }

    private static string QualifiedName(ModType type) =>
        type.Mod is { } mod ? mod.Name + "/" + type.Name : type.Name;

    internal static ContentKind KindOf(ModType type) => type switch
    {
        ModItem => ContentKind.Item,
        ModTile => ContentKind.Tile,
        ModWall => ContentKind.Wall,
        ModProjectile => ContentKind.Projectile,
        ModNpc => ContentKind.Npc,
        ModBuff => ContentKind.Buff,
        ModMount => ContentKind.Mount,
        ModPrefix => ContentKind.Prefix,
        ModRarity => ContentKind.Rarity,
        _ => ContentKind.Global,
    };

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

    public static int TypeOf<T>() =>
        ByClass.TryGetValue(typeof(T), out var type)
            ? type.Type
            : throw new InvalidOperationException(
                $"{typeof(T).FullName} 没有注册（要派生对应 ModType 且被自动发现）");

    /// <summary>按类取已注册的实例；没注册返回 null。</summary>
    public static T? GetInstance<T>() where T : ModType =>
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
    public static T Find<T>(string fullName) where T : ModType =>
        TryFind<T>(fullName, out var instance)
            ? instance
            : throw new InvalidOperationException($"ModContent.Find: 没有 '{fullName}'");

    /// <summary>按全名（&lt;mod id&gt;/&lt;类名&gt;）找实例。</summary>
    public static bool TryFind<T>(string fullName, [NotNullWhen(true)] out T? instance) where T : ModType
    {
        if (ByQualifiedName.TryGetValue(fullName, out var found) && found is T typed)
        {
            instance = typed;
            return true;
        }
        instance = null;
        return false;
    }

    /// <summary>按路径取已加载的贴图，同一路径只解码一次。只能游戏线程调。</summary>
    public static Asset<Texture2D> Request(Mod mod, string path)
    {
        var full = PylonTexture.Resolve(mod, path);
        if (RequestCache.TryGetValue(full, out var cached)) return cached;

        cached = PylonTexture.LoadAsset(mod, path);
        RequestCache[full] = cached;
        return cached;
    }

    /// <summary>按路径取已加载的游戏贴图。</summary>
    public static Texture2D Texture(Mod mod, string path) => Request(mod, path).Value;

    /// <summary>Mod 目录里有没有这张贴图。</summary>
    public static bool HasAsset(Mod mod, string path) => File.Exists(PylonTexture.Resolve(mod, path));

    /// <summary>建一个 SoundStyle。</summary>
    public static SoundStyle SoundStyle(string path, SoundStyle.Options? options = null) => new(path, options);

    public static ModType? Find(ContentKind kind, int type)
    {
        if (!States.TryGetValue(kind, out var state)) return null;
        var index = type - BaseCount(kind);
        return index >= 0 && index < state.ById.Count ? state.ById[index] : null;
    }

    public static bool IsCustom(ContentKind kind, int type) => Find(kind, type) is not null;

    /// <summary>这个种类注册了多少个（每帧门控用，不要在热路径里用 GetContent 分配）。</summary>
    internal static int Count(ContentKind kind) =>
        States.TryGetValue(kind, out var state) ? state.ById.Count : 0;

    /// <summary>Mods/_data/&lt;id&gt;：与 Mod 目录同级，升级 Mod 不清数据。</summary>
    internal static string DataDirectoryFor(string modDirectory, string id)
    {
        var parent = Path.GetDirectoryName(modDirectory.TrimEnd(Path.DirectorySeparatorChar, '/'));
        return Path.Combine(string.IsNullOrEmpty(parent) ? modDirectory : parent, "_data", id);
    }

    private static int BaseCount(ContentKind kind) => kind switch
    {
        ContentKind.Buff => VanillaCounts.Buff,
        ContentKind.Mount => MountLoader.BaseCount(),
        ContentKind.Prefix => PrefixLoader.BaseCount(),
        ContentKind.Rarity => RarityLoader.VanillaCount,
        ContentKind.Npc => VanillaCounts.Npc,
        ContentKind.Projectile => VanillaCounts.Projectile,
        ContentKind.Item => VanillaCounts.Item,
        ContentKind.Tile => VanillaCounts.Tile,
        ContentKind.Wall => VanillaCounts.Wall,
        _ => 0,
    };
}
