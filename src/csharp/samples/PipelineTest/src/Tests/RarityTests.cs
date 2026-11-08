using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent.UI;

namespace PipelineTest.Tests;

/// <summary>
/// 管线 30：Rarity —— 功能验证。不用 UI，直接调游戏入口：
/// - ItemRarity.Initialize()：重建颜色表 → 钩子应把 mod 颜色补写回 _rarities
/// - Item.GetPopupRarityColor(type)：拾取飘字色 → 钩子应返回 RarityColor
/// - SetStaticDefaults 在安装期被调（标记字段）
/// </summary>
internal static class RarityTests
{
    public static void Register() => PT.Group("rarity", Phase.Ready, Run);

    private static void Run()
    {
        var type = ModContent.RarityType<Content.TestRarity>();
        var type2 = ModContent.RarityType<Content.TestRarity2>();

        // R1 发号在原版之后（原版 12 个，VanillaCount = 12）
        PT.Case("RARITY-R1", c => c.True(type >= 12, $"type={type}（原版 12 个，mod 从 12 起）"));

        // R2 两个稀有度不同号
        PT.Case("RARITY-R2", c => c.True(type != type2, $"type1={type} type2={type2} 撞号"));

        // S1 SetStaticDefaults 被调（安装期 RarityTable.StaticDefaults 跑过；这里若未置位则主动触发一次重建再验）
        PT.Case("RARITY-S1", c =>
        {
            if (!Content.TestRarity.StaticDefaultsRan)
            {
                ItemRarity.Initialize(); // 游戏重建颜色表 → 钩子会再跑一遍 SetStaticDefaults
            }
            c.True(Content.TestRarity.StaticDefaultsRan, $"SetStaticDefaultsRan={Content.TestRarity.StaticDefaultsRan}");
        });

        // T1 功能：重建颜色表后，_rarities[type] == RarityColor（这是管线真正的功能：表被游戏清了能补回）
        PT.Case("RARITY-T1", c =>
        {
            ItemRarity.Initialize(); // 触发游戏重建 → 钩子补写
            var expected = new Content.TestRarity().RarityColor;
            var actual = ItemRarity._rarities[type];
            c.True(actual == expected, $"_rarities[{type}]={actual}（期望 {expected}，颜色表没补回）");
        });

        // T2 功能：拾取飘字色走钩子（GetPopupRarityColor 静态入口 → RarityTable.ColorOf）
        PT.Case("RARITY-T2", c =>
        {
            var expected = new Content.TestRarity().RarityColor;
            var actual = Item.GetPopupRarityColor(type);
            c.True(actual == expected, $"GetPopupRarityColor({type})={actual}（期望 {expected}）");
            // 原版稀有度不受污染
            var vanilla = Item.GetPopupRarityColor(1);
            c.True(vanilla == ItemRarity.GetColor(1), $"原版 1 号色 {vanilla} != 表色 {ItemRarity.GetColor(1)}");
        });

        // H1 功能：挂该稀有度的物品 SetDefaults 后 rare 保持（发号→物品挂载→表查询全链路）
        PT.Case("RARITY-H1", c =>
        {
            var item = new Item();
            item.SetDefaults(ModContent.TypeOf<Content.TestRarityItem>(), null);
            c.Eq(item.rare, type, "item.rare");
        });

        // E1 功能：越界/未注册的 mod 稀有度号查表返回 null 不抛；前缀升降 clamp 到合法区间
        PT.Case("RARITY-E1", c =>
        {
            c.NotNull(ModContent.Find(ContentKind.Rarity, type), "Find(已注册号)");
            c.True(ModContent.Find(ContentKind.Rarity, 9999) is null, "Find(9999) 应回 null");
            c.NoThrow(static () => Item.GetPopupRarityColor(9999), "GetPopupRarityColor(9999) 不抛");

            // GetPrefixedRarity：offset=+2 时应返回 Type+2，且 Prefixed 链 clamp 不越界
            var rarity = ModContent.GetInstance<Content.TestRarity>();
            c.Eq(rarity.GetPrefixedRarity(2, 1.5f), type + 2, "GetPrefixedRarity(+2)");
        });
    }
}