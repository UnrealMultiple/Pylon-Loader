using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>
/// 测试稀有度：标志色（洋红，一眼认出不是原版），GetPrefixedRarity 按档位升降。
/// </summary>
public sealed class TestRarity : ModRarity
{
    /// <summary>SetStaticDefaults 被调的标记（R1 用）。</summary>
    public static bool StaticDefaultsRan;

    public override Color RarityColor => new(255, 0, 200, 255);

    public override int GetPrefixedRarity(int offset, float valueMult) => Type + offset;

    public override void SetStaticDefaults() => StaticDefaultsRan = true;
}

/// <summary>第二个稀有度：验证两两不同号。</summary>
public sealed class TestRarity2 : ModRarity
{
    public override Color RarityColor => new(0, 200, 255, 255);
}

/// <summary>挂 TestRarity 的物品：rare = TestRarity.Type，供拾取色/颜色表断言。</summary>
public sealed class TestRarityItem : ModItem
{
    public override string DisplayName => "稀有度测试物品";

    public override void SetDefaults(Item item)
    {
        item.width = 20;
        item.height = 20;
        item.maxStack = 1;
        item.rare = ModContent.RarityType<TestRarity>();
        item.value = 5000;
    }
}