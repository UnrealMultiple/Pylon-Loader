using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>在真实鱼线绘制期间传递竿端偏移与染色前颜色。</summary>
internal static class FishingLineHooks
{
    private static bool _installed;
    [ThreadStatic] private static Color? _lineColor;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        On.Terraria.Main.DrawProj_FishingLine += Draw;
        On.Terraria.Main.TryApplyingPlayerStringColor += StringColor;
    }

    private static void Draw(On.Terraria.Main.orig_DrawProj_FishingLine orig,
                             Projectile projectile, Player owner, ref float polePosX, ref float polePosY,
                             Vector2 mountedCenter)
    {
        var bobber = ProjectilePipeline.Instance.InstanceOf(projectile);
        var inventory = owner.inventory;
        var selected = owner.selectedItem;
        var held = inventory is not null && (uint)selected < (uint)inventory.Length ? inventory[selected] : null;
        var rod = held is null ? null : ItemPipeline.Instance.InstanceOf(held);
        var wantsBobber = bobber is not null && bobber.Plan.Has(HookId.ModifyFishingLine);
        var wantsRod = rod is not null && rod.Plan.Has(HookId.ModifyFishingLine);
        var outer = _lineColor;
        // 无覆写的嵌套绘制仍需使用它自己的原版颜色，不能继承外层浮标的修改。
        _lineColor = null;
        try
        {
            if (!wantsBobber && !wantsRod)
            {
                orig(projectile, owner, ref polePosX, ref polePosY, mountedCenter);
                return;
            }

            var offset = new Vector2(0f, 0f);
            var color = VanillaLineColor(held?.type ?? 0);
            if (wantsBobber)
            {
                try { bobber!.ModifyFishingLine(projectile, ref offset, ref color); }
                catch (Exception e) { Fail(bobber!, e); }
            }
            if (wantsRod)
            {
                try { rod!.ModifyFishingLine(held!, projectile, ref offset, ref color); }
                catch (Exception e) { Fail(rod!, e); }
            }
            _lineColor = color;
            orig(projectile, owner, ref polePosX, ref polePosY,
                new Vector2(mountedCenter.X + offset.X, mountedCenter.Y + offset.Y));
        }
        finally { _lineColor = outer; }
    }

    private static Color StringColor(On.Terraria.Main.orig_TryApplyingPlayerStringColor orig,
                                     int playerStringColor, Color stringColor) =>
        orig(playerStringColor, _lineColor ?? stringColor);

    private static Color VanillaLineColor(int type) => type switch
    {
        2294 => new Color(100, 180, 230, 100),
        2295 => new Color(250, 90, 70, 100),
        2293 => new Color(203, 190, 210, 100),
        2421 => new Color(183, 77, 112, 100),
        2422 => new Color(255, 226, 116, 100),
        4325 => new Color(200, 100, 100, 100),
        4442 => new Color(100, 100, 200, 100),
        _ => new Color(200, 200, 200, 100),
    };

    private static void Fail(ModType mod, Exception e) =>
        ContentErrors.Report($"[fishing] {mod.GetType().Name}.ModifyFishingLine 异常:{Safe.Describe(e)}");
}
