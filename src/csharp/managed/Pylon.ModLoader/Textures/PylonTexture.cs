using System;
using System.IO;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ReLogic.Content;
using UnityEngine;
using GameTexture2D = Il2CppMicrosoft.Xna.Framework.Graphics.Texture2D;
using UnityTexture2D = UnityEngine.Texture2D;

namespace Pylon.ModLoader.Textures
{
    /// <summary>贴图上传失败（带路径/线程上下文）。</summary>
    public sealed class PylonTextureException : Exception
    {
        public PylonTextureException(string message) : base(message) { }
    }

    /// <summary>PNG/JPG → 游戏贴图。只能游戏线程调：别的线程上建 Unity 对象是 abort 进程。</summary>
    public static class PylonTexture
    {
        [DllImport("pylon")] private static extern int PylonMod_IsGameThread();
        [DllImport("pylon")] private static extern long PylonMod_GameThreadId();
        [DllImport("pylon")] private static extern long PylonMod_CurrentThreadId();

        internal static ModLogger Logger { get; set; }

        /// <summary>当前线程是不是游戏线程；问不到内核时一律当作"不是"。</summary>
        public static bool IsGameThread
        {
            get
            {
                try { return PylonMod_IsGameThread() != 0; }
                catch { return false; }
            }
        }

        public static GameTexture2D LoadTexture(Mod owner, string path) =>
            Decode(File.ReadAllBytes(Resolve(owner, path)), NameOf(owner, path));

        public static Asset<GameTexture2D> LoadAsset(Mod owner, string path)
        {
            var asset = new Asset<GameTexture2D>(NameOf(owner, path));
            asset.SubmitLoadedContent(LoadTexture(owner, path), null);
            return asset;
        }

        /// <summary>字节 → 游戏 Texture2D：① 占位纹理 → ② Unity 解码 → ③ 游戏层包装。</summary>
        public static GameTexture2D Decode(byte[] data, string assetName)
        {
            if (data == null || data.Length == 0) throw new PylonTextureException($"'{assetName}' 的贴图数据为空");
            RequireGameThread($"上传贴图 '{assetName}'");

            // 必须显式构造 IL2CPP 数组：传 C# byte[] 会被隐式转成空数组，表现是"不是能读的 PNG"。
            var bytes = new Il2CppStructArray<byte>(data);
            var unity = new UnityTexture2D(2, 2);   // 占位尺寸，LoadImage 会改成真实尺寸
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

            unity.filterMode = FilterMode.Point;       // 像素画：最近邻，否则糊
            unity.wrapMode = TextureWrapMode.Clamp;    // 越界不绕回对侧
            var texture = new GameTexture2D(unity);
            Logger?.Info($"贴图上传：{assetName} {unity.width}x{unity.height}");
            return texture;
        }

        /// <summary>贴图逻辑尺寸（读 Unity 侧；游戏包装的 Width/Height 不保证已填）。</summary>
        public static void Size(GameTexture2D texture, out int width, out int height)
        {
            if (texture == null) throw new PylonTextureException("贴图为 null");
            width = texture.UnityTextureWidth;
            height = texture.UnityTextureHeight;
        }

        /// <summary>绝对路径原样；相对路径按 Mod 目录解析，且不许跑出该目录。</summary>
        public static string Resolve(Mod owner, string path)
        {
            if (string.IsNullOrEmpty(path)) throw new PylonTextureException("贴图路径为空");
            if (Path.IsPathRooted(path)) return path;
            if (owner == null) throw new PylonTextureException($"相对路径 '{path}' 需要知道是哪个 Mod");

            var root = Path.GetFullPath(owner.Directory);
            var full = Path.GetFullPath(Path.Combine(root, path));
            var prefix = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.Ordinal))
                throw new PylonTextureException($"相对路径不能跑出 Mod 目录：{path}");
            return full;
        }

        private static string NameOf(Mod owner, string path) =>
            $"{(owner == null ? "?" : owner.Name)}/{(path ?? "?").Replace('\\', '/')}";

        // 拒绝是封闭的：只有确认在游戏线程上才放行。
        private static void RequireGameThread(string what)
        {
            if (IsGameThread) return;
            throw new PylonTextureException(
                $"{what} 只能在游戏线程(UnityMain)上做：别的线程上建 Unity 对象会 abort 进程。" +
                $"当前 tid={TryNative(PylonMod_CurrentThreadId)}，游戏线程 tid={TryNative(PylonMod_GameThreadId)}；" +
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
}
