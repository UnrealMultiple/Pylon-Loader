using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.ModLoader.Logging;
using Pylon.NaviteAPI.Host;
using ReLogic.Content;
using UnityEngine;
using GameTexture2D = Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D;
using UnityTexture2D = UnityEngine.Texture2D;

namespace Pylon.ModLoader.Textures;

public static class PylonTexture
{
    internal static ModLogger? Logger { get; set; }

    public static bool IsGameThread
    {
        get
        {
            try { return ThreadNative.IsGameThread() != 0; }
            catch { return false; }
        }
    }

    public static GameTexture2D LoadTexture(Mod? owner, string path) =>
        Decode(File.ReadAllBytes(Resolve(owner, path)), NameOf(owner, path));

    public static Asset<GameTexture2D> LoadAsset(Mod? owner, string path)
    {
        var asset = new Asset<GameTexture2D>(NameOf(owner, path));
        asset.SubmitLoadedContent(LoadTexture(owner, path), null);
        return asset;
    }

    public static GameTexture2D Decode(byte[]? data, string assetName)
    {
        if (data is null || data.Length == 0) throw new PylonTextureException($"'{assetName}' 的贴图数据为空");
        RequireGameThread($"上传贴图 '{assetName}'");

        var bytes = new Il2CppStructArray<byte>(data);
        var unity = new UnityTexture2D(2, 2);
        bool decoded;
        try
        {
            decoded = ImageConversion.LoadImage(unity, bytes, false);
        }
        catch (Exception e)
        {
            DestroyUnity(unity);
            throw new PylonTextureException($"LoadImage 抛异常（{assetName}）：{e.Message}");
        }
        if (!decoded)
        {
            DestroyUnity(unity);
            throw new PylonTextureException($"'{assetName}' 不是 Unity 能解码的 PNG/JPG");
        }

        unity.filterMode = FilterMode.Point;
        unity.wrapMode = TextureWrapMode.Clamp;
        var texture = new GameTexture2D(unity);

        Logger?.Info($"贴图上传：{assetName} {unity.width}x{unity.height}");
        return texture;
    }

    public static void Size(GameTexture2D? texture, out int width, out int height)
    {
        if (texture is null) throw new PylonTextureException("贴图为 null");
        width = texture.UnityTextureWidth;
        height = texture.UnityTextureHeight;
    }

    public static Action<string, string>? ContentResolver { get; set; }

    public static string Resolve(Mod? owner, string path)
    {
        if (string.IsNullOrEmpty(path)) throw new PylonTextureException("贴图路径为空");
        if (Path.IsPathRooted(path)) return path;
        if (owner is null) throw new PylonTextureException($"相对路径 '{path}' 需要知道是哪个 Mod");

        var root = Path.GetFullPath(owner.Directory);
        var full = Path.GetFullPath(Path.Combine(root, path));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
            throw new PylonTextureException($"相对路径不能跑出 Mod 目录：{path}");
        if (!File.Exists(full)) ContentResolver?.Invoke(root, Path.GetRelativePath(root, full));
        return full;
    }

    private static string NameOf(Mod? owner, string? path) =>
        $"{(owner is null ? "?" : owner.Name)}/{(path ?? "?").Replace('\\', '/')}";

    private static void RequireGameThread(string what)
    {
        if (IsGameThread) return;
        throw new PylonTextureException(
            $"{what} 只能在游戏线程(UnityMain)上做：别的线程上建 Unity 对象会 abort 进程。" +
            $"当前 tid={TryNative(ThreadNative.CurrentThreadId)}，游戏线程 tid={TryNative(ThreadNative.GameThreadId)}；" +
            "贴图请放到 SetupContent 或 hook 里加载。");
    }

    private static void DestroyUnity(UnityTexture2D texture)
    {
        try { UnityEngine.Object.Destroy(texture); }
        catch (Exception e) { Logger?.Warn($"销毁解码失败的纹理失败：{e.Message}"); }
    }

    private static long TryNative(Func<long> call)
    {
        try { return call(); } catch { return 0; }
    }
}
