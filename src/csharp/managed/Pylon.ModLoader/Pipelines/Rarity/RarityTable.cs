using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria.GameContent.UI;

namespace Pylon.ModLoader;

/// <summary>
/// 稀有度表：类型 ↔ 实例、颜色查询、颜色表写回。
/// 安装期与每次游戏重建颜色表都走这里，所以它不依赖管线实例。
/// </summary>
internal static class RarityTable
{
    public const int VanillaCount = 12;

    internal static int Count => VanillaCount + ModContent.Count(ContentKind.Rarity);

    internal static ModRarity? Of(int type) =>
        type < VanillaCount ? null : ModContent.Find(ContentKind.Rarity, type) as ModRarity;

    /// <summary>绘制路径每帧读：作者抛错就用白色，不让整帧绘制炸掉。</summary>
    internal static Color ColorOf(int type)
    {
        var rarity = Of(type);
        if (rarity is null) return Color.White;

        try
        {
            return rarity.RarityColor;
        }
        catch (Exception e)
        {
            Safe.Report("rarity." + rarity.Name + ".RarityColor", e);
            return Color.White;
        }
    }

    internal static void StaticDefaults(IModLog log)
    {
        var rarities = ModContent.GetContent<ModRarity>();
        if (rarities.Count == 0) return;

        foreach (var rarity in rarities)
        {
            try
            {
                rarity.SetStaticDefaults();
            }
            catch (Exception e)
            {
                log.Warn($"{rarity.Name}.SetStaticDefaults 异常：{e.Message}");
            }
        }

        Write();
        log.Info($"[rarity] 安装：{rarities.Count} 个（类型 {VanillaCount}..{Count - 1}）");
    }

    /// <summary>游戏每次重建 _rarities 都会清掉 mod 的条目：这里整表补一遍。</summary>
    internal static void Write()
    {
        try
        {
            var table = ItemRarity._rarities;
            if (table is null) return;

            foreach (var rarity in ModContent.GetContent<ModRarity>()) table[rarity.Type] = ColorOf(rarity.Type);
        }
        catch (Exception e)
        {
            Safe.Report("rarity.table", e);
        }
    }
}
