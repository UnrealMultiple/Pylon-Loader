using Terraria;

namespace Pylon.ModLoader;

/// <summary>墙壁管线：挖 / 尘土 / 掉落 / 边框。每类型数据在 WallTable（密集 struct 数组）。</summary>
internal sealed class WallPipeline : ILoaderModule
{
    public static WallPipeline Instance { get; } = new();

    private readonly HashSet<int> _reported = [];

    public string Name => "wall";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.WorldGen.KillWall += Kill;
        On.Terraria.WorldGen.KillWall_MakeWallDust += MakeDust;
        On.Terraria.WorldGen.KillWall_DropItems += DropItems;
        On.Terraria.Framing.WallFrame += Frame;

        Log.Info("[wall] 已挂（挖 / 尘土 / 掉落 / 边框）；灰尘数量与声音本版无逐格入口");
        return true;
    }

    internal void Build() => WallTable.Build(Log);

    private void Kill(On.Terraria.WorldGen.orig_KillWall orig, int i, int j, bool fail)
    {
        var wall = WallTable.At(i, j);

        if (wall is not null && wall.HookEnabled(HookId.KillWall))
        {
            try
            {
                wall.KillWall(i, j, ref fail);
            }
            catch (Exception e)
            {
                Fail(wall, HookId.KillWall, e);
            }
        }

        orig(i, j, fail);
    }

    private void MakeDust(On.Terraria.WorldGen.orig_KillWall_MakeWallDust orig, int i, int j, Tile tileCache)
    {
        var wall = WallTable.At(i, j);
        if (wall is null || !wall.HookEnabled(HookId.CreateDust))
        {
            orig(i, j, tileCache);
            return;
        }

        var type = wall.DustType > 0 ? wall.DustType : 0;

        try
        {
            if (!wall.CreateDust(i, j, ref type)) return;
        }
        catch (Exception e)
        {
            Fail(wall, HookId.CreateDust, e);
        }

        orig(i, j, tileCache);
    }

    private void DropItems(On.Terraria.WorldGen.orig_KillWall_DropItems orig, int i, int j, Tile tileCache)
    {
        var wall = WallTable.At(i, j);
        if (wall is null)
        {
            DropDefault(i, j, tileCache);
            orig(i, j, tileCache);
            return;
        }

        if (wall.HookEnabled(HookId.Drop))
        {
            var type = 0;

            try
            {
                if (!wall.Drop(i, j, ref type)) return;
            }
            catch (Exception e)
            {
                Fail(wall, HookId.Drop, e);
            }

            if (type > 0)
            {
                Item.NewItem(i * 16, j * 16, 16, 16, type);
                orig(i, j, tileCache);
                return;
            }
        }

        DropDefault(i, j, tileCache);
        orig(i, j, tileCache);
    }

    private static void DropDefault(int i, int j, Tile tileCache)
    {
        var item = WallTable.DropOf(tileCache.wall);
        if (item > 0) Item.NewItem(i * 16, j * 16, 16, 16, item);
    }

    private void Frame(On.Terraria.Framing.orig_WallFrame orig, int i, int j, bool resetFrame)
    {
        var wall = WallTable.At(i, j);

        if (wall is not null && wall.HookEnabled(HookId.WallFrame))
        {
            var style = 0;
            var frameNumber = 0;

            try
            {
                if (!wall.WallFrame(i, j, resetFrame, ref style, ref frameNumber)) return;
            }
            catch (Exception e)
            {
                Fail(wall, HookId.WallFrame, e);
            }
        }

        orig(i, j, resetFrame);
    }

    private void Fail(ModWall wall, HookId hook, Exception exception)
    {
        if (!_reported.Add(HashCode.Combine(wall.Type, hook))) return;
        ContentErrors.Report($"[wall] {wall.GetType().Name}.{hook} 异常：{Safe.Describe(exception)}");
    }
}
