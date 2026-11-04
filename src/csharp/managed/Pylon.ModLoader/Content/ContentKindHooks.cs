namespace Pylon.ModLoader;

/// <summary>占号之后必须同步给游戏计数器的种类（游戏中途重建表时按这个长度留槽）。</summary>
internal static class ContentKindHooks
{
    public static void OnRegistered(ModType type)
    {
        switch (type)
        {
            case ModMount:
                Terraria.ID.MountID.Count = Math.Max(Terraria.ID.MountID.Count, type.Type + 1);
                break;
            case ModPrefix prefix:
                Terraria.ID.PrefixID.Count = Math.Max(Terraria.ID.PrefixID.Count, type.Type + 1);
                PrefixTable.Register(prefix);
                break;
        }
    }
}
