using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using ReLogic.Content;

namespace Pylon.ModLoader;

/// <summary>贴图请求缓存：同一个绝对路径只解码一次。仅游戏线程。</summary>
internal static class ContentAssets
{
    private static readonly Dictionary<string, Asset<Texture2D>> Cache = new(StringComparer.Ordinal);

    public static Asset<Texture2D> Request(Mod mod, string path)
    {
        var full = PylonTexture.Resolve(mod, path);
        if (Cache.TryGetValue(full, out var cached)) return cached;

        cached = PylonTexture.LoadAsset(mod, path);
        Cache[full] = cached;
        return cached;
    }

    public static bool Has(Mod mod, string path) => File.Exists(PylonTexture.Resolve(mod, path));
}
