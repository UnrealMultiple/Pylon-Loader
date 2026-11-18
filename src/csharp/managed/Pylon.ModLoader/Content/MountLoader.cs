using Terraria;
using Terraria.ID;

namespace Pylon.ModLoader;

/// <summary>坐骑静态查询：类型计数、按号反查、判断一份 MountData 是不是 mod 坐骑的。</summary>
public static class MountLoader
{
    public static int MountCount => MountID.Count;

    public static ModMount? GetMount(int type) => MountTable.Of(type);

    public static bool IsModMount(Mount.MountData data)
    {
        if (data is null) return false;

        foreach (var mount in ModContent.GetContent<ModMount>())
        {
            if (mount.MountData is not null && mount.MountData.Pointer == data.Pointer) return true;
        }

        return false;
    }
}
