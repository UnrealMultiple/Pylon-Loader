using System.IO.Compression;
using Pylon.ModLoader.Logging;
using Terraria;

namespace Pylon.ModLoader.SaveData;
/// <summary>
/// 世界侧车：全部 mod 数据进一个 <c>&lt;世界&gt;.wld.pl</c> —— GZip 压缩的 TagCompound 根。
/// 分节约定：banners / npcs / rooms / tiles / walls / tileentities / headers；
/// mod 级世界数据与 system 级世界数据（键 <c>Mod.Uid/名</c>）平铺在根上，世界头统一收在 headers 节。
/// 读写都走 tmp + 原子替换。
/// </summary>
internal static class WorldSidecar
{
    internal const string HeadersSection = "headers";

    internal static string? PathFor(string? worldPath = null)
    {
        var path = worldPath ?? Main.worldPathName;
        if (string.IsNullOrEmpty(path)) return null;
        if (path.EndsWith(".wld", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        return path + ".wld.pl";
    }

    internal static TagCompound Read(string? worldPath = null)
    {
        var file = PathFor(worldPath);
        if (file is null || !File.Exists(file)) return new TagCompound();

        try
        {
            using var stream = File.OpenRead(file);
            using var gzip = new GZipStream(stream, CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);
            return TagBinary.Read(reader);
        }
        catch (Exception e)
        {
            Safe.Once("sidecar.read:" + file, $"世界侧车读不出来，已忽略：{file}（{Safe.Describe(e)}）", true);
            return new TagCompound();
        }
    }

    internal static void Write(TagCompound root, string? worldPath = null)
    {
        var file = PathFor(worldPath);
        if (file is null) return;

        try
        {
            var directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temp = file + ".tmp";
            using (var stream = File.Create(temp))
            using (var gzip = new GZipStream(stream, CompressionLevel.Optimal))
            using (var writer = new BinaryWriter(gzip))
            {
                TagBinary.Write(writer, root);
            }

            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e)
        {
            Safe.Report("sidecar.write", e);
        }
    }
}
