using System;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
using Terraria.Graphics;

namespace PipelineTest.Content;

/// <summary>
/// DrawEffects 改色 → ModifyDrawInfo 读回来；TransformDrawData 读缓存条数；Zoom 改完读 Main.GameViewMatrix.Zoom）。
/// 所有开关默认关闭：探针常驻，但只在用例里生效。
/// </summary>
public static class DrawInfoProbe
{
    public static bool TintOn;
    public static bool ZoomOn;
    public static bool ZoomInvalidOn;

    /// <summary>标量重载给的倍率；向量重载给的值。</summary>
    public static float ZoomValue = 1.75f;
    public static Vector2 ZoomVectorValue = new(1.25f, 1.25f);
    public static bool UseVectorZoom;

    public static int EffectsCalls, InfoCalls, TransformCalls, DrawPlayerCalls, ScreenPosCalls;
    public static int ZoomScalarCalls, ZoomVectorCalls;

    public static int ShirtRed = -1, PantsRed = -1;
    public static int TransformCache = -1;
    public static bool CameraSeen;
    public static float ScreenDeltaSeen;

    public static void Reset()
    {
        TintOn = false;
        ZoomOn = false;
        ZoomInvalidOn = false;
        UseVectorZoom = false;
        ZoomValue = 1.75f;
        ZoomVectorValue = new Vector2(1.25f, 1.25f);

        EffectsCalls = InfoCalls = TransformCalls = DrawPlayerCalls = ScreenPosCalls = 0;
        ZoomScalarCalls = ZoomVectorCalls = 0;

        ShirtRed = PantsRed = -1;
        TransformCache = -1;
        CameraSeen = false;
        ScreenDeltaSeen = 0f;
    }
}

/// <summary>六个绘制信息时机各记一笔。</summary>
public sealed class TestDrawInfoPlayer : ModPlayer
{
    public override void DrawEffects(PlayerDrawSet drawInfo, ref float r, ref float g, ref float b,
                                     ref float a, ref bool fullBright)
    {
        DrawInfoProbe.EffectsCalls++;

        if (!DrawInfoProbe.TintOn) return;

        // 只改红通道：衣服与裤子的红分量应变成 0，其它通道不变。
        r = 0f;
    }

    public override void ModifyDrawInfo(ref PlayerDrawSet drawInfo)
    {
        DrawInfoProbe.InfoCalls++;
        DrawInfoProbe.ShirtRed = drawInfo.colorShirt.R;
        DrawInfoProbe.PantsRed = drawInfo.colorPants.R;
    }

    public override void TransformDrawData(ref PlayerDrawSet drawInfo)
    {
        DrawInfoProbe.TransformCalls++;
        DrawInfoProbe.TransformCache = drawInfo.DrawDataCacheCount;
    }

    public override void DrawPlayer(Camera camera)
    {
        DrawInfoProbe.DrawPlayerCalls++;
        DrawInfoProbe.CameraSeen |= camera is not null;
    }

    public override void ModifyScreenPosition()
    {
        DrawInfoProbe.ScreenPosCalls++;
        DrawInfoProbe.ScreenDeltaSeen = Main.screenPosition.X;
    }

    public override void ModifyZoom(ref float zoom)
    {
        DrawInfoProbe.ZoomScalarCalls++;

        if (DrawInfoProbe.ZoomInvalidOn) zoom = 0f;          // 非法值：管线应保留原值
        else if (DrawInfoProbe.ZoomOn) zoom = DrawInfoProbe.ZoomValue;
    }
}