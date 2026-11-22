using System;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;

namespace PipelineTest.Content;

/// <summary>
/// 物品绘制四钩测试物品（B2 验证脚手架）：地上前 3 帧否决（记 skip 计数），
/// 之后每帧把 rotation ref 写回并写穿 Item.rotation（肉眼可见的慢转）；
/// 库存四钩只计数。计数断言在 ItemDrawTests（IDW 系）。
/// </summary>
public sealed class TestDrawHooksItem : ModItem
{
    public static int CtPreWorld, CtPostWorld, CtPreInv, CtPostInv, SkipCount, RefWrites;
    private static int _skipRemaining;

    public static void Reset()
    {
        CtPreWorld = CtPostWorld = CtPreInv = CtPostInv = SkipCount = RefWrites = 0;
        _skipRemaining = 3;
    }

    public override string TexturePath => "assets/items/TestItem.png";

    public override void SetDefaults()
    {
        Item.width = 20;
        Item.height = 20;
        Item.maxStack = 1;
        Item.value = 50;
        Item.rare = 1;
    }

    public override bool PreDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor,
                                        ref float rotation, ref float scale, int whoAmI)
    {
        CtPreWorld++;
        if (_skipRemaining > 0)
        {
            _skipRemaining--;
            SkipCount++;
            return false;
        }

        RefWrites++;
        // 手机版 Item 无 rotation 字段（DrawItem 每帧现算），写回只经 ref 生效：
        // 按帧序正弦摆动，肉眼可辨「ref 修改后的帧物品旋转可见变化」。
        rotation = MathF.Sin((float)CtPreWorld * 0.15f) * 0.9f;
        return true;
    }

    public override void PostDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor,
                                         float rotation, float scale, int whoAmI) => CtPostWorld++;

    public override bool PreDrawInInventory(SpriteBatch spriteBatch, Vector2 position, Rectangle frame,
                                            Color drawColor, Color itemColor, Vector2 origin, float scale)
    {
        CtPreInv++;
        return true;
    }

    public override void PostDrawInInventory(SpriteBatch spriteBatch, Vector2 position, Rectangle frame,
                                             Color drawColor, Color itemColor, Vector2 origin, float scale) => CtPostInv++;
}
