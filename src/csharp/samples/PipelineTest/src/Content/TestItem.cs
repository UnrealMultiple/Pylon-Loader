using System.Collections.Generic;
using System.Collections.ObjectModel;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;

namespace PipelineTest.Content;

/// <summary>测试物品：非常规数值 + 一行介绍 + ModifyTooltips 加一行，验证 tooltip 链不崩。</summary>
public sealed class TestItem : ModItem
{
    public override string DisplayName => "测试物品";
    public override string Tooltip => "这是一行测试介绍";

    public override void SetDefaults()
    {
        Item.width = 20;
        Item.height = 20;
        Item.maxStack = 9999;
        Item.value = 100;
        Item.rare = 1;

        // 近战武器：能挥舞，命中判定尺寸
        Item.damage = 15;
        Item.melee = true;
        Item.useTime = 20;
        Item.useAnimation = 20;
        Item.useStyle = (byte)Terraria.ID.ItemUseStyleID.Swing;
        Item.knockBack = 4f;
        Item.autoReuse = true;

        // 挥舞时发射弹幕（ Shoot 钩子链路）
        Item.shoot = Terraria.ID.ProjectileID.WoodenArrowFriendly;
        Item.shootSpeed = 8f;
    }

    // ---- tooltip 探针（TooltipTests 用）：开关默认全关，避免污染其它组 ----
    public static bool MutateTooltip;
    public static bool RemoveTooltipLine;
    public static int CtModifyTooltips;
    public static int CtPreDrawTooltip;
    public static int CtPostDrawTooltip;
    public static int CtPreDrawTooltipLine;
    public static int CtPostDrawTooltipLine;
    /// <summary>最后一次 ModifyTooltips 看到的行数（真机悬停时记录）。</summary>
    /// <summary>W2 记下的改行后行数（W3 对比用）。</summary>
    public static int MutatedLineCount;
    public static int LastLineCount = -1;
    public static int ProbeLineR = -1, ProbeLineG = -1, ProbeLineB = -1;

    public static void ResetTooltipProbe()
    {
        MutateTooltip = RemoveTooltipLine = false;
        CtModifyTooltips = CtPreDrawTooltip = CtPostDrawTooltip = 0;
        CtPreDrawTooltipLine = CtPostDrawTooltipLine = 0;
        LastLineCount = -1;
        ProbeLineR = ProbeLineG = ProbeLineB = -1;
    }

    public override void ModifyTooltips(List<TooltipLine> tooltips)
    {
        CtModifyTooltips++;

        if (RemoveTooltipLine && tooltips.Count > 1)
        {
            // 删最后一行（名字行在第 0，删它游戏会补）
            tooltips.RemoveAt(tooltips.Count - 1);
        }

        if (MutateTooltip)
        {
            if (tooltips.Count > 0) tooltips[0].Text = "[管线改写] " + tooltips[0].Text;
            tooltips.Add(new TooltipLine(Mod, "PipelineProbe", "[管线加行] 界面探针")
            {
                OverrideColor = new Il2CppMicrosoft.Xna.Framework.Graphics.Color(255, 40, 40, 255),
            });
        }
        LastLineCount = tooltips.Count;

    }

    public override bool PreDrawTooltip(ReadOnlyCollection<TooltipLine> lines, ref int x, ref int y)
    {
        CtPreDrawTooltip++;
        return true;
    }

    public override void PostDrawTooltip(ReadOnlyCollection<DrawableTooltipLine> lines) => CtPostDrawTooltip++;

    public override bool PreDrawTooltipLine(DrawableTooltipLine line, ref int yOffset)
    {
        CtPreDrawTooltipLine++;
        return true;
    }

    public override void PostDrawTooltipLine(DrawableTooltipLine line)
    {
        CtPostDrawTooltipLine++;
        if (line.Name == "PipelineProbe")
        {
            ProbeLineR = line.Color.R;
            ProbeLineG = line.Color.G;
            ProbeLineB = line.Color.B;
        }
    }

    public override bool Shoot(Player player, Terraria.DataStructures.EntitySource_ItemUse_WithAmmo? source,
                               Vector2 position, Vector2 velocity, int type, int damage, float knockBack)
    {
        Tests.ItemTests2.ShootCalled = true;
        // 发射自定义测试弹幕（Projectile 管线肉眼验证）：贴图为 assets/projectiles/TestProjectile.png
        var projType = ModContent.ProjectileType<TestProjectile>();
        Terraria.Projectile.NewProjectile(null, position, velocity,
            projType, damage, knockBack, player.whoAmI);
        return false; // 不走原版的弹药消耗路径
    }

}
