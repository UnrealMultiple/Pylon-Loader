using Terraria.IO;

namespace Pylon.ModLoader;

/// <summary>世界档案的作者面：读取某个 system 写进世界头旁车文件的数据（键为 <c>Mod.Uid/类型名</c>）。</summary>
public static class WorldFileDataTml
{
    public static bool TryGetHeaderData(this WorldFileData world, ModSystem system, out TagCompound data) =>
        SystemMapHooks.TryGetHeaderData(world, system, out data);

    public static bool TryGetHeaderData<T>(this WorldFileData world, out TagCompound data) where T : ModSystem =>
        TryGetHeaderData(world, ModContent.GetInstance<T>()!, out data);
}
