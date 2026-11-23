using System.Text;
using Pylon.ModLoader.Logging;
using Pylon.ModLoader.Pipeline;

namespace Pylon.ModLoader.Pipelines;
/// <summary>
/// 内容发号指纹：注册顺序决定 ID，两端 mod 集合或顺序不同就会错位。
/// 指纹不参与游戏逻辑，只在启动与联机时给出一条"两端不一致"的证据。
/// 纹理指纹（RefreshTextures）另算一份：折进每个已加载 mod 目录下的图片文件（路径 + 字节），
/// 不含代码与清单，专门承载"材质是否一致"这条独立维度；进程内只算一次，联机收发两端各算各的。
/// </summary>
internal static class ContentFingerprint
{
    private const ulong Basis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];
    private static readonly object TextureLock = new();

    private static ulong _value;
    private static string _text = "0";
    private static bool _textureReady;
    private static ulong _textureValue;
    private static string _textureText = "0";

    /// <summary>注册期最后一次算出的指纹（十六进制）。</summary>
    internal static string Text => _text;

    internal static ulong Value => _value;

    /// <summary>纹理指纹：联机首次用到时才计算，之后进程内缓存。</summary>
    internal static ulong TextureValue => _textureValue;

    internal static string TextureText => _textureText;

    /// <summary>
    /// 按注册顺序把「种类 + 编号 + Mod名/类型名」折成一个 64 位哈希。
    /// FNV-1a：只为了发现"两端不一样"，不做安全用途，也不需要抗碰撞。
    /// </summary>
    internal static void Refresh()
    {
        var registry = LoaderHost.Current.Content;
        var hash = Basis;

        for (var i = 0; i < registry.Count; i++)
        {
            var descriptor = registry.DescriptorAt(i);
            var instance = registry.InstanceAt(i);

            hash = Mix(hash, (int)descriptor.Kind);
            hash = Mix(hash, descriptor.Type);
            hash = Mix(hash, instance.FullKey);
        }

        _value = hash;
        _text = hash.ToString("X16");
    }

    internal static void RefreshTextures()
    {
        if (_textureReady) return;
        lock (TextureLock)
        {
            if (_textureReady) return;
            var hash = Basis;
            try
            {
                foreach (var mod in LoaderHost.Current.Mods.Mods)
                {
                    if (mod is null || string.IsNullOrEmpty(mod.Directory)) continue;
                    var root = Path.GetFullPath(mod.Directory);
                    if (!Directory.Exists(root)) continue;

                    hash = Mix(hash, mod.Name);
                    foreach (var file in ImageFiles(root)) hash = MixFile(hash, root, file);
                }
            }
            catch (Exception e)
            {
                Safe.Warn("计算纹理指纹失败：" + Safe.Describe(e));
                hash = Mix(hash, "texture-fingerprint-error");
            }

            _textureValue = hash;
            _textureText = hash.ToString("X16");
            _textureReady = true;
        }
    }

    /// <summary>两端指纹不一致时给出可直接对账的一行文本。</summary>
    internal static string Describe(ulong remote) =>
        $"mod 内容指纹不一致：本端 {_text}，对端 {remote:X16}；两端的 mod 集合或装载顺序不同，配方 / 商店 / 掉落 / 物品编号可能错位";

    internal static string DescribeTexture(ulong remote) =>
        $"纹理指纹不一致：本端 {_textureText}，对端 {remote:X16}；两端的 mod 贴图文件不同";

    private static List<string> ImageFiles(string root)
    {
        var files = new List<string>();
        Collect(root, files);
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    private static void Collect(string directory, List<string> files)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (!ImageExtensions.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                files.Add(file);
            }
            foreach (var sub in Directory.EnumerateDirectories(directory)) Collect(sub, files);
        }
        catch (Exception)
        {
            files.Add(directory + "/#unreadable");
        }
    }

    private static ulong MixFile(ulong hash, string root, string path)
    {
        hash = Mix(hash, Path.GetRelativePath(root, path).Replace('\\', '/'));
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[8192];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    hash ^= buffer[i];
                    hash *= Prime;
                }
            }
        }
        catch (Exception)
        {
            hash = Mix(hash, "#unreadable");
        }

        return Mix(hash, -1);
    }

    private static ulong Mix(ulong hash, int value)
    {
        hash ^= (uint)value;
        hash *= Prime;
        return hash;
    }

    private static ulong Mix(ulong hash, string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            hash ^= text[i];
            hash *= Prime;
        }

        return Mix(hash, -1);
    }

    /// <summary>给日志用的一行摘要。</summary>
    internal static string Summary()
    {
        var registry = LoaderHost.Current.Content;
        return new StringBuilder(96).Append("内容 ").Append(registry.Count).Append(" 项，指纹 ").Append(_text)
            .Append("，纹理 ").Append(_textureReady ? _textureText : "-").ToString();
    }
}
