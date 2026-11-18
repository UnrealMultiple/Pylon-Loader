using Il2CppMicrosoft.Xna.Framework.Graphics;
using ReLogic.Content;

namespace Pylon.ModLoader;

/// <summary>Mod 的资源门面：基线的 Mod.Assets；本构建只支持贴图请求。</summary>
public sealed class ModAssets
{
    private readonly Mod _mod;

    internal ModAssets(Mod mod) => _mod = mod;

    public Asset<T> Request<T>(string assetName, AssetRequestMode mode = AssetRequestMode.AsyncLoad) where T : class
    {
        if (typeof(T) != typeof(Texture2D))
            throw new NotSupportedException($"{_mod.Name}: 本构建只能请求 Texture2D，收到 {typeof(T).Name}");

        return (Asset<T>)(object)ContentAssets.Request(_mod, assetName);
    }

    public bool HasAsset<T>(string assetName) =>
        typeof(T) == typeof(Texture2D) && ContentAssets.Has(_mod, assetName);
}
