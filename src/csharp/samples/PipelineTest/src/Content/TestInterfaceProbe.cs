#nullable enable
using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.GameContent;
using AuthorLayer = Pylon.ModLoader.GameInterfaceLayer;

namespace PipelineTest.Content;

/// <summary>
/// 界面层与系统绘制探针：钩子挂在 <see cref="PipelineTestMod"/> 的覆写上（这三个时机属于 Mod 本身）。
/// 作者层往屏幕左上角画一块半透明横幅（MagicPixel 纯色矩形），肉眼直接可见。
/// 所有开关默认关，不污染其他组；由 InterfaceTests 的世界组按场景逐个打开。
/// </summary>
public static class TestInterfaceProbe
{
    /// <summary>作者层的固定名字（外壳按名字缓存，换序/查找都靠它）。</summary>
    public const string PanelName = "PipelineTest: Panel";

    public static bool Counters;       // 计 Tiles/Interface/换序（Modify 恒计）
    public static bool InsertPanel;    // 插入作者层（画出可见横幅）
    public static bool MovePanelToEnd; // 换序：把作者层挪到列表末尾
    public static bool ThrowInModify;  // ModifyInterfaceLayers 注入异常（验失败语义）

    public static int ModifyCalls;
    public static int TilesCalls;
    public static int InterfaceCalls;
    public static int PanelDraws;

    /// <summary>横幅颜色随帧翻转（蓝/绿交替），肉眼能确认它是活的而不是一张静态图。</summary>
    public static bool FlipColor;

    /// <summary>最近一帧交给作者的层名序列（在所有改动之后抓，换序才看得到）。</summary>
    public static string OrderText = "";

    public static void Reset()
    {
        Counters = InsertPanel = MovePanelToEnd = ThrowInModify = FlipColor = false;
        ModifyCalls = TilesCalls = InterfaceCalls = PanelDraws = 0;
        OrderText = "";
    }

    public static void OnModifyInterfaceLayers(List<AuthorLayer> layers)
    {
        // Modify 恒计：Ready 组也要能观察到"菜单阶段管线每帧都在跑"。
        ModifyCalls++;
        if (!Counters) return;

        if (ThrowInModify) throw new Exception("界面层探针注入的异常");

        if (InsertPanel && !layers.Exists(l => l?.Name == PanelName))
        {
            layers.Insert(2, new AuthorLayer(PanelName, DrawBanner));
        }

        if (MovePanelToEnd)
        {
            var panel = layers.Find(l => l?.Name == PanelName);
            if (panel is not null)
            {
                layers.Remove(panel);
                layers.Add(panel);
            }
        }

        // 序列在所有改动之后抓：插入/换序的结果才能被断言看到。
        var names = new List<string>(layers.Count);
        for (var i = 0; i < layers.Count; i++) names.Add(layers[i]?.Name ?? "");
        OrderText = string.Join("|", names);
    }

    /// <summary>作者层的可见绘制体：屏幕左上角一块 420x36 的半透明横幅，颜色每 60 次翻转。</summary>
    private static bool DrawBanner()
    {
        PanelDraws++;

        var batch = Main.spriteBatch;
        var pixel = TextureAssets.MagicPixel?.Value;
        if (batch is null || pixel is null) return true;

        // UI 坐标系：左上角贴一点，避开原版的角落信息。
        var pos = new Vector2(24f, 96f);
        var rect = new Il2CppSystem.Nullable<Rectangle>(new Rectangle(0, 0, 420, 36));
        var color = FlipColor
            ? new Color(60, 200, 120, 160)   // 绿
            : new Color(70, 130, 220, 160);  // 蓝
        if (PanelDraws % 60 == 0) FlipColor = !FlipColor;

        batch.Draw(pixel, pos, rect, color, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
        return true;
    }

    public static void OnPostDrawTiles()
    {
        if (Counters) TilesCalls++;
    }

    public static void OnPostDrawInterface(SpriteBatch batch)
    {
        if (Counters) InterfaceCalls++;
    }
}
