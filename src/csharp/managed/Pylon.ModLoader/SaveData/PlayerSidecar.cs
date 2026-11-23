using System.IO.Compression;
using Pylon.ModLoader.Logging;
using Terraria.IO;

namespace Pylon.ModLoader.SaveData;
/// <summary>
/// 玩家侧车：<c>&lt;玩家文件&gt;.pl</c>—— GZip 压缩的 TagCompound 二进制，
/// ModPlayer 存读 / 发型 / mod 物品与前缀共用；云存档不写；写盘走 tmp + 原子替换。
/// </summary>
internal static class PlayerSidecar
{
    internal static string? DataFile(PlayerFileData? data)
    {
        if (data is null || data.IsCloudSave) return null;
        var path = data.Path;
        return string.IsNullOrEmpty(path) ? null : path + ".pl";
    }

    internal static TagCompound Read(string? file)
    {
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
            Safe.Once("playersidecar.read:" + file, $"玩家侧车读不出来，已忽略：{file}（{Safe.Dump(e)}）", true);
            return new TagCompound();
        }
    }

    internal static void Write(string? file, TagCompound root)
    {
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
            Safe.Report("playersidecar.write", e);
        }
    }
}
