using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Textures;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>血腥粒子管线：生成 / 更新 / 颜色三条入口，加安装期的扩表。类型表在 <see cref="GoreTable"/>。</summary>
internal sealed class GorePipeline : ILoaderModule, ILoaderModuleRefresh
{
    public static GorePipeline Instance { get; } = new();

    private readonly HashSet<long> _reported = [];

    public string Name => "gore";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.Gore.NewGore += NewGore;
        On.Terraria.Gore.Update += Update;
        On.Terraria.Gore.GetAlpha += GetAlpha;

        Log.Info("[gore] 已挂（生成 / 更新 / 颜色）；绘制与 UpdateType 本版不接");
        return true;
    }

    public void Refresh() => GoreTable.Assign();

    /// <summary>安装期：扩平行表 → 发号 → 逐类型 SetStaticDefaults。</summary>
    internal void Build()
    {
        Grow();
        GoreTable.Assign();

        var count = 0;
        for (var type = GoreTable.Base; type < GoreTable.Total; type++)
        {
            var gore = GoreTable.Of(type);
            if (gore is null) continue;

            Safe.Run(gore.Name + ".SetStaticDefaults", gore.SetStaticDefaults);
            count++;
        }

        if (count > 0) Log.Info($"[gore] 安装：{count} 个（类型 {GoreTable.Base}..{GoreTable.Total - 1}）");
    }

    /// <summary>TextureAssets.Gore 与 GoreID.Sets 那几张平行表一次扩到位；扩表器留在 Active 里供维护期补扩。</summary>
    private static void Grow()
    {
        var total = GoreTable.Total;
        if (total <= GoreTable.Base) return;

        try
        {
            var grower = new TypeTables.TableGrower(TypeTables.TableKind.Gore, total);
            grower.NextBatch(total, Log.Info);
        }
        catch (Exception e)
        {
            Safe.Report("gore.grow", e);
        }
    }

    private int NewGore(On.Terraria.Gore.orig_NewGore orig, Il2CppMicrosoft.Xna.Framework.Vector2 position,
                        Il2CppMicrosoft.Xna.Framework.Vector2 velocity, int type, float scale)
    {
        var index = orig(position, velocity, type, scale);

        var gore = GoreTable.Of(type);
        if (gore is null || !GoreTable.MasksOf(type).Has(HookId.OnSpawn)) return index;
        if ((uint)index >= (uint)Main.gore.Length) return index;

        var spawned = Main.gore[index];
        if (spawned is null || !spawned.active || spawned.type != type) return index;

        try
        {
            gore.OnSpawn(spawned);
        }
        catch (Exception e)
        {
            Report(gore, HookId.OnSpawn, e);
        }

        return index;
    }

    private void Update(On.Terraria.Gore.orig_Update orig, Gore self)
    {
        var gore = GoreTable.Of(self.type);
        if (gore is null || !GoreTable.MasksOf(self.type).Has(HookId.Update))
        {
            orig(self);
            return;
        }

        try
        {
            if (!gore.Update(self)) return;
        }
        catch (Exception e)
        {
            // 吞掉之后照常走原版：粒子会自己淡出，不会因为每帧抛错而定在屏幕上。
            Report(gore, HookId.Update, e);
        }

        var borrowed = gore.UpdateType;
        if (borrowed <= 0)
        {
            orig(self);
            return;
        }

        // 借原版 AI：临时把自己的编号换成目标编号，原版更新跑完再换回来。
        // 用 try/finally 而不是"跑完再赋值"：原版更新里抛异常时也必须换回来，否则这一粒会以别人的编号留在世界上。
        var own = self.type;
        self.type = borrowed;
        try
        {
            orig(self);
        }
        finally
        {
            if (self.type == borrowed) self.type = own;
        }
    }

    private Color GetAlpha(On.Terraria.Gore.orig_GetAlpha orig, Gore self, Color newColor)
    {
        var gore = GoreTable.Of(self.type);
        if (gore is null || !GoreTable.MasksOf(self.type).Has(HookId.GetAlpha)) return orig(self, newColor);

        try
        {
            if (gore.GetAlpha(self, newColor) is { } color) return color;
        }
        catch (Exception e)
        {
            Report(gore, HookId.GetAlpha, e);
        }

        return orig(self, newColor);
    }

    private void Report(ModGore gore, HookId hook, Exception exception)
    {
        if (!_reported.Add(((long)gore.Type << 32) | (uint)hook)) return;
        ContentErrors.Report($"[gore] {gore.Name}.{hook} 异常：{Safe.Describe(exception)}");
    }
}
