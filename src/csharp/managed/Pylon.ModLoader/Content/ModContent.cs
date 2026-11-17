using System.Diagnostics.CodeAnalysis;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using ReLogic.Content;

namespace Pylon.ModLoader;

/// <summary>内容门面：Mod 侧唯一入口。所有状态都在 <see cref="LoaderHost"/>，这里只做转发。</summary>
public static class ModContent
{
    private static ContentRegistry Registry => LoaderHost.Current.Content;

    public static int ItemType<T>() where T : ModItem => TypeOf<T>();
    public static int TileType<T>() where T : ModTile => TypeOf<T>();
    public static int WallType<T>() where T : ModWall => TypeOf<T>();
    public static int ProjectileType<T>() where T : ModProjectile => TypeOf<T>();
    public static int NpcType<T>() where T : ModNpc => TypeOf<T>();
    public static int BuffType<T>() where T : ModBuff => TypeOf<T>();
    public static int MountType<T>() where T : ModMount => TypeOf<T>();
    public static int PrefixType<T>() where T : ModPrefix => TypeOf<T>();
    public static int RarityType<T>() where T : ModRarity => TypeOf<T>();
    public static int DustType<T>() where T : ModDust => GetInstance<T>()?.Type ?? 0;
    public static int EmoteBubbleType<T>() where T : ModEmoteBubble => GetInstance<T>()?.Type ?? 0;
    public static int GoreType<T>() where T : ModGore => GetInstance<T>()?.Type ?? 0;

    /// <summary>按类型号取 mod 物品；原版类型返回 null。</summary>
    public static ModItem? GetModItem(int type) => Find(ContentKind.Item, type) as ModItem;

    /// <summary>按类型号取 mod NPC；原版类型返回 null。</summary>
    public static ModNpc? GetModNPC(int type) => Find(ContentKind.Npc, type) as ModNpc;

    /// <summary>按类型号取 mod 方块；原版类型返回 null。</summary>
    public static ModTile? GetModTile(int type) => Find(ContentKind.Tile, type) as ModTile;

    /// <summary>按类型号取 mod buff；原版 buff 返回 null。</summary>
    public static ModBuff? GetModBuff(int type) => Find(ContentKind.Buff, type) as ModBuff;

    /// <summary>按类型号取 mod 尘土；没注册返回 null。</summary>
    public static ModDust? GetModDust(int type) => DustTable.Of(type);

    /// <summary>按类型号取 mod 表情；没注册返回 null。</summary>
    public static ModEmoteBubble? GetModEmoteBubble(int type) => EmoteBubbleTable.Of(type);

    /// <summary>按类型号取 mod 血腥粒子；没注册返回 null。</summary>
    public static ModGore? GetModGore(int type) => GoreTable.Of(type);

    /// <summary>按贴图路径取 mod NPC 的 Boss 头像槽；没有返回 -1（路径可带 Mod 名或 .png）。</summary>
    public static int GetModBossHeadSlot(string texture)
    {
        if (string.IsNullOrEmpty(texture)) return -1;

        var wanted = texture.Replace('\\', '/');
        if (wanted.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) wanted = wanted[..^4];

        foreach (var npc in GetContent<ModNpc>())
        {
            if (!npc.Registered || npc.Type <= 0) continue;

            var path = !string.IsNullOrEmpty(npc.BossHeadTexture) ? npc.BossHeadTexture : npc.TextureSibling("_Head_Boss");
            if (string.IsNullOrEmpty(path)) continue;

            var normalized = path.Replace('\\', '/');
            if (normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) normalized = normalized[..^4];

            var full = npc.Mod is null ? normalized : npc.Mod.Name + "/" + normalized;
            if (string.Equals(wanted, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(wanted, full, StringComparison.OrdinalIgnoreCase) ||
                wanted.EndsWith("/" + normalized, StringComparison.OrdinalIgnoreCase))
                return NpcHeadHooks.BossSlot(npc.Type);
        }

        return -1;
    }

    public static int TypeOf<T>() where T : ModType =>
        Registry.InstanceOf<T>() is { } instance
            ? instance.Type
            : throw new InvalidOperationException(
                $"{typeof(T).FullName} 没有注册（要派生对应 ModType 且被自动发现）");

    /// <summary>按类取已注册的实例；没注册返回 null。</summary>
    public static T? GetInstance<T>() where T : ModType => Registry.InstanceOf<T>();

    /// <summary>全部已注册、且可赋给 T 的实例。</summary>
    public static List<T> GetContent<T>() where T : ModType => Registry.ContentOf<T>();

    public static T Find<T>(string fullName) where T : ModType =>
        TryFind<T>(fullName, out var instance)
            ? instance
            : throw new InvalidOperationException($"ModContent.Find: 没有 '{fullName}'");

    public static bool TryFind<T>(string fullName, [NotNullWhen(true)] out T? instance) where T : ModType =>
        Registry.TryFind(fullName, out instance);

    public static ModType? Find(ContentKind kind, int type) => Registry.ById(kind, type);

    public static bool IsCustom(ContentKind kind, int type) => Registry.ById(kind, type) is not null;

    /// <summary>该种类的下一个可用 ID（扩表时用来算表长）。</summary>
    internal static int NextType(ContentKind kind) => Registry.NextType(kind);

    internal static int Count(ContentKind kind) => Registry.RegisteredCount(kind);

    // ===================== 资源 =====================

    /// <summary>按路径取已加载的贴图，同一路径只解码一次。只能游戏线程调。</summary>
    public static Asset<Texture2D> Request(Mod mod, string path) => ContentAssets.Request(mod, path);

    public static Texture2D Texture(Mod mod, string path) => ContentAssets.Request(mod, path).Value;

    public static bool HasAsset(Mod mod, string path) => ContentAssets.Has(mod, path);

    public static SoundStyle SoundStyle(string path, SoundStyle.Options? options = null) => new(path, options);

    /// <summary>Mods/_data/&lt;id&gt;：与 Mod 目录同级，升级 Mod 不清数据。</summary>
    internal static string DataDirectoryFor(string modDirectory, string id)
    {
        var parent = Path.GetDirectoryName(modDirectory.TrimEnd(Path.DirectorySeparatorChar, '/'));
        return Path.Combine(string.IsNullOrEmpty(parent) ? modDirectory : parent, "_data", id);
    }
}
