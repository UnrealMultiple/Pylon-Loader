using Pylon.ModLoader.Textures;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>墙壁管线：挖 / 尘土 / 声音 / 掉落 / 边框。每类型数据在 WallTable（密集 struct 数组）。</summary>
internal sealed class WallPipeline : ILoaderModule
{
    public static WallPipeline Instance { get; } = new();

    private readonly HashSet<int> _reported = [];

    /// <summary>这一击还要出几粒尘土：NumDust 定预算，MakeWallDust 每粒扣 1。</summary>
    [ThreadStatic] private static int _dustBudget;

    /// <summary>这一击是不是"只出尘土、墙还在"（NumDust / KillSound 要用）。</summary>
    [ThreadStatic] private static bool _failing;

    public string Name => "wall";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.WorldGen.KillWall += Kill;
        On.Terraria.WorldGen.KillWall_MakeWallDust += MakeDust;
        On.Terraria.WorldGen.KillWall_PlaySounds += PlaySounds;
        On.Terraria.WorldGen.KillWall_DropItems += DropItems;
        On.Terraria.Framing.WallFrame += Frame;

        Log.Info("[wall] 已挂（挖 / 尘土 / 声音 / 掉落 / 边框）");
        return true;
    }

    internal void Build()
    {
        WallTable.Build(Log);
        InstallCounts();
    }

    /// <summary>
    /// 墙表扩完之后的收尾：<c>WallID.Count</c> 写成新总数，<c>Main.wallBlend</c> 的新槽填成自身。
    /// 少了这两步的表现是"<c>WorldGen.PlaceWall</c> 对 mod 墙类型静默失败"——原版放墙开头就拿
    /// <c>WallID.Count</c> 当上界挡越界类型，正因为如此玩家也永远放不下 mod 墙。
    /// </summary>
    private static void InstallCounts()
    {
        var total = VanillaCounts.Wall + ModContent.Count(ContentKind.Wall);
        if (total <= VanillaCounts.Wall) return;

        var written = TypeTables.WriteStaticCount("Terraria.ID", "WallID", "Count", total);
        var blended = TypeTables.FillWallBlend(VanillaCounts.Wall, total);

        Log.Info($"[wall] WallID.Count → {total}（{(written ? "写入成功" : "写入失败")}），wallBlend 补 {blended} 槽");
    }

    private void Kill(On.Terraria.WorldGen.orig_KillWall orig, int i, int j, bool fail)
    {
        var wall = WallTable.At(i, j);

        if (wall is null)
        {
            orig(i, j, fail);
            return;
        }

        if (wall.HookEnabled(HookId.KillWall))
        {
            var failing = fail;
            try
            {
                wall.KillWall(i, j, ref failing);
            }
            catch (Exception e)
            {
                Fail(wall, HookId.KillWall, e);
            }

            fail = failing;
        }

        // 原版默认：只出尘土的 1 粒、真挖掉的 3 粒；作者用 NumDust 改。
        var budget = fail ? 1 : 3;
        if (wall.HookEnabled(HookId.NumDust))
        {
            try
            {
                wall.NumDust(i, j, fail, ref budget);
            }
            catch (Exception e)
            {
                Fail(wall, HookId.NumDust, e);
            }
        }

        var previousBudget = _dustBudget;
        var previousFailing = _failing;
        _dustBudget = Math.Max(0, budget);
        _failing = fail;

        try
        {
            orig(i, j, fail);
        }
        finally
        {
            _dustBudget = previousBudget;
            _failing = previousFailing;
        }
    }

    private void MakeDust(On.Terraria.WorldGen.orig_KillWall_MakeWallDust orig, int i, int j, Tile tileCache)
    {
        var wall = WallTable.Of(tileCache.wall);
        if (wall is null || !wall.HookEnabled(HookId.CreateDust))
        {
            orig(i, j, tileCache);
            return;
        }

        // 预算用完就不出（原版每粒调一次这里）。
        if (_dustBudget <= 0) return;
        _dustBudget--;

        var type = wall.DustType;
        try
        {
            if (!wall.CreateDust(i, j, ref type) || type <= 0) return;
        }
        catch (Exception e)
        {
            Fail(wall, HookId.CreateDust, e);
            return;
        }

        Dust.NewDust(new Il2CppMicrosoft.Xna.Framework.Vector2(i * 16, j * 16), 16, 16, type, 0, 0, 0, default, 1);
    }

    private void PlaySounds(On.Terraria.WorldGen.orig_KillWall_PlaySounds orig, int i, int j, Tile tileCache)
    {
        var wall = WallTable.Of(tileCache.wall);
        if (wall is null || !wall.HookEnabled(HookId.KillSound))
        {
            orig(i, j, tileCache);
            return;
        }

        var play = true;
        try
        {
            play = wall.KillSound(i, j, _failing);
        }
        catch (Exception e)
        {
            Fail(wall, HookId.KillSound, e);
        }

        if (!play) return;

        // HitSound 是原版音效号；作者没给（<=0）就用游戏的默认声音。
        if (wall.HitSound > 0)
        {
            Terraria.Audio.SoundEngine.PlaySound(wall.HitSound, i * 16, j * 16, 1, 1f, 0f);
            return;
        }

        orig(i, j, tileCache);
    }

    private void DropItems(On.Terraria.WorldGen.orig_KillWall_DropItems orig, int i, int j, Tile tileCache)
    {
        var wall = WallTable.Of(tileCache.wall);
        if (wall is null)
        {
            orig(i, j, tileCache);
            return;
        }

        // 默认掉落（放置物品或 ItemDrop 登记）先填好，作者在 Drop 里可以换。
        var type = WallTable.DropOf(tileCache.wall);

        if (wall.HookEnabled(HookId.Drop))
        {
            try
            {
                if (!wall.Drop(i, j, ref type)) return;
            }
            catch (Exception e)
            {
                Fail(wall, HookId.Drop, e);
            }
        }

        if (type <= 0) return;

        Item.NewItem(i * 16, j * 16, 16, 16, type);
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
