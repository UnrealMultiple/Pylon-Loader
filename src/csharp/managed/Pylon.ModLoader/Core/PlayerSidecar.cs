using Terraria.IO;

namespace Pylon.ModLoader;

/// <summary>玩家侧车存档路径：<c>&lt;玩家文件&gt;.pl.json</c>（pl = Pylon Loader）；云存档不写。</summary>
internal static class PlayerSidecar
{
    internal static string? DataFile(PlayerFileData? data)
    {
        if (data is null || data.IsCloudSave) return null;
        var path = data.Path;
        return string.IsNullOrEmpty(path) ? null : path + ".pl.json";
    }
}
