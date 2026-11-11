using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>
/// 稀有度管线：拾取色 / 悬停色表 / 前缀升降。
/// 没有实体、没有每帧路径，所以没有绑定表也没有上下文字段。
/// </summary>
internal sealed class RarityPipeline : ILoaderModule
{
    public static RarityPipeline Instance { get; } = new();

    public string Name => "rarity";

    private static IModLog Log => LoaderHost.Current.Log;

    public bool Install()
    {
        On.Terraria.GameContent.UI.ItemRarity.Initialize += Initialized;
        On.Terraria.Item.GetPopupRarityColor_int += PopupColor;
        On.Terraria.Item.Prefix_int_boolRef += Prefixed;

        Log.Info("[rarity] 已挂（颜色表 / 拾取色 / 前缀升降）");
        return true;
    }

    private static void Initialized(On.Terraria.GameContent.UI.ItemRarity.orig_Initialize orig)
    {
        orig();
        RarityTable.Write();

        foreach (var rarity in ModContent.GetContent<ModRarity>())
        {
            try
            {
                rarity.SetStaticDefaults();
            }
            catch (Exception e)
            {
                Log.Warn($"{rarity.Name}.SetStaticDefaults 重建时异常：{e.Message}");
            }
        }
    }

    private static Color PopupColor(On.Terraria.Item.orig_GetPopupRarityColor_int orig, int itemRarity)
    {
        var rarity = RarityTable.Of(itemRarity);
        return rarity is null ? orig(itemRarity) : RarityTable.ColorOf(itemRarity);
    }

    /// <summary>前缀挂上之后改稀有度：档位由原版的战力倍率折算，结果 clamp 到合法范围。</summary>
    private static bool Prefixed(On.Terraria.Item.orig_Prefix_int_boolRef orig, Item self, int prefixWeWant,
                                 out bool rolledPrefixIsTopTier)
    {
        var baseRare = 0;
        try
        {
            baseRare = self.rare;
        }
        catch (Exception e)
        {
            Safe.Report("rarity.prefix.read", e);
        }

        var applied = orig(self, prefixWeWant, out rolledPrefixIsTopTier);
        if (!applied || prefixWeWant == -3) return applied;

        var rarity = RarityTable.Of(baseRare);
        if (rarity is null) return applied;

        try
        {
            var value = 1f;
            if (self.prefix != 0)
            {
                self.TryGetPrefixStatMultipliersForItem(self.prefix, out _, out _, out _, out _, out _, out _, out _,
                    out _, out _, out value);
            }

            var offset = value >= 1.2f ? 2 : value >= 1.05f ? 1 : value <= 0.8f ? -2 : value <= 0.95f ? -1 : 0;
            var rare = rarity.GetPrefixedRarity(offset, value);

            // 越界的 rare 会让颜色表查找抛异常，而这里在拾取/绘制路径上：clamp 的代价只是颜色不对。
            if (rare > -11) rare = Math.Clamp(rare, -1, RarityTable.Count - 1);
            self.rare = rare;
        }
        catch (Exception e)
        {
            Safe.Report("rarity." + rarity.Name + ".GetPrefixedRarity", e);
        }

        return applied;
    }
}
